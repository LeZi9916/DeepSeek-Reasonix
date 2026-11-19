import type { Item, SessionState } from "./session_types";
import type { Tool } from "../port/wire";
import { estimateChars, sample } from "../port/tokens";

// Throughput is an observation of what arrived, and a call's arguments are a
// model output like any other prose: a turn that answers with one large edit and
// no text still produced tokens the whole time it streamed. Keeping both kinds
// of arrival here is what lets the rate see the whole turn rather than only the
// part of it that happened to be written as a sentence.

/** One arrival of `n` tokens. A frame that brought none makes no sample: a zero
 *  would stretch the span the rate is measured over with nothing behind it. */
export const streamed = (s: SessionState, n: number) =>
  n > 0 ? { outWindow: sample(s.outWindow, n, Date.now()), outLive: s.outLive + n } : {};

/** How many new argument tokens a streaming dispatch brought. The card carries
 *  the cumulative count, so this is a difference rather than an increment: a
 *  replayed or out-of-order frame repeating an older count adds nothing. */
export function newArgTokens(items: Item[], tool: Tool): number {
  if (!tool.partial || !tool.argChars) return 0;
  let seen = 0;
  for (const i of items) {
    if (i.t !== "tool") continue;
    if (i.tool.id === tool.id) seen = i.tool.argChars ?? 0;
    else for (const kid of i.children) if (kid.id === tool.id) seen = kid.argChars ?? 0;
  }
  return estimateChars(Math.max(0, tool.argChars - seen));
}
