import { describe, expect, it } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import { Context } from "./Context";
import type { ContextBreakdown } from "../../port/port";

// A session that folds at 160k, which is 16% of a 1M window. Everything here turns on the
// two numbers being different.
const wide = (over: Partial<ContextBreakdown> = {}): ContextBreakdown => ({
  used: 82_000, window: 1_000_000, compact_at: 160_000,
  boundary: "capacity", capacity_at: 800_000,
  system: 20_000, tools: 12_000, user: 10_000, reply: 20_000, output: 20_000,
  project_memory: 0,
  ...over,
});

const draw = (ctx: ContextBreakdown) => renderToStaticMarkup(<Context ctx={ctx} legend />);

// The bar's width is the whole claim: it is what a reader takes as "how far
// along am I", and it was measured against the wrong number.
const width = (html: string) => {
  const bars = html.match(/class="ctxbar"[^>]*>(.*?)<\/div>/s)?.[1] ?? "";
  return [...bars.matchAll(/width:\s*([\d.]+)%/g)].reduce((a, m) => a + Number(m[1]), 0);
};

describe("the context gauge's denominator", () => {
  // The defect this panel was carrying: 82k of 1M reads as 8% full, and the
  // session compacts at 160k with the bar still nearly empty. The reader
  // concludes the host folded for no reason.
  it("counts down to the fold point, not to the window", () => {
    expect(width(draw(wide()))).toBeCloseTo(51.25, 1);
  });

  it("names the deadline and what share of it is gone", () => {
    const html = draw(wide());
    expect(html).toContain("下次维护");
    expect(html).toContain("160k");
    expect(html).toContain("51%");
  });

  // Capacity does not disappear: it answers a different question — whether the
  // model is simply too small — and it is the only place a relay's wrong window
  // can be corrected.
  it("keeps the window as a second, quieter figure", () => {
    const html = draw(wide());
    expect(html).toContain("模型容量");
    expect(html).toContain("ctxcapbar");
    expect(html).toContain("1.0M");
  });

  // A full bar at the fold point and a full bar at the window are the same
  // pixels saying different things; only one of them is a deadline.
  it("does not let the capacity bar borrow the maintenance reading", () => {
    const html = draw(wide());
    const cap = html.match(/class="ctxcapbar"[^>]*>.*?width:\s*([\d.]+)%/s)?.[1];
    expect(Number(cap)).toBeCloseTo(8.2, 1);
  });
});

// The fold point was a bare number on a screen whose other number was twenty
// times larger. Nothing said which of the two bounds produced it, so the reader
// supplied the only explanation available: that the host folds for no reason.
describe("why the fold point is where it is", () => {
  it("marks the fold point on the window it is a fraction of", () => {
    const html = draw(wide());
    const notch = html.match(/class="ctxcapbar"[^>]*>.*?left:\s*([\d.]+)%/s)?.[1];
    expect(Number(notch)).toBeCloseTo(16, 1);
  });

  // A fold at the window's own share explains itself; repeating it there would
  // be a footnote on every session that never needed one.
  it("stays silent when the window's own share is what fires", () => {
    const html = draw(wide({ window: 200_000, compact_at: 160_000, boundary: "capacity", capacity_at: 160_000 }));
    expect(html).not.toContain("ctxwhy");
  });
});

describe("what the gauge says as the fold point approaches", () => {
  // The kernel tells the model to work narrower at 75% of the trigger and to
  // land what it knows at 92%. The panel reuses those, so the screen and the
  // conversation are never under two different pressures.
  it("stays quiet below the kernel's first rung", () => {
    const html = draw(wide({ used: 100_000 }));
    expect(html).not.toContain('data-press');
  });

  it("says the model has been told to narrow at the first rung", () => {
    const html = draw(wide({ used: 124_000 }));
    expect(html).toContain('data-press="near"');
    expect(html).toContain("收窄");
  });

  it("says maintenance is close at the second", () => {
    const html = draw(wide({ used: 150_000 }));
    expect(html).toContain('data-press="soon"');
  });

  // Compaction is routine maintenance. Painted as a warning it reads as a
  // malfunction, and every long session looks broken.
  it("never dresses routine maintenance as a fault", () => {
    const html = draw(wide({ used: 158_000 }));
    expect(html).not.toContain('data-lvl="warn"');
    expect(html).not.toContain('data-lvl="err"');
  });
});

describe("when there is no fold point to count down to", () => {
  // A ratio placed past the window is how maintenance is retired. There is no
  // deadline left, so inventing one would be a countdown to nothing.
  it("draws the window alone once the fold point is out of reach", () => {
    const html = draw(wide({ compact_at: 1_200_000 }));
    expect(html).not.toContain("下次维护");
    // The one ceiling left still says which one it is. What it must not do is
    // call itself a deadline.
    expect(html).toContain("模型容量");
    expect(width(html)).toBeCloseTo(8.2, 1);
  });

  // An undeclared window is what turns maintenance off entirely, and it says so
  // rather than drawing a gauge with no denominator.
  it("keeps saying why an undeclared window draws nothing", () => {
    const html = draw(wide({ window: 0, compact_at: 0 }));
    expect(html).toContain("不会自动压缩");
    expect(html).not.toContain("ctxbar");
  });
});

// One usage figure, two ceilings, each answering for itself. Merging them into
// a single percentage of something unstated is the failure this guards: "6%"
// alone cannot say whether it is 6% of the fold point or of the window.
describe("usage is one number and the ceilings are two questions", () => {
  const usage = (html: string) => [...html.matchAll(/82k/g)].length;

  it("draws the usage figure once", () => {
    expect(usage(draw(wide()))).toBe(1);
  });

  it("keeps both ceilings, each with its own denominator named", () => {
    const html = draw(wide());
    expect(html).toContain("下次维护");
    expect(html).toContain("160k");
    expect(html).toContain("模型容量");
    expect(html).toContain("1.0M");
  });

  it("gives each ceiling its own share, not one shared percentage", () => {
    const html = draw(wide());
    expect(html).toContain("51%");   // of the fold point
    expect(html).toContain("8%");    // of the window
  });
});

// The legend's rows and the header percentage answer the same question about the
// same prompt. When the classes were priced at a different rate from the gauge,
// the rows added to 100% of one sum while the header named a share of another,
// and the panel offered no way to see which.
describe("the legend reads each class against the capacity, not against what is used", () => {
  const rows = (html: string) =>
    [...html.matchAll(/class="r"[^>]*title="[^"]*">\s*<i[^>]*><\/i>\s*<span class="t">([^<]*)<\/span>\s*<em>(\d+)%<\/em>/g)]
      .map((m) => ({ label: m[1], share: Number(m[2]) }));

  // A row answers "how much of the ceiling does this class take", which is the
  // same question the bar above it answers. Read against the usage figure it
  // answered "how much of what I already used", so every row summed to 100%
  // while the bar said the session was a fifth full.
  it("reports each class against the fold point, so the rows sum to the bar", () => {
    const html = draw(wide());
    const taken = rows(html).filter((p) => p.label !== "空闲");
    expect(taken.length).toBeGreaterThan(0);
    const total = taken.reduce((a, p) => a + p.share, 0);
    // 82k used of a 160k fold point; each row is rounded on its own, so the
    // sum lands within a point or two of the bar rather than on it exactly.
    expect(total).toBeGreaterThanOrEqual(50);
    expect(total).toBeLessThanOrEqual(54);
  });

  it("gives the system prompt its share of the ceiling, not of the usage", () => {
    // 20k of a 160k fold point is twelve and a half percent, not a quarter.
    const html = draw(wide());
    expect(rows(html).find((p) => p.label === "系统提示")?.share).toBe(13);
  });

  it("names what the classes do not account for, still against the ceiling", () => {
    const html = draw(wide({
      used: 100_000,
      system: 10_000, tools: 10_000, user: 10_000, reply: 10_000, output: 10_000,
    }));
    expect(html).toContain("未计入");
    // 50k unaccounted of a 160k ceiling is 31%, not the 50% of usage.
    expect(rows(html).find((p) => p.label === "未计入")?.share).toBe(31);
  });

  it("shows no remainder when the classes already add up", () => {
    const html = draw(wide({ used: 82_000 }));
    expect(rows(html).find((p) => p.label === "未计入")).toBeUndefined();
  });

  // The rows say what is taken; a reader who wants to know how much is left has
  // to subtract from a ceiling named in another row. Idle is that remainder, in
  // the same units, so the legend adds up to the whole capacity.
  it("names the capacity the session has not used yet", () => {
    const html = draw(wide());
    const idle = rows(html).find((p) => p.label === "空闲");
    expect(idle).toBeDefined();
    // 160k fold point less 82k used is 49%.
    expect(idle?.share).toBe(49);
  });

  it("makes the legend add up to the whole capacity, not just to what is used", () => {
    const html = draw(wide());
    const total = rows(html).reduce((a, p) => a + p.share, 0);
    // Each row is rounded on its own, so a point or two of drift is expected.
    expect(total).toBeGreaterThanOrEqual(98);
    expect(total).toBeLessThanOrEqual(103);
  });

  it("shows no idle row once the capacity is spent", () => {
    const html = draw(wide({ used: 160_000 }));
    expect(rows(html).find((p) => p.label === "空闲")).toBeUndefined();
  });
});
