---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Conversation review

Electron is on the left and Avalonia on the right. Sixteen pairs cover 1440 × 900 and 760 × 480 in both palettes, with settled messages, expanded reasoning and work, and a focused draft. Four closeups expose the composer at its original scale.

The sixteen workbench pairs include the updated composer beside files and Git.

`AnswerCard.axaml`, `UserMessage.axaml`, `ActivityDisclosure.axaml` and `ToolCallHeader.axaml` compose the native message surfaces. They use the reference Inter/Noto/IBM Plex faces, semantic brushes, selectable Markdown and vector actions.

Streaming retains selectable blocks while their parsed shape stays stable.

Settled work starts folded; running work opens automatically until the reader makes a choice. Search reveals reasoning and work. Copy retains original Markdown and quotation uses the selected passage. Tool disclosures preserve arguments, outputs, errors and capped-output paging.

The composer now uses the same reading face, 22.4-DIP text line, neutral focus outline, pill geometry, toolbar spacing, vendor mark and tabular status figures as Electron. An empty draft disables sending. Running turns expose stop and steer/queue labels.

Drafts remain scoped to the session and remote machine.

Git counts refresh without opening the workbench; the changed-files pill opens its actual changes board. Work strategy, execution permissions, branch, model and effort controls use kernel acknowledgements and readback. Mode refusals preserve the draft and previous reading.

Prompt refinement offers an independent proposal with discard, retry, explicit adoption and keyboard shortcuts. Side questions use a separate read-only kernel stream, support closing and quoting, and never submit or clear the main draft. Shared scrolling bounds these surfaces in compact windows.

Context and wallet readings come from the same captured HTTP responses as Electron, including wallet freshness and amount masking. Context opens its existing detail pane and cache opens an overview.

The detailed speed history, provider gallery and full worktree-configuration picker remain outstanding.

All 285 baseline tests and 135 render tests pass. The baseline geometry fixture now replays the same task, Git and telemetry responses as the screenshots. Eight newly mapped composer controls were removed from recorded missing debt; the debt budget was not widened.

Element-level fixtures also compare the file-count texts, status readings, ring and model labels against Electron, including individual bounds, line height, weight, color and rasterized glyph rows in both palettes and viewports.

`StudioLineText` draws single-line labels around the primary font baseline with CSS half-leading. Pixel checks allow one row of rasterization difference, so matching an outer control cannot hide shifted glyphs.

Status figures use the reference reading weight and a five-character speed slot. Short line boxes allow glyph overflow; the model name retains horizontal truncation. The context ring preserves its 11-pixel size, opacity and live warning colors.

The mapped composer controls have no box differences in the desktop light/dark captures. Full-window reports still retain conversation/chrome geometry, style and unmatched DOM differences; this is not a claim of complete frontend parity.

Render captures use Avalonia Headless with Skia on Windows. Live remote SSH authentication and macOS/Linux platform interaction were not exercised locally.

Go formatting and vet pass. Go lint reports six existing findings; the changed-file debt gate reports the existing global CSS, essay, release-note and wire-parity totals.

Builtin tests fail on the existing Git metadata denial attribution fixture. Boot tests fail and time out in WSL where the required shell sandbox is unavailable.
