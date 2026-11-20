import type { Json, Row } from "../lib/types";
import { cx } from "../lib/cx";
import { fmt } from "../lib/format";

const flagged = new Set(["Awaiting Parts", "Reassigned", "Parts Ordered"]);

export function Timeline({ workOrders }: { workOrders: Row[] }) {
  return (
    <div className="grid gap-2 xl:grid-cols-2">
      {workOrders.map((wo) => {
        const actions = (Array.isArray(wo.actions) ? wo.actions : []) as Row[];
        return (
          <div key={String(wo.wo_number)} className="rounded-md border border-line">
            <div className="border-b border-line bg-surface-soft px-2.5 py-1.5">
              <div className="flex items-center gap-2">
                <span className="font-mono text-[11px] font-semibold text-ink">{fmt(wo.wo_number)}</span>
                <span className="text-[11px] text-ink-muted">{fmt(wo.priority)}</span>
                <span className="ml-auto text-[11px] font-medium tabular-nums text-ink">{fmt(wo.days_to_close)} days</span>
              </div>
              <div className="text-[12px] text-ink-soft">{fmt(wo.title)} · {fmt(wo.building)} {fmt(wo.unit)}</div>
            </div>
            <ol className="flex flex-col px-2.5 py-1.5">
              {actions.map((a, i) => (
                <li key={i} className="grid grid-cols-[88px_1fr] gap-2 py-0.5 text-[11.5px]">
                  <span className="whitespace-nowrap font-mono text-[10.5px] text-ink-faint">{fmt(a.at_local).slice(5)}</span>
                  <span className={cx(flagged.has(String(a.action_type)) ? "text-ink" : "text-ink-soft")}>
                    <span className={cx("font-medium", flagged.has(String(a.action_type)) && "text-step-micro")}>{fmt(a.action_type)}</span>
                    {a.worker ? <span className="text-ink-faint"> · {fmt(a.worker as Json)}</span> : null}
                    <span className="block text-ink-muted">{fmt(a.notes)}</span>
                  </span>
                </li>
              ))}
            </ol>
          </div>
        );
      })}
    </div>
  );
}
