// @vitest-environment jsdom
import { cleanup, render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it } from "vitest";
import { ContextSummaryCard } from "./ContextSummaryCard";
import type { ContextBreakdown } from "../port/port";
import "./testkit";

afterEach(cleanup);

const breakdown = (over: Partial<ContextBreakdown> = {}): ContextBreakdown => ({
  used: 40_000, window: 1_000_000, compact_at: 160_000, boundary: "capacity", capacity_at: 800_000,
  system: 5_000, tools: 6_000, user: 2_000, reply: 3_000, output: 24_000,
  project_memory: 0,
  largest: [
    { class: "output", tool: "read_file", tokens: 12_000 },
    { class: "output", tool: "grep", tokens: 8_000, active: true },
    { class: "user", tokens: 1_500 },
  ],
  ...over,
});
const card = (context: ContextBreakdown) => <ContextSummaryCard context={context} mcp={[]} percent={4} onManage={() => {}} />;
const toggle = (cls: string) => document.querySelector<HTMLButtonElement>(`[data-action="context.largest"][data-value="${cls}"]`);

it("opens a class to the messages that made it heavy, naming the tool and the running turn", async () => {
  const user = userEvent.setup();
  render(card(breakdown()));
  expect(document.querySelector(".chrome-context-items")).toBeNull();
  expect(toggle("output")?.getAttribute("aria-expanded")).toBe("false");

  await user.click(toggle("output")!);
  const list = document.querySelector(".chrome-context-items")!;
  expect(toggle("output")?.getAttribute("aria-expanded")).toBe("true");
  expect(list.textContent).toContain("read_file");
  expect(list.textContent).toContain("grep");
  expect(list.querySelector(".head")?.textContent).toContain("工具输出");
  expect(list.querySelectorAll("li:not(.head)")).toHaveLength(2);
  expect(list.querySelectorAll("li:not(.head)")[1].textContent).toContain("进行中的回合");
});

it("keeps one class open at a time", async () => {
  const user = userEvent.setup();
  render(card(breakdown()));
  await user.click(toggle("output")!);
  await user.click(toggle("user")!);
  expect(document.querySelectorAll(".chrome-context-items")).toHaveLength(1);
  expect(toggle("output")?.getAttribute("aria-expanded")).toBe("false");
  expect(toggle("user")?.getAttribute("aria-expanded")).toBe("true");
  await user.click(toggle("user")!);
  expect(document.querySelector(".chrome-context-items")).toBeNull();
});

// Project memory is absent from this fixture, so the card draws five classes:
// a class nobody loaded has no row to give a figure to.
it("offers no list for a class with no holders named, nor at all from a kernel that names none", () => {
  render(card(breakdown()));
  expect(toggle("reply")).toBeNull();
  expect(toggle("system")).toBeNull();
  cleanup();
  render(card(breakdown({ largest: undefined })));
  expect(document.querySelector('[data-action="context.largest"]')).toBeNull();
  // Five classes with a figure, plus the idle row; each is plain text because
  // none of them can open without holders to name.
  expect(document.querySelectorAll(".chrome-context-parts b")).toHaveLength(6);
});

it("says where the window folds on its own, beside the capacity", () => {
  render(card(breakdown({ window: 128_000, compact_at: 115_000 })));
  const total = document.querySelector(".chrome-context-total")!;
  expect(total.textContent).toContain("模型容量");
  expect(total.textContent).toContain("到 115K 自动压缩");
});

it("says nothing about a fold the kernel has no threshold for", () => {
  render(card(breakdown({ compact_at: 0 })));
  expect(document.querySelector(".chrome-context-total")!.textContent).not.toContain("自动压缩");
});

// The card answers the same question as the rail's legend, so it has to name the
// same rows: what is taken, what the fold left behind, and how much is left.
// It listed six classes and no remainder, so a session at 25% of its fold point
// showed rows summing to the whole bar.
it("names the folded history and the capacity still idle", () => {
  render(card(breakdown({ digest: 8_000 })));

  const rows = [...document.querySelectorAll(".chrome-context-parts > div")].map((row) => row.textContent ?? "");
  expect(rows.some((r) => r.includes("折叠历史") && r.includes("8K"))).toBe(true);
  // 160k fold point less 40k used is 120k.
  expect(rows.some((r) => r.includes("空闲") && r.includes("120K"))).toBe(true);
});

it("scales the bar against the fold point, so the idle slice is visible", () => {
  render(card(breakdown()));
  const slices = [...document.querySelectorAll<HTMLElement>(".chrome-context-bar > i")];
  const idle = slices.find((i) => i.dataset.part === "idle");
  expect(idle).toBeTruthy();
  // 120k of a 160k ceiling.
  expect(Number.parseFloat(idle!.style.width)).toBeCloseTo(75, 0);
});

it("shows no idle row once the capacity is spent", () => {
  render(card(breakdown({ used: 160_000 })));
  const rows = [...document.querySelectorAll(".chrome-context-parts > div")].map((row) => row.textContent ?? "");
  expect(rows.some((r) => r.includes("空闲"))).toBe(false);
});
