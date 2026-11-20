import type { Row } from "../lib/types";
import { fmt, label } from "../lib/format";

export function DataTable({ rows, columns }: { rows: Row[]; columns?: string[] }) {
  const cols = columns ?? Object.keys(rows[0] ?? {});
  if (rows.length === 0) return <p className="px-1 py-2 text-[12px] text-ink-muted">No rows.</p>;
  return (
    <div className="max-h-[280px] overflow-auto rounded-md border border-line scrollbar-thin">
      <table className="w-full border-collapse text-[11.5px]">
        <thead className="sticky top-0 bg-surface-soft">
          <tr>
            {cols.map((c) => (
              <th key={c} className="whitespace-nowrap border-b border-line px-2 py-1 text-left font-medium text-ink-muted">{label(c)}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={i} className="border-b border-line last:border-b-0 hover:bg-surface-hover">
              {cols.map((c) => (
                <td key={c} className={typeof r[c] === "number" ? "whitespace-nowrap px-2 py-1 text-right tabular-nums text-ink-soft" : "whitespace-nowrap px-2 py-1 text-ink-soft"}>
                  {fmt(r[c], c)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
