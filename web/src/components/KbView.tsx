import type { KbState, Reference } from "../lib/types";
import { cx } from "../lib/cx";
import { Markdown } from "./Markdown";

/** Agentic retrieval, staged: plan -> subqueries (live) -> references -> cited answer. */
export function KbView({ kb, running, showAnswer = true }: { kb: KbState; running: boolean; showAnswer?: boolean }) {
  const byQuery = new Map<number | undefined, Reference[]>();
  for (const r of kb.references) byQuery.set(r.subquery, [...(byQuery.get(r.subquery) ?? []), r]);
  const docs = new Set(kb.references.map((r) => r.doc_id).filter(Boolean));
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 text-[12px]">
        <span className={cx("h-1.5 w-1.5 rounded-full", kb.planning === "done" ? "bg-step-docs" : "bg-line-strong")} />
        <span className="font-medium text-ink">Query plan</span>
        <span className="text-ink-muted">
          {kb.planning === "done" ? `${kb.subqueries.length} subqueries${kb.planMs ? ` · planned in ${(kb.planMs / 1000).toFixed(1)}s` : ""}` : running ? "planning…" : ""}
        </span>
        {docs.size > 0 && <span className="ml-auto text-[11px] text-ink-faint">{kb.references.length} passages from {docs.size} documents</span>}
      </div>
      <ol className="flex flex-col gap-1">
        {kb.subqueries.map((s, i) => (
          <li key={s.id} className="rounded-md border border-line px-2 py-1">
            <div className="flex items-center gap-2 text-[12px]">
              <span className="w-4 text-right font-mono text-[10.5px] text-ink-faint">{i + 1}</span>
              <span className={cx("min-w-0 flex-1 truncate", s.done ? "text-ink" : "text-ink-muted")} title={s.search}>{s.search}</span>
              <span className="whitespace-nowrap text-[11px] text-ink-faint">{s.done ? `${s.count ?? 0} hits` : "searching…"}</span>
            </div>
            {(byQuery.get(s.id) ?? []).length > 0 && (
              <div className="mt-1 flex flex-wrap gap-1 pl-6">
                {(byQuery.get(s.id) ?? []).slice(0, 6).map((r) => <RefChip key={r.ref_id} r={r} />)}
              </div>
            )}
          </li>
        ))}
      </ol>
      {kb.references.some((r) => r.subquery === undefined) && (
        <div className="flex flex-wrap gap-1">{kb.references.filter((r) => r.subquery === undefined).slice(0, 10).map((r) => <RefChip key={r.ref_id} r={r} />)}</div>
      )}
      {showAnswer && kb.answer && (
        <div className="rounded-md border border-line px-2.5 py-2">
          <div className="tracked-label mb-1">Grounded answer</div>
          <Markdown text={kb.answer} />
        </div>
      )}
    </div>
  );
}

function RefChip({ r }: { r: Reference }) {
  return (
    <span
      className="inline-flex max-w-[260px] items-center gap-1 rounded-full bg-[var(--step-docs-soft)] px-2 py-0.5 text-[10.5px] text-step-docs"
      title={`${r.title ?? ""} — ${r.section ?? ""}\n\n${(r.content ?? "").slice(0, 400)}`}
    >
      <span className="font-semibold">{r.ref_id}</span>
      <span className="truncate">{r.doc_id} · {r.section}</span>
    </span>
  );
}
