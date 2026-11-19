import { describe, expect, it } from "vitest";
import { newArgTokens, streamed } from "./arrivals";
import { initialState, reduce } from "./session";
import type { SessionEvent } from "./session";
import type { SessionState } from "./session_types";
import { tokensPerSecond } from "../port/tokens";

const run = (s: SessionState, ...evs: SessionEvent[]) => evs.reduce(reduce, s);
const dispatch = (extra: Record<string, unknown>): SessionEvent =>
  ({ kind: "tool_dispatch", tool: { id: "c1", name: "edit_file", readOnly: false, ...extra } } as SessionEvent);

describe("a tool call's arguments are output", () => {
  // The defect: a turn that answers with a large edit and no prose streamed for
  // seconds while the rate read zero, because only text and reasoning sampled.
  // The kernel re-emits the cumulative count as the payload grows, so a live
  // turn produces several of these.
  it("samples the arguments as they stream", () => {
    let s = run(initialState, { kind: "turn_started" } as SessionEvent);
    for (const argChars of [400, 1200, 2400]) s = run(s, dispatch({ partial: true, argChars }));
    expect(s.outLive).toBeGreaterThan(0);
    expect(s.outWindow.length).toBeGreaterThan(1);
    expect(tokensPerSecond(s.outWindow, s.outWindow[s.outWindow.length - 1].t + 1000)).toBeGreaterThan(0);
  });

  it("counts the difference between cumulative frames, not the total again", () => {
    const items = [{ t: "tool", id: "i1", running: true, children: [], tool: { id: "c1", name: "edit_file", partial: true, argChars: 400 } }] as never;
    expect(newArgTokens(items, { id: "c1", name: "edit_file", readOnly: false, partial: true, argChars: 400 })).toBe(0);
    expect(newArgTokens(items, { id: "c1", name: "edit_file", readOnly: false, partial: true, argChars: 800 })).toBe(100);
  });

  it("says nothing when the call is complete or carried no arguments", () => {
    const items = [] as never;
    expect(newArgTokens(items, { id: "c1", name: "edit_file", readOnly: false })).toBe(0);
    expect(newArgTokens(items, { id: "c1", name: "edit_file", readOnly: false, partial: true })).toBe(0);
  });

  it("makes no sample from a frame that brought nothing", () => {
    expect(streamed(initialState, 0)).toEqual({});
  });

  it("finds a nested call's count where its card lives", () => {
    const items = [{ t: "tool", id: "p", running: true, children: [{ id: "k", name: "bash", partial: true, argChars: 200 }], tool: { id: "p", name: "task" } }] as never;
    expect(newArgTokens(items, { id: "k", name: "bash", readOnly: false, partial: true, argChars: 320 })).toBe(30);
  });
});
