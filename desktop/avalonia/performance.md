---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-26
---

# Avalonia performance measurements

Set `REASONIX_STUDIO_PERF_LOG` to a writable file path before launching the
Release shell. Close the window normally to flush the log. Start each run with
a fresh file.

Each tab-separated row contains a stage name, an operation ID, a monotonic
timestamp, and elapsed milliseconds. Operation ID zero denotes a standalone
stage. Stages with overlapping timestamps run concurrently.

Run each case at least 30 times on the same machine and display:

- Start a new process with and without first-run setup.
- Open a workspace and confirm the composer can receive input.
- Open a long history, switch back to its retained tab, and load an earlier page.

Measure animation tick P95 and the fraction of intervals over 33 ms on a 60 Hz
display. `startup.first-draw` marks Avalonia's first draw.
`navigation.viewport-tick` marks the tick after the conversation commit.
Use a platform compositor trace to measure actual presentation.

Compare `host.handshake`, HTTP response timing, `history.project`,
`session.ui-commit`, and `history.anchor-restored` against the elapsed
operation. The streamed history body and JSON decode share one stage.
