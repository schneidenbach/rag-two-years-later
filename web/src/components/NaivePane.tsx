import type { RunState } from "../lib/types";
import { fmt } from "../lib/format";
import { Markdown } from "./Markdown";
import { PaneHeader } from "./AgentPane";

/** The 2024 baseline: top-5 nearest chunks + one completion. */
export function NaivePane({ run, title, kind }: { run: RunState; title: string; kind: "wo" | "docs" }) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <PaneHeader title={title} run={run} meta={run.retrieved.length ? `top ${run.retrieved.length} by similarity` : ""} />
      <div className="min-h-0 flex-1 overflow-y-auto px-3 py-2 scrollbar-thin">
        {run.status === "idle" && <p className="text-[12px] text-ink-muted">Embed the question, take the 5 most similar {kind === "wo" ? "work orders" : "passages"}, answer.</p>}
        {run.retrieved.length > 0 && (
          <ol className="mb-2 flex flex-col gap-1">
            {run.retrieved.map((r, i) => (
              <li key={i} className="rounded-md border border-line px-2 py-1 text-[11.5px]">
                <div className="flex items-center gap-2">
                  <span className="font-mono text-[10.5px] text-ink">{fmt(kind === "wo" ? r.wo_number : r.doc_id)}</span>
                  <span className="ml-auto text-[10.5px] tabular-nums text-ink-faint">{fmt(r.similarity ?? r.score)}</span>
                </div>
                <div className="text-ink-soft">{fmt(kind === "wo" ? r.title : r.section)}</div>
              </li>
            ))}
          </ol>
        )}
        {run.text && (
          <div className="rounded-md border border-line px-2.5 py-2">
            <div className="tracked-label mb-1">Answer</div>
            <Markdown text={run.text} streaming={run.status === "running"} />
          </div>
        )}
        {run.status === "error" && <p className="text-[12px] text-step-action">{run.error}</p>}
      </div>
    </div>
  );
}
