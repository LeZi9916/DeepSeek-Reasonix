import { useEffect, useRef, useState, type CSSProperties } from "react";
import { t } from "../i18n";
import { reason, savedNotApplied } from "../i18n/kernel";
import type { AgentPort, CompactionSettings } from "../port/port";
import { SaveNote, type SaveNoteState } from "./SaveNote";

const tokens = (n: number) => (n >= 1000 ? `${Math.round(n / 1000)}k` : String(n));

// The range the slider offers. The kernel accepts any fraction strictly between
// 0 and 1, but a share this low folds every turn and this high would defeat the
// guard, so the control stops at the ends a person would actually mean.
const RATIO_MIN = 0.1;
const RATIO_MAX = 0.99;
const RATIO_STEP = 0.01;

export function Compaction({ port }: { port: AgentPort }) {
  const [box, setBox] = useState<CompactionSettings | null>(null);
  const [used, setUsed] = useState<number | null>(null);
  useEffect(() => {
    port
      .compaction()
      .then(setBox)
      .catch(() => setBox(null));
    // The threshold is a number until it is next to the session's own usage,
    // and then it is a distance. A gauge that fails to load costs the reader
    // the distance, not the setting.
    port.context().then((c) => setUsed(c.used)).catch(() => setUsed(null));
  }, [port]);

  if (!box) return <div className="empty">{t("chat.the_compaction_settings_could_not")}</div>;

  const win = box.context_window;
  const capacity = Math.round(win * box.ratio);
  const off = win === 0;
  const pct = off || !used ? 0 : Math.min((used / box.trigger) * 100, 100);

  return (
    <>
      <div className="cmp">
        {off ? (
          <p className="note">{t("chat.this_source_declares_no_context")}</p>
        ) : (
          <>
            <div className="cmpg">
              <div className="r">
                <span className="k">{t("common.now")}</span>
                <b>{used === null ? "—" : tokens(used)}</b>
              </div>
              <div className="r">
                <span className="k">{t("chat.next_maintenance")}</span>
                <b>{tokens(box.trigger)}</b>
              </div>
              <div className="r">
                <span className="k">{t("chat.model_window")}</span>
                <b>{tokens(win)}</b>
              </div>
            </div>
            {used !== null && (
              <div className="cmpbar" role="presentation">
                <i style={{ width: `${pct}%` }} />
              </div>
            )}
            <p className="note">{t("chat.this_session_should_tidy_its", { n: tokens(box.trigger) })}</p>
          </>
        )}
      </div>

      <div className="lrow">
            <span className="tx">
              <span className="lb">{t("chat.capacity_guard")}</span>
              <span className="ds">
                {t("chat.compacts_at_p_of_the", { p: String(Math.round(box.ratio * 100)) })}
              </span>
            </span>
            <span className="sc">{off ? "—" : tokens(capacity)}</span>
      </div>

      <RatioSetting port={port} box={box} onSaved={setBox} />
    </>
  );
}

/** The fold threshold as a share of the window. It moves continuously under
 *  the finger and the kernel hears about it once, on release: dragging from
 *  0.9 to 0.5 asks for one rebuild, not forty. */
function RatioSetting({ port, box, onSaved }: {
  port: AgentPort;
  box: CompactionSettings;
  onSaved: (next: CompactionSettings) => void;
}) {
  const shown = Math.min(RATIO_MAX, Math.max(RATIO_MIN, box.ratio));
  const [draft, setDraft] = useState(shown);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<SaveNoteState | null>(null);
  // Pointer and key handlers settle outside the render that set the draft, so
  // the value they commit is read here rather than from the render they close
  // over.
  const latest = useRef(shown);
  // One release can raise both pointerup and blur before either re-renders, so
  // the guard has to be a ref: `busy` is still false on the second call.
  const inFlight = useRef(false);
  // Only a gesture that moved something writes. A stored share outside the
  // slider's ends is drawn at the nearest end, and a click on it must not
  // silently rewrite the value it never reached.
  const moved = useRef(false);

  useEffect(() => {
    latest.current = shown;
    setDraft(shown);
  }, [shown]);

  const move = (next: number) => {
    moved.current = true;
    latest.current = next;
    setDraft(next);
  };

  const commit = async () => {
    if (inFlight.current || !moved.current) return;
    moved.current = false;
    const next = Math.round(latest.current / RATIO_STEP) * RATIO_STEP;
    // Dragging back to the share already in force is not a change, and a write
    // here would rebuild the runtime to reach the threshold it already had.
    if (Math.abs(next - box.ratio) < RATIO_STEP / 2) return;
    inFlight.current = true;
    setBusy(true);
    setNote(null);
    try {
      onSaved(await port.setCompactRatio(Number(next.toFixed(2))));
    } catch (e) {
      // A share the kernel refused was never written, so the thumb goes back to
      // the stored value. A save that landed but waits for the turn to end keeps
      // showing what the person chose, with the note saying why it is not live.
      if (!savedNotApplied(e)) {
        latest.current = shown;
        setDraft(shown);
      }
      setNote({ text: reason(e), unapplied: savedNotApplied(e) });
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  };

  const at = ((draft - RATIO_MIN) / (RATIO_MAX - RATIO_MIN)) * 100;

  return (
    <div className="lrow cmp-ratio-row">
      <span className="tx">
        <span className="lb">{t("chat.compact_ratio_control")}</span>
        <span className="ds">{t("chat.compact_ratio_control_hint")}</span>
        <SaveNote note={note} />
      </span>
      <div className="cmp-ratio">
        <input
          className="slider"
          style={{ "--at": `${at}%` } as CSSProperties}
          type="range"
          min={RATIO_MIN}
          max={RATIO_MAX}
          step={RATIO_STEP}
          value={draft}
          disabled={busy}
          aria-label={t("chat.compact_ratio_control")}
          aria-valuetext={`${Math.round(draft * 100)}%`}
          data-action-change="compaction.ratio-preview"
          data-action-pointerup="compaction.ratio"
          data-action-pointercancel="compaction.ratio"
          data-action-keyup="compaction.ratio"
          data-action-blur="compaction.ratio"
          onChange={(e) => move(Number(e.target.value))}
          onPointerUp={() => void commit()}
          onPointerCancel={() => void commit()}
          onKeyUp={() => void commit()}
          onBlur={() => void commit()}
        />
        <span className="v">{Math.round(draft * 100)}%</span>
      </div>
    </div>
  );
}
