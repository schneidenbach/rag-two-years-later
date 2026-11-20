import type { Row } from "../lib/types";
import { cx } from "../lib/cx";
import { fmt } from "../lib/format";

export function WorkOrderList({ rows }: { rows: Row[] }) {
  if (rows.length === 0) return <p className="px-1 py-2 text-[12px] text-ink-muted">No matching work orders.</p>;
  return (
    <div className="overflow-hidden rounded-md border border-line">
      <table className="w-full border-collapse text-[11.5px]">
        <thead className="bg-surface-soft text-ink-muted">
          <tr>
            {["Work order", "Issue", "Location", "Days open", "Waiting on parts", "Reassigned", "Vendor"].map((h) => (
              <th key={h} className="whitespace-nowrap border-b border-line px-2 py-1 text-left font-medium">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.slice(0, 10).map((r) => {
            const parts = typeof r.days_waiting_on_parts === "number" ? r.days_waiting_on_parts : 0;
            return (
              <tr key={String(r.wo_number)} className="border-b border-line last:border-b-0">
                <td className="whitespace-nowrap px-2 py-1 font-mono text-[11px] text-ink">{fmt(r.wo_number)}</td>
                <td className="max-w-[220px] truncate px-2 py-1 text-ink-soft" title={String(r.title)}>{fmt(r.title)}</td>
                <td className="whitespace-nowrap px-2 py-1 text-ink-soft">{fmt(r.building)}</td>
                <td className="px-2 py-1 text-right tabular-nums font-medium text-ink">{fmt(r.days_to_close)}</td>
                <td className={cx("px-2 py-1 text-right tabular-nums", parts >= 7 ? "font-semibold text-step-micro" : "text-ink-soft")}>{fmt(r.days_waiting_on_parts)}</td>
                <td className="px-2 py-1 text-right tabular-nums text-ink-soft">{fmt(r.reassignment_count)}</td>
                <td className="whitespace-nowrap px-2 py-1 text-ink-soft">{fmt(r.parts_vendor)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {rows.length > 10 && <div className="border-t border-line px-2 py-1 text-[11px] text-ink-faint">+{rows.length - 10} more</div>}
    </div>
  );
}
