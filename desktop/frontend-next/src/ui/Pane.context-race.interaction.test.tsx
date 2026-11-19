// @vitest-environment jsdom
import { cleanup, render, waitFor } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import "./testkit";
import { Pane } from "./Pane";
import { MockPort } from "../port/mock";
import type { RuntimeView } from "../port/hub";
import type { ContextBreakdown } from "../port/port";

afterEach(cleanup);

const rt = { id: "p1", root: "/w", name: "w" } as RuntimeView;

const CONTEXT: ContextBreakdown = {
  used: 42_000, window: 128_000, compact_at: 115_200, boundary: "capacity",
  capacity_at: 115_200, system: 5_000, tools: 7_000, project_memory: 0,
  user: 10_000, reply: 10_000, output: 10_000,
};

it("a superseded read cannot take away what a newer one settled on", async () => {
  const port = new MockPort();
  const pending: { resolve: (v: ContextBreakdown) => void; reject: (e: unknown) => void }[] = [];
  port.context = () => new Promise<ContextBreakdown>((resolve, reject) => {
    pending.push({ resolve, reject });
  });

  let latest: ContextBreakdown | null | undefined;
  const seen: (number | null | undefined)[] = [];
  const pane = (pulse: number) => (
    <Pane port={port} rt={rt} title="w" active visible sideHost={null} side={false}
      onFocus={() => {}} onReport={(_id, report) => { latest = report.context; seen.push(report.context?.used); }}
      onSessionChanged={() => {}} pulse={pulse} findPulse={0}
      onSettings={() => {}} needsProject={false} onOpenProject={() => {}} onKeepHere={() => {}}
      theme="dark" dockW={560} dockMax={880} onDockW={() => {}} sessionRef={undefined} />
  );

  const view = render(pane(0));
  await waitFor(() => expect(pending.length).toBe(1));

  // A newer read starts; both are in flight now.
  view.rerender(pane(1));
  await waitFor(() => expect(pending.length).toBe(2));

  // The newer one answers first, so the gauge has data.
  pending[1].resolve(CONTEXT);
  await waitFor(() => expect(latest?.used).toBe(CONTEXT.used));

  // The older, superseded read now fails.
  pending[0].reject(new Error("read failed"));
  await new Promise((r) => setTimeout(r, 60));

  // The gauge may go through its initial empty renders, but once a newer read
  // has settled the figure, a superseded one may not take it away again.
  expect(seen[seen.length - 1]).toBe(CONTEXT.used);
  expect(latest?.used).toBe(CONTEXT.used);
});

// The gauge is a reading of a conversation that is still there. One failed read
// is not evidence that the conversation is empty, so it must not blank the
// figure — the next round trip refreshes it.
it("a failed read keeps the last figure instead of blanking the gauge", async () => {
  const port = new MockPort();
  let call = 0;
  port.context = () => {
    call++;
    if (call === 1) return Promise.resolve(CONTEXT);
    return Promise.reject(new Error("read failed"));
  };

  let latest: ContextBreakdown | null | undefined;
  const pane = (pulse: number) => (
    <Pane port={port} rt={rt} title="w" active visible sideHost={null} side={false}
      onFocus={() => {}} onReport={(_id, report) => { latest = report.context; }}
      onSessionChanged={() => {}} pulse={pulse} findPulse={0}
      onSettings={() => {}} needsProject={false} onOpenProject={() => {}} onKeepHere={() => {}}
      theme="dark" dockW={560} dockMax={880} onDockW={() => {}} sessionRef={undefined} />
  );

  const view = render(pane(0));
  await waitFor(() => expect(latest?.used).toBe(CONTEXT.used));

  view.rerender(pane(1));
  await waitFor(() => expect(call).toBeGreaterThanOrEqual(2));
  await new Promise((r) => setTimeout(r, 30));

  expect(latest?.used).toBe(CONTEXT.used);
});
