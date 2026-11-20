import type { Json } from "../lib/types";
import { Markdown } from "./Markdown";

const strs = (v: Json | undefined) => (Array.isArray(v) ? v.filter((x): x is string => typeof x === "string") : []);

export function EscalationCard({ draft }: { draft: { [k: string]: Json } }) {
  const evidence = strs(draft.evidenceWorkOrders);
  const cites = strs(draft.documentCitations);
  const asks = strs(draft.asks);
  return (
    <div className="overflow-hidden rounded-lg border border-line-strong">
      <div className="flex items-center gap-2 border-b border-line bg-surface-soft px-3 py-1.5">
        <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--step-action-soft)] px-2 py-0.5 text-[11px] font-medium text-step-action">
          <span className="h-1.5 w-1.5 rounded-full bg-step-action" />Draft · not sent
        </span>
        <span className="text-[11px] text-ink-muted">Human review required</span>
      </div>
      <div className="flex flex-col gap-1.5 px-3 py-2">
        <div className="grid grid-cols-[56px_1fr] gap-x-2 text-[12px]">
          <span className="text-ink-muted">To</span><span className="text-ink">{String(draft.to ?? "")}</span>
          <span className="text-ink-muted">Subject</span><span className="font-semibold text-ink">{String(draft.subject ?? "")}</span>
        </div>
        <Markdown text={String(draft.body ?? "")} className="border-t border-line pt-1.5" />
        {asks.length > 0 && (
          <div>
            <div className="tracked-label mb-0.5">Asks</div>
            <ul className="list-disc pl-4 text-[12px] text-ink-soft">{asks.map((a) => <li key={a}>{a}</li>)}</ul>
          </div>
        )}
        {(evidence.length > 0 || cites.length > 0) && (
          <div className="flex flex-wrap gap-1 pt-0.5">
            {evidence.map((e) => <span key={e} className="rounded-full bg-[var(--step-micro-soft)] px-2 py-0.5 font-mono text-[10.5px] text-step-micro">{e}</span>)}
            {cites.map((c) => <span key={c} className="rounded-full bg-[var(--step-docs-soft)] px-2 py-0.5 text-[10.5px] text-step-docs">{c}</span>)}
          </div>
        )}
      </div>
    </div>
  );
}
