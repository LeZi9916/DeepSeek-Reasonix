---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Conversation review

Electron is on the left and Avalonia on the right. Twelve pairs cover 1440 × 900 and 760 × 480 in both palettes, with settled messages, expanded reasoning and work, and a focused draft. The sixteen workbench pairs also include the updated conversation beside files and Git.

`AnswerCard.axaml`, `UserMessage.axaml`, `ActivityDisclosure.axaml` and `ToolCallHeader.axaml` compose the native message surfaces.

They use the reference Inter/Noto/IBM Plex faces and semantic brushes, a bounded reading column inside the full scroll viewport, selectable Markdown blocks, turn rules and vector actions. Streaming updates retain selectable blocks while their parsed shape stays stable.

Settled work starts folded; running work opens automatically until the reader makes a choice. Search reveals reasoning and work. Copy retains the original Markdown and quotation uses the selected passage.

Tool disclosures expose canonical arguments, output and errors; capped output retains its existing paged read path.

The composer uses an independent outline, the reference geometry, live Git counts and a changes-board launcher, a separate context footer and disabled empty-send state. Appearance preferences still override the reading and code fonts.

Secondary Electron composer launchers and its detailed telemetry rail remain outside this change; screenshots retain those visible gaps.

The full solution builds with zero warnings and zero errors. All 280 baseline tests and 118 render tests pass.

The suites exercise streaming, selection, quotation, Markdown links, work and reasoning folds, search, output caps, composer layout, typography preferences and adjacent Git/sidebar flows.

Render captures use Avalonia Headless with Skia on Windows; macOS/Linux screen-reader and platform interaction checks were not run locally.

The geometry exporter now reads Border fill, padding and radius. The coverage fixture opens native work disclosures because the DOM collector also records descendants of closed details; the normal screenshot fixture preserves the settled collapsed state.

The debt gate reports zero new findings. Full-window screenshot reports still record ten box differences in the conversation views, with unmatched DOM identities and style differences listed separately. They are not a claim of complete frontend parity.

Go formatting and vet pass. `make lint` reports six existing Go findings; `make check` reports the existing CSS, essay, release-note and wire-parity totals. Builtin tests pass. Boot tests fail and time out in WSL where the required shell sandbox is unavailable.
