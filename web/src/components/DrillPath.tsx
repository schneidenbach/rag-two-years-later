import type { Step } from "../lib/types";
import { cx } from "../lib/cx";
import { drillPath, stepTone, type StepKind } from "../lib/steps";

const levels: StepKind[] = ["trend", "segment", "micro", "docs", "action"];

export function DrillPath({ steps, showDocs }: { steps: Step[]; showDocs: boolean }) {
  const { crumbs, reached } = drillPath(steps);
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 border-b border-line bg-surface-soft px-3 py-2">
      <div className="flex items-center gap-1">
        {levels.filter((l) => showDocs || l !== "docs").map((l, i) => {
          const tone = stepTone[l];
          const on = reached.has(l);
          return (
            <div key={l} className="flex items-center gap-1">
              {i > 0 && <span className={cx("h-px w-3", on ? "bg-ink-faint" : "bg-line")} />}
              <span className={cx("inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium transition", on ? cx(tone.text, tone.bg) : "text-ink-faint")}>
                <span className={cx("h-1.5 w-1.5 rounded-full", on ? tone.dot : "bg-line-strong")} />
                {tone.label}
              </span>
            </div>
          );
        })}
      </div>
      <div className="flex min-w-0 items-center gap-1 text-[12px]">
        <span className="text-ink-muted">All work orders</span>
        {crumbs.map((c, i) => (
          <span key={`${c}-${i}`} className="flex items-center gap-1">
            <span className="text-ink-faint">›</span>
            <span className={cx(i === crumbs.length - 1 ? "font-semibold text-ink" : "text-ink-soft", c.startsWith("WO-") && "font-mono text-[11.5px]")}>{c}</span>
          </span>
        ))}
      </div>
    </div>
  );
}
