import { useEffect, useRef } from "react";
import type { RunState } from "../lib/types";
import { seconds } from "../lib/format";
import { splitAnswer } from "../lib/run";
import { DrillPath } from "./DrillPath";
import { Markdown } from "./Markdown";
import { ToolCard } from "./ToolCard";

export function AgentPane({ run, title }: { run: RunState; title: string }) {
  const { trail, answer } = splitAnswer(run.steps, run.status !== "running");
  const scroller = useRef<HTMLDivElement>(null);
  const toolCount = run.steps.filter((s) => s.kind === "tool").length;

  useEffect(() => {
    const el = scroller.current;
    if (el && run.status === "running") el.scrollTo({ top: el.scrollHeight, behavior: "smooth" });
  }, [run.steps, run.status]);

  let n = 0;
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <PaneHeader title={title} run={run} meta={toolCount ? `${toolCount} tool calls` : ""} />
      {run.steps.length > 0 && <DrillPath steps={run.steps} showDocs={run.mode === "agent-full"} />}
      <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(320px,38%)]">
        <div ref={scroller} className="min-h-0 overflow-y-auto px-3 py-2 scrollbar-thin">
          <div className="flex flex-col gap-2">
            {trail.map((s, i) =>
              s.kind === "text" ? (
                <p key={i} className="text-[12.5px] italic text-ink-soft">{s.text.trim()}</p>
              ) : (
                <ToolCard key={s.callId} step={s} index={++n} />
              ),
            )}
            {run.status === "running" && trail.length === 0 && <p className="text-[12px] text-ink-muted">Thinking…</p>}
          </div>
        </div>
        <div className="min-h-0 overflow-y-auto border-l border-line bg-surface-soft px-3 py-2 scrollbar-thin">
          <div className="tracked-label mb-1">Findings</div>
          {answer ? <Markdown text={answer} streaming={run.status === "running"} /> : <p className="text-[12px] text-ink-muted">{run.status === "idle" ? "Run the agent to investigate." : "Investigating…"}</p>}
          {run.status === "error" && <p className="mt-2 text-[12px] text-step-action">{run.error}</p>}
        </div>
      </div>
    </div>
  );
}

export function PaneHeader({ title, run, meta }: { title: string; run: RunState; meta?: string }) {
  return (
    <div className="flex items-center gap-2 border-b border-line px-3 py-1.5">
      <span className="text-[12.5px] font-semibold text-ink">{title}</span>
      {run.replay && run.status !== "idle" && <span className="rounded-full bg-surface-deep px-2 py-0.5 text-[10.5px] font-medium text-ink-muted">replay</span>}
      <span className="ml-auto text-[11px] text-ink-faint">
        {[meta, run.status === "running" ? "running…" : run.status === "done" ? seconds(run.elapsedMs) : ""].filter(Boolean).join(" · ")}
      </span>
    </div>
  );
}
