import { FOLDS_INTO, NAV, SETTINGS, pageHome } from "./prefsnav";

/** The sections the sheet's own table of contents lists, which are the ones a
 *  capture may name. A folded section (providers, memory, persona…) is drawn
 *  inside its home page, so naming it alone would capture the home page's top
 *  under another name; it is reachable as `<home>:<anchor>` instead. */
const TOP_LEVEL = new Set<string>(NAV.flatMap(([, items]) => items.map(([section]) => section)));

/** One page a baseline capture can open, named rather than reached by clicking.
 *
 *  A capture that has to click its way in measures whatever the click happened
 *  to leave behind: a sheet mid-entrance, a list still paging. Naming the page
 *  makes the first paint already be the state under measurement, and makes a
 *  page that no longer opens a parser error rather than a smaller capture. */
export type Screen =
  | { kind: "conversation" }
  | { kind: "analysis" }
  | { kind: "workbench" }
  | { kind: "onboarding" }
  | { kind: "settings"; section: string };

/** The screens the parser accepts, in the order a failure message lists them. */
export const SCREEN_KINDS = ["conversation", "analysis", "workbench", "onboarding", "settings"] as const;

export class UnknownScreen extends Error {
  constructor(value: string, why: string) {
    super(`?screen=${value}: ${why}`);
    this.name = "UnknownScreen";
  }
}

/** Parses `?screen=` into the page it names, or throws saying why.
 *
 *  Absent is the conversation, which is what the window opens on anyway. A
 *  value this cannot read throws: falling back to the conversation would record
 *  a domain under another page's name, and the gate would then hold the wrong
 *  screen to the wrong baseline forever. */
export function parseScreen(raw: string | null): Screen {
  if (raw === null || raw.trim() === "") return { kind: "conversation" };
  const [kind, ...rest] = raw.trim().split(":");
  switch (kind) {
    case "conversation":
      if (rest.length > 0) throw new UnknownScreen(raw, "conversation takes no argument");
      return { kind: "conversation" };
    case "analysis":
      if (rest.length > 0) throw new UnknownScreen(raw, "analysis takes no argument");
      return { kind: "analysis" };
    case "workbench":
      if (rest.length > 0) throw new UnknownScreen(raw, "workbench takes no argument");
      return { kind: "workbench" };
    case "onboarding":
      if (rest.length > 0) throw new UnknownScreen(raw, "onboarding takes no argument");
      return { kind: "onboarding" };
    case "settings": {
      const [named, anchor, ...more] = rest.join(":").split(":");
      if (!named) throw new UnknownScreen(raw, "settings needs a section, as settings:model");
      if (more.length > 0) throw new UnknownScreen(raw, `settings takes at most section:anchor, not "${rest.join(":")}"`);

      if (anchor) {
        const entry = SETTINGS.find((s) => s.anchor === anchor);
        if (!entry) {
          throw new UnknownScreen(raw, `no setting named "${anchor}"`);
        }
        const home = pageHome(entry.section);
        if (home !== named) {
          throw new UnknownScreen(raw, `"${anchor}" lives on ${home}, not ${named}`);
        }
        return { kind: "settings", section: `${named}:${anchor}` };
      }

      if (!TOP_LEVEL.has(named)) {
        const home = FOLDS_INTO[named as keyof typeof FOLDS_INTO];
        throw new UnknownScreen(raw, home
          ? `"${named}" is drawn inside ${home}; ask for ${home}:${named}`
          : `no settings section "${named}"; expected one of ${[...TOP_LEVEL].join(", ")}`);
      }
      return { kind: "settings", section: named };
    }
    default:
      throw new UnknownScreen(raw, `unknown screen "${kind}"; expected one of ${SCREEN_KINDS.join(", ")}`);
  }
}

/** The settings value `App` should open on for this screen, or false.
 *
 *  This is the same string the settings sheet already accepts from the
 *  window's own entry points, so a named page and a clicked one are one path. */
export function settingsOf(screen: Screen): string | false {
  return screen.kind === "settings" ? screen.section : false;
}

/** The pane view a named screen opens on, or null for the conversation.
 *
 *  The workbench is not one of these: it is a dock beside the conversation,
 *  opened by its own flag, and asking for it as a view would put an empty
 *  column where a conversation belongs. */
export function viewOf(screen: Screen): "analysis" | null {
  return screen.kind === "analysis" ? "analysis" : null;
}

/** Whether the named screen opens the workbench dock beside the conversation. */
export function opensWorkbench(screen: Screen): boolean {
  return screen.kind === "workbench";
}

/** The file a named screen opens, or undefined when it opens none.
 *
 *  A workbench with nothing open draws its launcher, which is the same page
 *  whatever the workspace holds; opening one file is what makes a capture show
 *  the editor, the tab strip and the tree a reader actually works in. */
export function seedFileOf(screen: Screen): string | undefined {
  return screen.kind === "workbench" ? "README.md" : undefined;
}
