import type { RunState } from "../lib/types";
import { KbView } from "./KbView";
import { PaneHeader } from "./AgentPane";

export function KbPane({ run, title }: { run: RunState; title: string }) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <PaneHeader title={title} run={run} meta={run.kb.subqueries.length ? `${run.kb.subqueries.length} subqueries` : ""} />
      <div className="min-h-0 flex-1 overflow-y-auto px-3 py-2 scrollbar-thin">
        {run.status === "idle" ? (
          <p className="text-[12px] text-ink-muted">The knowledge base plans subqueries, searches in parallel, reranks, and answers with citations.</p>
        ) : (
          <KbView kb={run.kb} running={run.status === "running"} />
        )}
        {run.status === "error" && <p className="text-[12px] text-step-action">{run.error}</p>}
      </div>
    </div>
  );
}
