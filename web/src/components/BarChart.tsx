import { useState } from "react";
import type { Row } from "../lib/types";
import { cx } from "../lib/cx";
import { fmt, label } from "../lib/format";

/** Horizontal bars for one grouped dimension. The largest value gets the accent; the rest stay neutral. */
export function BarChart({ rows, measure, groupKey, measures }: { rows: Row[]; measure: string; groupKey: string; measures: string[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const values = rows.map((r) => (typeof r[measure] === "number" ? (r[measure] as number) : 0));
  const max = Math.max(...values, 0) || 1;
  const top = values.indexOf(Math.max(...values));
  return (
    <div className="flex flex-col gap-1 px-1 py-1">
      {rows.slice(0, 14).map((r, i) => {
        const v = values[i] ?? 0;
        return (
          <div
            key={`${String(r[groupKey])}-${i}`}
            className="relative grid grid-cols-[150px_1fr_56px] items-center gap-2 text-[12px]"
            onMouseEnter={() => setHover(i)}
            onMouseLeave={() => setHover(null)}
          >
            <span className={cx("truncate", i === top ? "font-semibold text-ink" : "text-ink-soft")} title={String(r[groupKey])}>{fmt(r[groupKey])}</span>
            <div className="h-3.5">
              <div
                className="h-full rounded-r-[4px] transition"
                style={{ width: `${Math.max(1.5, (v / max) * 100)}%`, background: i === top ? "#2a78d6" : hover === i ? "#a3a3a3" : "#d4d4d4" }}
              />
            </div>
            <span className={cx("text-right tabular-nums", i === top ? "font-semibold text-ink" : "text-ink-soft")}>{fmt(r[measure], measure)}</span>
            {hover === i && (
              <div className="pointer-events-none absolute left-[160px] top-5 z-10 rounded-md border border-line bg-surface px-2 py-1 text-[11px]">
                <div className="font-medium text-ink">{fmt(r[groupKey])}</div>
                {measures.map((m) => (
                  <div key={m} className="text-ink-soft">{label(m)}: <span className="text-ink">{fmt(r[m], m)}</span></div>
                ))}
              </div>
            )}
          </div>
        );
      })}
      {rows.length > 14 && <span className="text-[11px] text-ink-faint">+{rows.length - 14} more in table</span>}
    </div>
  );
}
