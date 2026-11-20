import { useState } from "react";
import type { Json, Row } from "../lib/types";
import { cx } from "../lib/cx";
import { fmt, label } from "../lib/format";
import { BarChart } from "./BarChart";
import { LineChart } from "./LineChart";
import { DataTable } from "./DataTable";

type Agg = { rows: Row[]; groupBy: string[]; measures: string[]; timeSeries: boolean; population: number; sql?: string; note?: string };

const chip = "rounded-full px-2 py-0.5 text-[11px] font-medium transition";

export function parseAggregate(result: Json | undefined): Agg | null {
  if (typeof result !== "object" || result === null || Array.isArray(result) || result.kind !== "aggregate") return null;
  const strs = (v: Json | undefined) => (Array.isArray(v) ? v.filter((x): x is string => typeof x === "string") : []);
  return {
    rows: (Array.isArray(result.rows) ? result.rows : []) as Row[],
    groupBy: strs(result.groupBy),
    measures: strs(result.measures),
    timeSeries: result.timeSeries === true,
    population: typeof result.population === "number" ? result.population : 0,
    sql: typeof result.sql === "string" ? result.sql : undefined,
    note: typeof result.note === "string" ? result.note : undefined,
  };
}

export function AggregateView({ agg }: { agg: Agg }) {
  const primary = agg.measures.find((m) => m !== "count" && m !== "open_count") ?? agg.measures[0] ?? "count";
  const [measure, setMeasure] = useState(primary);
  const [view, setView] = useState<"chart" | "table" | "sql">("chart");
  const dims = agg.groupBy.filter((g) => g !== "period");

  let chart: JSX.Element;
  if (agg.rows.length === 0) chart = <p className="px-1 py-2 text-[12px] text-ink-muted">{agg.note ?? "No rows."}</p>;
  else if (agg.timeSeries && dims.length <= 1) chart = <LineChart rows={agg.rows} measure={measure} seriesKey={dims[0] ?? null} />;
  else if (!agg.timeSeries && dims.length === 1 && dims[0]) chart = <BarChart rows={agg.rows} measure={measure} groupKey={dims[0]} measures={agg.measures} />;
  else if (dims.length === 0 && agg.rows.length === 1 && agg.rows[0]) chart = <Stats row={agg.rows[0]} measures={agg.measures} />;
  else chart = <DataTable rows={agg.rows} />;

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex flex-wrap items-center gap-1">
        {agg.measures.length > 1 && view === "chart" && agg.rows.length > 1 && agg.measures.map((m) => (
          <button key={m} type="button" onClick={() => setMeasure(m)} className={cx(chip, m === measure ? "bg-ink text-white" : "text-ink-muted hover:bg-surface-hover hover:text-ink")}>
            {label(m)}
          </button>
        ))}
        <span className="ml-auto text-[11px] text-ink-faint">{agg.population.toLocaleString()} work orders</span>
        {(["chart", "table", "sql"] as const).map((v) => (
          <button key={v} type="button" onClick={() => setView(v)} className={cx(chip, view === v ? "bg-surface-deep text-ink" : "text-ink-faint hover:text-ink")}>
            {v === "sql" ? "SQL" : v[0]?.toUpperCase() + v.slice(1)}
          </button>
        ))}
      </div>
      {view === "chart" && chart}
      {view === "table" && <DataTable rows={agg.rows} />}
      {view === "sql" && <pre className="overflow-x-auto rounded-md bg-surface-deep px-2.5 py-2 font-mono text-[11px] leading-relaxed text-ink-soft scrollbar-thin">{agg.sql}</pre>}
    </div>
  );
}

function Stats({ row, measures }: { row: Row; measures: string[] }) {
  return (
    <div className="flex flex-wrap gap-2">
      {measures.map((m) => (
        <div key={m} className="min-w-[120px] rounded-lg border border-line px-2.5 py-1.5">
          <div className="tracked-label">{label(m)}</div>
          <div className="text-[15px] font-semibold tracking-tight tabular-nums text-ink">{fmt(row[m], m)}</div>
        </div>
      ))}
    </div>
  );
}
