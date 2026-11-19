import { StudioIcon } from "./StudioIcon";
import { useState } from "react";
import { t } from "../i18n";
import type { ContextBreakdown, McpEntry } from "../port/port";
import { CompactNow, type CompactAction } from "./CompactNow";

const compact = (n: number): string => n >= 1_000_000 ? `${(n / 1_000_000).toFixed(1)}M` : n >= 1_000 ? `${Math.round(n / 100) / 10}K` : String(n);

export function ContextSummaryCard({ context, mcp, percent, onManage, compact: action, className = "" }: {
  context: ContextBreakdown | null;
  mcp: McpEntry[];
  percent: number | null;
  onManage: () => void;
  compact?: CompactAction;
  className?: string;
}) {
  // One class open at a time: two lists in a card this narrow push the rest off the screen.
  const [listed, setListed] = useState<string | null>(null);
  const active = mcp.filter((entry) => entry.enabled);
  const ready = active.filter((entry) => entry.state === "ready");
  const tools = ready.reduce((sum, entry) => sum + (entry.tools || 0), 0);
  const parts = context ? [
    [t("common.system_prompt"), context.system, "sys"],
    [t("common.tool_definitions"), context.tools, "tool"],
    [t("panels.project_memory"), context.project_memory || 0, "memory"],
    [t("common.what_you_said"), context.user, "user"],
    [t("common.model_replies"), context.reply, "reply"],
    [t("common.tool_output"), context.output, "output"],
  ] as const : [];
  const memoryFiles = context?.project_memory_files ?? [];
  const holdersOf = (key: string) => (context?.largest ?? []).filter((h) => h.class === key);
  // Project memory is not a message class: what it opens to is the documents the
  // block was built from, which the kernel names directly.
  const opens = (key: string) => key === "memory" ? memoryFiles.length > 0 : holdersOf(key).length > 0;
  const opened = parts.find(([, , key]) => key === listed);
  const openLargest = opened && opened[2] !== "memory";

  return (
    <div className={["chrome-context-card", className].filter(Boolean).join(" ")} role="dialog" aria-label={t("common.context_details")}>
      <header><span><b>{t("common.context_details")}</b><small>{t("composer.content_sent_to_the_model")}</small></span><strong>{percent ?? 0}%</strong></header>
      {context ? (
        <>
          <div className="chrome-context-total"><span>{t("common.used")} <b>{compact(context.used)}</b></span><span>{t("common.model_capacity")} <b>{compact(context.window)}</b>{context.compact_at > 0 && <> · {t("composer.auto_compacts_at", { n: compact(context.compact_at) })}</>}</span></div>
          <div className="chrome-context-bar">{parts.map(([label, value, key]) => <i key={key} data-part={key} title={`${label} ${compact(value)}`} style={{ width: `${context.used ? Math.max(1, (value / context.used) * 100) : 0}%` }} />)}</div>
          <div className="chrome-context-parts">{parts.map(([label, value, key]) => (
            <div key={key}>
              <i data-part={key} /><span>{label}</span>
              {key === "memory"
                ? (opens(key) && <button type="button" className="chrome-context-more" data-action="context.memory-files" data-value={key} aria-expanded={listed === key} aria-label={t("panels.show_the_loaded_memory_files")} onClick={() => setListed(listed === key ? null : key)}>{compact(value)}<StudioIcon name="chevron" /></button>)
                : opens(key)
                  ? <button type="button" className="chrome-context-more" data-action="context.largest" data-value={key} aria-expanded={listed === key} aria-label={t("composer.show_the_largest_items_in", { name: label })} onClick={() => setListed(listed === key ? null : key)}>{compact(value)}<StudioIcon name="chevron" /></button>
                  : <b>{compact(value)}</b>}
            </div>
          ))}</div>
          {!opened && parts.some(([, , key]) => opens(key)) && <p className="chrome-context-hint">{t("composer.open_a_class_for_its_largest")}</p>}
          {opened && !openLargest && (
            <ul className="chrome-context-items" aria-labelledby="chrome-context-items-title">
              <li id="chrome-context-items-title" className="head">{t("panels.project_memory_files")}</li>
              {memoryFiles.map((f) => (
                <li key={f.path} title={f.path}>
                  <span>{f.path}</span>
                  <em>{f.scope}</em>
                  <b>{compact(f.tokens)}</b>
                </li>
              ))}
            </ul>
          )}
          {opened && openLargest && (
            <ul className="chrome-context-items" aria-labelledby="chrome-context-items-title">
              <li id="chrome-context-items-title" className="head">{t("composer.largest_items_in_name", { name: opened[0] })}</li>
              {holdersOf(opened[2]).map((h, i) => (
                <li key={i}>
                  <span data-tool={h.tool ? "" : undefined}>{h.tool || opened[0]}</span>
                  {h.active && <em>{t("composer.running_turn")}</em>}
                  <b>{compact(h.tokens)}</b>
                </li>
              ))}
            </ul>
          )}
        </>
      ) : <p>{t("composer.no_context_data_yet")}</p>}
      <footer className="chrome-context-foot">
        <span title={active.map((server) => `${server.name} · ${server.state === "ready" ? `${server.tools || 0} ${t("composer.tools")}` : t("composer.waiting_to_connect")}`).join("\n") || undefined}>
          MCP · {ready.length} {t("composer.connected")} · {tools} {t("composer.tools")}
        </span>
        {action && <CompactNow action={action} usage={context ? { used: context.used, window: context.window } : undefined} />}
        <button type="button" className="manage" data-action="settings.section" data-value="ext" onClick={onManage}>{t("composer.manage_mcp")}</button>
      </footer>
    </div>
  );
}
