import { useMemo, useState } from "react";
import type { Row } from "../lib/types";
import { fmt, label, shortMonth } from "../lib/format";
import { seriesColor } from "../lib/colors";

const W = 640;
const H = 210;
const M = { top: 12, right: 92, bottom: 24, left: 40 };
const MAX_SERIES = 5;

/** Time series: x = period, one line per value of `seriesKey` (or a single line). */
export function LineChart({ rows, measure, seriesKey }: { rows: Row[]; measure: string; seriesKey: string | null }) {
  const [hover, setHover] = useState<number | null>(null);

  const { periods, series, yMax, hidden } = useMemo(() => {
    const periods = [...new Set(rows.map((r) => String(r.period)))].sort();
    const groups = new Map<string, Map<string, number>>();
    const weight = new Map<string, number>();
    for (const r of rows) {
      const name = seriesKey ? String(r[seriesKey]) : label(measure);
      const v = r[measure];
      if (typeof v !== "number") continue;
      if (!groups.has(name)) groups.set(name, new Map());
      groups.get(name)?.set(String(r.period), v);
      weight.set(name, (weight.get(name) ?? 0) + (typeof r.count === "number" ? r.count : 1));
    }
    const ordered = [...groups.keys()].sort((a, b) => (weight.get(b) ?? 0) - (weight.get(a) ?? 0));
    const shown = ordered.slice(0, MAX_SERIES);
    // Name order keeps colors stable regardless of which series is biggest.
    const series = shown.sort().map((name) => ({ name, points: groups.get(name) ?? new Map<string, number>(), color: seriesColor(name) }));
    let yMax = 0;
    for (const s of series) for (const v of s.points.values()) yMax = Math.max(yMax, v);
    return { periods, series, yMax: niceMax(yMax), hidden: ordered.length - shown.length };
  }, [rows, measure, seriesKey]);

  if (periods.length === 0) return <p className="px-3 py-2 text-[12px] text-ink-muted">No data.</p>;
  const innerW = W - M.left - M.right;
  const innerH = H - M.top - M.bottom;
  const x = (i: number) => M.left + (periods.length === 1 ? innerW / 2 : (i / (periods.length - 1)) * innerW);
  const y = (v: number) => M.top + innerH - (v / yMax) * innerH;
  const ticks = [0, yMax / 2, yMax];
  const labelEvery = Math.ceil(periods.length / 9);

  // End labels: nudge apart so they don't collide.
  const ends = series
    .map((s) => {
      let lastIdx = -1;
      periods.forEach((p, i) => { if (s.points.has(p)) lastIdx = i; });
      const v = lastIdx >= 0 ? (s.points.get(periods[lastIdx] ?? "") ?? 0) : 0;
      return { name: s.name, color: s.color, x: x(lastIdx), y: y(v) };
    })
    .sort((a, b) => a.y - b.y);
  for (let i = 1; i < ends.length; i++) {
    const prev = ends[i - 1];
    const cur = ends[i];
    if (prev && cur && cur.y - prev.y < 12) cur.y = prev.y + 12;
  }

  const hp = hover !== null ? periods[hover] : undefined;

  return (
    <div className="relative">
      <svg viewBox={`0 0 ${W} ${H}`} className="block w-full" onMouseLeave={() => setHover(null)}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={M.left} x2={W - M.right} y1={y(t)} y2={y(t)} stroke="#eaeaea" />
            <text x={M.left - 6} y={y(t) + 3} textAnchor="end" fontSize="10" fill="#a3a3a3">{fmt(t, measure)}</text>
          </g>
        ))}
        {periods.map((p, i) => (i % labelEvery === 0 || i === periods.length - 1) && (
          <text key={p} x={x(i)} y={H - 6} textAnchor="middle" fontSize="10" fill="#a3a3a3">{shortMonth(p)}</text>
        ))}
        {hp && <line x1={x(hover ?? 0)} x2={x(hover ?? 0)} y1={M.top} y2={M.top + innerH} stroke="#d4d4d4" />}
        {series.map((s) => {
          const d = periods
            .map((p, i) => (s.points.has(p) ? `${x(i)},${y(s.points.get(p) ?? 0)}` : null))
            .filter(Boolean)
            .join(" L ");
          return (
            <g key={s.name}>
              <path d={`M ${d}`} fill="none" stroke={s.color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
              {hp && s.points.has(hp) && <circle cx={x(hover ?? 0)} cy={y(s.points.get(hp) ?? 0)} r="4" fill={s.color} stroke="#fff" strokeWidth="2" />}
            </g>
          );
        })}
        {ends.map((e) => (
          <text key={e.name} x={e.x + 6} y={e.y + 3} fontSize="10.5" fill="#404040">{e.name.length > 14 ? `${e.name.slice(0, 13)}…` : e.name}</text>
        ))}
        {periods.map((p, i) => (
          <rect key={p} x={x(i) - innerW / periods.length / 2} y={M.top} width={innerW / periods.length} height={innerH} fill="transparent" onMouseEnter={() => setHover(i)} />
        ))}
      </svg>
      {hp && (
        <div className="pointer-events-none absolute top-1 rounded-md border border-line bg-surface px-2 py-1 text-[11px]" style={{ left: `${Math.min(70, ((x(hover ?? 0)) / W) * 100)}%` }}>
          <div className="font-medium text-ink">{shortMonth(hp)}</div>
          {series.filter((s) => s.points.has(hp)).map((s) => (
            <div key={s.name} className="flex items-center gap-1.5 text-ink-soft">
              <span className="h-1.5 w-1.5 rounded-full" style={{ background: s.color }} />
              {s.name}: <span className="text-ink">{fmt(s.points.get(hp), measure)}</span>
            </div>
          ))}
        </div>
      )}
      {series.length > 1 && (
        <div className="flex flex-wrap gap-x-3 gap-y-1 px-1 pt-1 text-[11px] text-ink-soft">
          {series.map((s) => (
            <span key={s.name} className="inline-flex items-center gap-1.5"><span className="h-0.5 w-3 rounded" style={{ background: s.color }} />{s.name}</span>
          ))}
          {hidden > 0 && <span className="text-ink-faint">+{hidden} more in table</span>}
        </div>
      )}
    </div>
  );
}

function niceMax(v: number): number {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  const n = v / p;
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * p;
}
