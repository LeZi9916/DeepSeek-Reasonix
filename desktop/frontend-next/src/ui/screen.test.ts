import { describe, expect, it } from "vitest";
import { opensWorkbench, parseScreen, seedFileOf, settingsOf, viewOf } from "./screen";

describe("parseScreen", () => {
  it("opens the conversation when the address names no page", () => {
    expect(parseScreen(null)).toEqual({ kind: "conversation" });
    expect(parseScreen("")).toEqual({ kind: "conversation" });
    expect(parseScreen("   ")).toEqual({ kind: "conversation" });
  });

  it("reads each page a capture may ask for", () => {
    expect(parseScreen("conversation")).toEqual({ kind: "conversation" });
    expect(parseScreen("analysis")).toEqual({ kind: "analysis" });
    expect(parseScreen("workbench")).toEqual({ kind: "workbench" });
    expect(parseScreen("onboarding")).toEqual({ kind: "onboarding" });
    expect(parseScreen("settings:model")).toEqual({ kind: "settings", section: "model" });
  });

  it("keeps an anchor, so a capture can name one block rather than a section top", () => {
    expect(parseScreen("settings:model:context")).toEqual({ kind: "settings", section: "model:context" });
    // persona is folded into session, and that is where its block is drawn.
    expect(parseScreen("settings:session:persona")).toEqual({ kind: "settings", section: "session:persona" });
  });

  it("refuses arguments a page does not take", () => {
    expect(() => parseScreen("analysis:now")).toThrow(/analysis takes no argument/);
    expect(() => parseScreen("workbench:README.md")).toThrow(/workbench takes no argument/);
    expect(() => parseScreen("settings:model:context:extra")).toThrow(/at most section:anchor/);
  });

  it("refuses a page nobody declared, instead of falling back to the conversation", () => {
    expect(() => parseScreen("market")).toThrow(/unknown screen "market"/);
    expect(() => parseScreen("settings")).toThrow(/settings needs a section/);
    expect(() => parseScreen("settings:nope")).toThrow(/no settings section "nope"/);
  });

  it("names the home page for a section that is folded into another", () => {
    expect(() => parseScreen("settings:providers")).toThrow(/drawn inside model; ask for model:providers/);
    expect(() => parseScreen("settings:memory")).toThrow(/drawn inside storage/);
  });

  it("refuses an anchor that does not exist, or that lives on another page", () => {
    expect(() => parseScreen("settings:model:nope")).toThrow(/no setting named "nope"/);
    expect(() => parseScreen("settings:session:context")).toThrow(/lives on model, not session/);
  });
});

describe("what a named page opens", () => {
  it("maps the pages onto the window's own entry points", () => {
    expect(settingsOf({ kind: "settings", section: "model" })).toBe("model");
    expect(settingsOf({ kind: "conversation" })).toBe(false);
    expect(viewOf({ kind: "analysis" })).toBe("analysis");
    expect(viewOf({ kind: "workbench" })).toBeNull();
    expect(opensWorkbench({ kind: "workbench" })).toBe(true);
    expect(opensWorkbench({ kind: "conversation" })).toBe(false);
  });

  it("opens the conversation as it is, with no seeded file", () => {
    expect(seedFileOf({ kind: "conversation" })).toBeUndefined();
    expect(seedFileOf({ kind: "analysis" })).toBeUndefined();
    expect(seedFileOf({ kind: "workbench" })).toBe("README.md");
  });
});
