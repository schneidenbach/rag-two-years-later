import type { Json, Step, ToolStep } from "./types";

export type StepKind = "trend" | "segment" | "micro" | "docs" | "action";

export const stepTone: Record<StepKind, { text: string; bg: string; dot: string; label: string }> = {
  trend: { text: "text-step-trend", bg: "bg-[var(--step-trend-soft)]", dot: "bg-step-trend", label: "Trend" },
  segment: { text: "text-step-segment", bg: "bg-[var(--step-segment-soft)]", dot: "bg-step-segment", label: "Segment" },
  micro: { text: "text-step-micro", bg: "bg-[var(--step-micro-soft)]", dot: "bg-step-micro", label: "Work orders" },
  docs: { text: "text-step-docs", bg: "bg-[var(--step-docs-soft)]", dot: "bg-step-docs", label: "Documents" },
  action: { text: "text-step-action", bg: "bg-[var(--step-action-soft)]", dot: "bg-step-action", label: "Action" },
};

type Obj = { [k: string]: Json };
const isObj = (v: Json | undefined): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);

export function queryOf(step: ToolStep): Obj {
  const q = step.args.query;
  return isObj(q) ? q : step.args;
}

export function filtersOf(step: ToolStep): Obj {
  const q = queryOf(step);
  const f = isObj(q.filters) ? q.filters : isObj(step.args.filters) ? step.args.filters : {};
  return f;
}

export function classify(step: ToolStep): StepKind {
  switch (step.name) {
    case "query_work_order_data":
      return queryOf(step).timeSeries === true ? "trend" : "segment";
    case "find_work_orders":
    case "get_work_order_details":
      return "micro";
    case "search_facilities_knowledge":
      return "docs";
    case "draft_escalation":
      return "action";
    default:
      return "segment";
  }
}

const arr = (v: Json | undefined): string[] => (Array.isArray(v) ? v.filter((x): x is string => typeof x === "string") : []);

/** Human-readable chips describing what a tool call asked for. */
export function argChips(step: ToolStep): string[] {
  if (step.name === "get_work_order_details") return arr(step.args.woNumbers);
  if (step.name === "search_facilities_knowledge") return [String(step.args.question ?? "")];
  if (step.name === "draft_escalation") return [String(step.args.to ?? "")];
  if (step.name === "get_filter_values") return [`${String(step.args.field ?? "")} values`];
  const f = filtersOf(step);
  const q = queryOf(step);
  const chips: string[] = [];
  if (typeof f.startDate === "string" && typeof f.endDate === "string") chips.push(`${f.startDate.slice(0, 7)} → ${f.endDate.slice(0, 7)}`);
  for (const k of ["categories", "subcategories", "campuses", "buildings", "priorities", "locationTypes", "partsVendors", "assignedWorkers", "assignedWorkerCategories", "statuses"])
    chips.push(...arr(f[k]));
  if (f.afterHours === true) chips.push("After hours");
  if (f.afterHours === false) chips.push("Business hours");
  if (f.partsOrdered === true) chips.push("Parts ordered");
  if (f.partsOrdered === false) chips.push("No parts");
  if (f.wasReassigned === true) chips.push("Reassigned");
  if (q.timeSeries === true) chips.push(`by ${String(q.timeGrain ?? "Month").toLowerCase()}`);
  for (const d of arr(q.dimensions)) chips.push(`by ${d}`);
  if (typeof step.args.actionNotesContain === "string") chips.push(`notes ~ "${step.args.actionNotesContain}"`);
  if (typeof step.args.textContains === "string") chips.push(`text ~ "${step.args.textContains}"`);
  return chips;
}

/** Breadcrumb from the most specific population the agent has looked at so far. */
export function drillPath(steps: Step[]): { crumbs: string[]; reached: Set<StepKind> } {
  const reached = new Set<StepKind>();
  let crumbs: string[] = [];
  let depth = -1;
  for (const s of steps) {
    if (s.kind !== "tool") continue;
    const kind = classify(s);
    reached.add(kind);
    if (s.name === "query_work_order_data" || s.name === "find_work_orders") {
      const f = filtersOf(s);
      const c: string[] = [];
      for (const k of ["categories", "campuses", "buildings", "subcategories", "partsVendors", "priorities", "assignedWorkers"]) {
        const v = arr(f[k]);
        if (v.length) c.push(v.join(", "));
      }
      if (f.afterHours === true) c.push("After hours");
      if (f.partsOrdered === true) c.push("Parts ordered");
      if (c.length >= depth) { crumbs = c; depth = c.length; }
    }
    if (s.name === "get_work_order_details") {
      const wos = arr(s.args.woNumbers);
      if (wos.length) crumbs = [...crumbs.filter((x) => !x.startsWith("WO-")), wos[0] + (wos.length > 1 ? ` +${wos.length - 1}` : "")];
    }
  }
  return { crumbs, reached };
}
