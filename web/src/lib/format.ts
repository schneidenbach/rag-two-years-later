import type { Json } from "./types";

export function label(key: string): string {
  const map: Record<string, string> = {
    count: "Work orders",
    avg_days_to_close: "Avg days to close",
    median_days_to_close: "Median days to close",
    p90_days_to_close: "P90 days to close",
    avg_days_to_first_response: "Avg days to first response",
    avg_days_waiting_on_parts: "Avg days waiting on parts",
    parts_ordered_rate: "Parts ordered rate",
    reassignment_rate: "Reassignment rate",
    avg_labor_hours: "Avg labor hours",
    open_count: "Open",
  };
  return map[key] ?? key.replace(/_/g, " ").replace(/^\w/, (c) => c.toUpperCase());
}

export function fmt(v: Json | undefined, key = ""): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "number") {
    if (key.endsWith("_rate")) return `${(v * 100).toFixed(0)}%`;
    if (Number.isInteger(v)) return v.toLocaleString();
    return v.toFixed(Math.abs(v) >= 100 ? 0 : 1);
  }
  if (typeof v === "boolean") return v ? "Yes" : "No";
  return String(v);
}

export function num(v: Json | undefined): number | null {
  return typeof v === "number" ? v : null;
}

export function shortMonth(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return d.toLocaleDateString(undefined, { month: "short", year: "2-digit" });
}

export function seconds(ms: number): string {
  return `${(ms / 1000).toFixed(1)}s`;
}
