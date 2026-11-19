// @vitest-environment jsdom
import { cleanup, render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { Context } from "./panels/Context";
import { ContextSummaryCard } from "./ContextSummaryCard";
import type { ContextBreakdown } from "../port/port";
import "./testkit";

afterEach(cleanup);

// Project memory rides the user turn, so a breakdown that folded it back into
// `user` is the defect these cases describe.
const breakdown = (over: Partial<ContextBreakdown> = {}): ContextBreakdown => ({
  used: 40_000, window: 1_000_000, compact_at: 160_000, boundary: "capacity", capacity_at: 800_000,
  system: 5_000, tools: 6_000, project_memory: 4_000, user: 2_000, reply: 3_000, output: 24_000,
  project_memory_files: [
    { path: "REASONIX.md", scope: "project", tokens: 3_000 },
    { path: "AGENTS.md", scope: "project", tokens: 1_000 },
  ],
  ...over,
});

const draw = (ctx: ContextBreakdown) => render(<Context ctx={ctx} legend />);
const card = (ctx: ContextBreakdown) => render(
  <ContextSummaryCard context={ctx} mcp={[]} percent={4} onManage={() => {}} />,
);

// A block drawn in the bar but absent from the legend reads as a colour nobody
// accounts for, so both carry the class.
describe("project memory in the context panel", () => {
  it("draws its own slice of the bar, named in the legend", () => {
    const { container } = draw(breakdown());
    expect(container.querySelector('.ctxbar > i[data-p="memory"]')).not.toBeNull();
    expect(container.textContent).toContain("项目记忆");
  });

  // The class exists only where there is a block. A zero-width slice with its
  // own legend row would suggest rules this session does not have.
  it("stays out of both when nothing is loaded", () => {
    const { container } = draw(breakdown({ project_memory: 0, project_memory_files: [] }));
    expect(container.querySelector('.ctxbar > i[data-p="memory"]')).toBeNull();
    expect(container.textContent).not.toContain("项目记忆");
  });
});

// "4K tokens of project memory" says to edit something; it does not say what.
describe("opening project memory", () => {
  it("names every loaded file with its own cost", async () => {
    const user = userEvent.setup();
    const { container } = draw(breakdown());
    expect(container.querySelector(".ctxfiles")).toBeNull();

    const toggle = container.querySelector<HTMLButtonElement>('[data-action="context.memory-files"]');
    expect(toggle?.getAttribute("aria-expanded")).toBe("false");
    await user.click(toggle!);

    const rows = [...container.querySelectorAll(".ctxfiles li")];
    expect(rows).toHaveLength(2);
    expect(container.textContent).toContain("REASONIX.md");
    expect(container.textContent).toContain("AGENTS.md");
    expect(toggle?.getAttribute("aria-expanded")).toBe("true");
  });

  // A class with nothing under it gets a plain figure, never a control that
  // opens onto an empty list.
  it("offers no control when no file was loaded", () => {
    const { container } = draw(breakdown({ project_memory_files: [] }));
    expect(container.querySelector('[data-action="context.memory-files"]')).toBeNull();
  });
});

// The two places a person reads the context must not disagree about it.
describe("project memory in the composer card", () => {
  it("lists it among the classes and opens to the files", async () => {
    const user = userEvent.setup();
    const { container } = card(breakdown());
    expect(container.textContent).toContain("项目记忆");

    const toggle = container.querySelector<HTMLButtonElement>('[data-action="context.memory-files"]');
    await user.click(toggle!);
    const rows = [...container.querySelectorAll(".chrome-context-items li")];
    expect(rows.map((r) => r.textContent)).toEqual(
      expect.arrayContaining([expect.stringContaining("REASONIX.md"), expect.stringContaining("AGENTS.md")]),
    );
  });

  // The class's own figure is what was measured, not a second estimate.
  it("shows the measured class rather than recomputing it", () => {
    const { container } = card(breakdown({ project_memory: 4_000 }));
    expect(container.textContent).toContain("4K");
  });
});
