import { useCallback, useLayoutEffect, useRef, useSyncExternalStore } from "react";

export type PaneView = "flow" | "analysis" | "browser";

interface Entry {
  owner: symbol;
  view: PaneView;
}

// Which view each open pane is showing. A pane's own choice, not a preference:
// it lives exactly as long as the pane instance that made it, so a pane
// remounted for another conversation starts on the conversation again.
const entries = new Map<string, Entry>();
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((fn) => fn());
}

function subscribe(fn: () => void) {
  listeners.add(fn);
  return () => void listeners.delete(fn);
}

/** The view a pane is showing; "flow" for one that is not mounted. */
export function paneView(id: string): PaneView {
  return entries.get(id)?.view ?? "flow";
}

export function usePaneViewOf(id: string): PaneView {
  return useSyncExternalStore(subscribe, () => paneView(id), () => "flow");
}

/** The pane's view and the one way to change it. The pane that mounts last
 *  owns the id: when a key change mounts the new instance before the old one's
 *  cleanup runs, the old cleanup finds it is no longer the owner and leaves the
 *  entry alone.
 *
 *  `initial` opens the pane on that view instead of the conversation. It is
 *  read once, at mount: a later render passing another value must not move a
 *  pane the person has since navigated, which is the difference between an
 *  opening position and a command. */
export function usePaneView(id: string, initial?: PaneView): [PaneView, (to: PaneView) => void] {
  const owner = useRef<symbol>(Symbol(id));
  const opening = useRef<PaneView>(initial ?? "flow");
  const view = useSyncExternalStore<PaneView>(
    subscribe,
    () => {
      const entry = entries.get(id);
      return entry && entry.owner === owner.current ? entry.view : "flow";
    },
    () => "flow",
  );
  useLayoutEffect(() => {
    const me = owner.current;
    entries.set(id, { owner: me, view: opening.current });
    emit();
    return () => {
      if (entries.get(id)?.owner !== me) return;
      entries.delete(id);
      emit();
    };
  }, [id]);
  const show = useCallback(
    (to: PaneView) => {
      const entry = entries.get(id);
      if (!entry || entry.owner !== owner.current || entry.view === to) return;
      entries.set(id, { ...entry, view: to });
      emit();
    },
    [id],
  );
  return [view, show];
}
