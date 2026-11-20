import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import type { Mode, StreamEvent } from "./lib/types";
import { emptyRun } from "./lib/types";
import { cx } from "./lib/cx";
import { runLive, runReplay } from "./lib/api";
import { runReducer } from "./lib/run";
import { AgentPane } from "./components/AgentPane";
import { KbPane } from "./components/KbPane";
import { NaivePane } from "./components/NaivePane";
import { RecordingsModal } from "./components/RecordingsModal";

const primaryButtonClass =
  "inline-flex items-center justify-center whitespace-nowrap rounded-lg bg-ink px-2.5 py-1.5 text-[12px] font-medium text-white transition hover:bg-ink-soft disabled:cursor-not-allowed disabled:opacity-50";
const secondaryButtonClass =
  "inline-flex items-center justify-center whitespace-nowrap rounded-lg border border-line bg-surface px-3 py-1.5 text-[12.5px] font-medium text-ink-soft transition hover:border-line-strong hover:text-ink disabled:cursor-not-allowed disabled:opacity-40";
const inputClass =
  "w-full rounded-lg border border-line bg-surface px-2.5 py-1.5 text-[13px] text-ink outline-none transition placeholder:text-ink-faint focus:border-ink";
const toggle = "rounded-md px-2 py-1 text-[11.5px] font-medium transition";

type Demo = 1 | 2;

const defaults: Record<Demo, string> = {
  1: "Why do plumbing jobs take so long to close?",
  2: "Tower B nurses say patient bathrooms are out of service for weeks. Is our parts vendor living up to its contract, and what can we do about it?",
};

const newSession = () => Math.random().toString(36).slice(2);

export function App() {
  const [demo, setDemo] = useState<Demo>(1);
  const [questions, setQuestions] = useState(defaults);
  const [replay, setReplay] = useState(false);
  const [stage, setStage] = useState(false);
  const [showRecordings, setShowRecordings] = useState(false);
  const [right2, setRight2] = useState<"kb" | "agent-full">("kb");
  const [sessionId, setSessionId] = useState(newSession);

  const [naive1, dNaive1] = useReducer(runReducer, undefined, emptyRun);
  const [agent1, dAgent1] = useReducer(runReducer, undefined, emptyRun);
  const [naive2, dNaive2] = useReducer(runReducer, undefined, emptyRun);
  const [kb2, dKb2] = useReducer(runReducer, undefined, emptyRun);
  const [full2, dFull2] = useReducer(runReducer, undefined, emptyRun);
  const aborts = useRef(new Map<Mode, AbortController>());

  const dispatchFor = { "naive-wo": dNaive1, "agent-workorders": dAgent1, "naive-docs": dNaive2, kb: dKb2, "agent-full": dFull2 } as const;
  const anyRunning = [naive1, agent1, naive2, kb2, full2].some((r) => r.status === "running");

  const run = useCallback(
    (mode: Mode, question: string, file: string | null = null) => {
      aborts.current.get(mode)?.abort();
      const ctrl = new AbortController();
      aborts.current.set(mode, ctrl);
      const dispatch = dispatchFor[mode];
      const isReplay = replay || file !== null;
      dispatch({ type: "reset", mode, question, replay: isReplay });
      if (mode === "kb") setRight2("kb");
      if (mode === "agent-full") setRight2("agent-full");
      const onEvent = (event: StreamEvent) => dispatch({ type: "event", event });
      const p = isReplay ? runReplay(mode, file, onEvent, ctrl.signal) : runLive(mode, question, sessionId, onEvent, ctrl.signal);
      p.catch((err: unknown) => {
        if (ctrl.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({ type: "event", event: { type: "error", t: 0, message: isReplay ? `No pinned replay for ${mode}. Pin one in Recordings. (${message})` : message } });
      });
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [replay, sessionId],
  );

  const reset = () => {
    aborts.current.forEach((c) => c.abort());
    for (const d of Object.values(dispatchFor)) d({ type: "reset", mode: null, question: "", replay: false });
    // reset() above marks runs "running"; push them back to idle.
    for (const d of Object.values(dispatchFor)) d({ type: "event", event: { type: "done", t: 0, elapsedMs: 0 } });
    setSessionId(newSession());
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA")) return;
      if (e.key === "1") setDemo(1);
      else if (e.key === "2") setDemo(2);
      else if (e.key.toLowerCase() === "r") setReplay((v) => !v);
      else if (e.key.toLowerCase() === "z") setStage((v) => !v);
      else if (e.key === "Escape") aborts.current.forEach((c) => c.abort());
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const q = questions[demo];
  const setQ = (v: string) => setQuestions((prev) => ({ ...prev, [demo]: v }));

  return (
    <div className={cx("clean-app flex h-screen flex-col bg-surface text-[13px] text-ink", stage && "stage-zoom")}>
      <header className="flex items-center gap-3 border-b border-line px-3 py-1.5">
        <span className="text-[13px] font-semibold tracking-tight">Lakeshore Health</span>
        <span className="text-[12px] text-ink-muted">Facilities</span>
        <nav className="ml-2 flex items-center gap-0.5">
          {([1, 2] as const).map((d) => (
            <button key={d} type="button" onClick={() => setDemo(d)} className={cx(toggle, demo === d ? "bg-surface-deep text-ink" : "text-ink-muted hover:text-ink")}>
              <span className="mr-1 font-mono text-ink-faint">{d}</span>{d === 1 ? "Work orders" : "Documents"}
            </button>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-1">
          <div className="flex items-center rounded-lg border border-line p-0.5">
            <button type="button" onClick={() => setReplay(false)} className={cx(toggle, !replay ? "bg-ink text-white" : "text-ink-muted hover:text-ink")}>Live</button>
            <button type="button" onClick={() => setReplay(true)} className={cx(toggle, replay ? "bg-ink text-white" : "text-ink-muted hover:text-ink")}>Replay</button>
          </div>
          <button type="button" onClick={() => setStage((v) => !v)} className={cx(toggle, stage ? "bg-surface-deep text-ink" : "text-ink-muted hover:text-ink")}>Stage</button>
          <button type="button" onClick={() => setShowRecordings(true)} className={cx(toggle, "text-ink-muted hover:text-ink")}>Recordings</button>
          <button type="button" onClick={reset} className={cx(toggle, "text-ink-muted hover:text-ink")}>Reset</button>
        </div>
      </header>

      <form
        className="flex items-center gap-2 border-b border-line px-3 py-2"
        onSubmit={(e) => {
          e.preventDefault();
          run(demo === 1 ? "agent-workorders" : "kb", q);
        }}
      >
        <input className={inputClass} value={q} onChange={(e) => setQ(e.target.value)} aria-label="Question" />
        {demo === 1 ? (
          <>
            <button type="button" className={secondaryButtonClass} onClick={() => run("naive-wo", q)}>Vector RAG · 2024</button>
            <button type="submit" className={primaryButtonClass}>Agent · 2026</button>
          </>
        ) : (
          <>
            <button type="button" className={secondaryButtonClass} onClick={() => run("naive-docs", q)}>Vector RAG · 2024</button>
            <button type="submit" className={secondaryButtonClass}>Agentic retrieval</button>
            <button type="button" className={primaryButtonClass} onClick={() => run("agent-full", q)}>Agent · data + docs</button>
          </>
        )}
        {anyRunning && <span className="text-[11px] text-ink-faint">Esc to stop</span>}
      </form>

      <main className="grid min-h-0 flex-1 grid-cols-[minmax(240px,24%)_minmax(0,1fr)]">
        {demo === 1 ? (
          <>
            <section className="flex min-h-0 flex-col border-r border-line"><NaivePane run={naive1} title="Vector RAG · 2024" kind="wo" /></section>
            <section className="flex min-h-0 flex-col"><AgentPane run={agent1} title="Agentic search · 2026" /></section>
          </>
        ) : (
          <>
            <section className="flex min-h-0 flex-col border-r border-line"><NaivePane run={naive2} title="Vector RAG · 2024" kind="docs" /></section>
            <section className="flex min-h-0 flex-col">
              <div className="flex items-center gap-0.5 border-b border-line bg-surface-soft px-2 py-1">
                <button type="button" onClick={() => setRight2("kb")} className={cx(toggle, right2 === "kb" ? "bg-surface text-ink" : "text-ink-muted hover:text-ink")}>Agentic retrieval</button>
                <button type="button" onClick={() => setRight2("agent-full")} className={cx(toggle, right2 === "agent-full" ? "bg-surface text-ink" : "text-ink-muted hover:text-ink")}>Agent · data + docs</button>
              </div>
              {right2 === "kb" ? <KbPane run={kb2} title="Azure AI Search · knowledge base" /> : <AgentPane run={full2} title="Agent · work orders + knowledge base" />}
            </section>
          </>
        )}
      </main>

      <RecordingsModal open={showRecordings} onClose={() => setShowRecordings(false)} onReplay={(file, mode) => { setShowRecordings(false); setDemo(mode === "naive-wo" || mode === "agent-workorders" ? 1 : 2); run(mode, "", file); }} />
    </div>
  );
}
