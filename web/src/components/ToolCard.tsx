import type { Json, Row, ToolStep } from "../lib/types";
import { cx } from "../lib/cx";
import { seconds } from "../lib/format";
import { kbFromToolResult } from "../lib/run";
import { argChips, classify, stepTone } from "../lib/steps";
import { AggregateView, parseAggregate } from "./AggregateView";
import { DataTable } from "./DataTable";
import { EscalationCard } from "./EscalationCard";
import { KbView } from "./KbView";
import { Timeline } from "./Timeline";
import { WorkOrderList } from "./WorkOrderList";

type Obj = { [k: string]: Json };
const isObj = (v: Json | undefined): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);

export function ToolCard({ step, index }: { step: ToolStep; index: number }) {
  const kind = classify(step);
  const tone = stepTone[kind];
  const chips = argChips(step);
  const pending = step.result === undefined;
  return (
    <div className="rounded-lg border border-line">
      <div className="flex flex-wrap items-center gap-1.5 border-b border-line px-2.5 py-1.5">
        <span className="w-4 text-right font-mono text-[10.5px] text-ink-faint">{index}</span>
        <span className={cx("inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium", tone.text, tone.bg)}>
          <span className={cx("h-1.5 w-1.5 rounded-full", tone.dot)} />{tone.label}
        </span>
        <span className="font-mono text-[11px] text-ink-soft">{step.name}</span>
        {chips.map((c, i) => (
          <span key={`${c}-${i}`} className="max-w-[420px] truncate rounded-full border border-line px-2 py-0.5 text-[11px] text-ink-soft" title={c}>{c}</span>
        ))}
        <span className="ml-auto text-[11px] text-ink-faint">{pending ? "running…" : step.endedAt !== undefined ? seconds(step.endedAt - step.startedAt) : ""}</span>
      </div>
      {!pending && <div className="px-2.5 py-2"><Result step={step} /></div>}
    </div>
  );
}

function Result({ step }: { step: ToolStep }) {
  const r = step.result;
  const agg = parseAggregate(r);
  if (agg) return <AggregateView agg={agg} />;
  const kb = kbFromToolResult(r);
  if (kb) return <KbView kb={kb} running={false} showAnswer={false} />;
  if (isObj(r)) {
    if (r.kind === "work_order_list") return <WorkOrderList rows={(Array.isArray(r.rows) ? r.rows : []) as Row[]} />;
    if (r.kind === "work_order_details") return <Timeline workOrders={(Array.isArray(r.workOrders) ? r.workOrders : []) as Row[]} />;
    if (r.kind === "escalation_draft") return <EscalationCard draft={r} />;
    if (r.kind === "filter_values") return <DataTable rows={(Array.isArray(r.values) ? r.values : []) as Row[]} />;
  }
  return <pre className="max-h-40 overflow-auto font-mono text-[11px] text-ink-muted scrollbar-thin">{typeof r === "string" ? r : JSON.stringify(r, null, 2)}</pre>;
}
