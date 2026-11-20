import type { Json, KbState, Reference, RunState, Step, StreamEvent, Subquery, ToolStep } from "./types";
import { emptyKb, emptyRun } from "./types";

export type Action = { type: "reset"; mode: RunState["mode"]; question: string; replay: boolean } | { type: "event"; event: StreamEvent };

export function runReducer(state: RunState, action: Action): RunState {
  if (action.type === "reset") return { ...emptyRun(), mode: action.mode, question: action.question, replay: action.replay, status: "running" };
  const e = action.event;
  switch (e.type) {
    case "start":
      return { ...state, status: "running" };
    case "text":
      if (state.mode === "naive-wo" || state.mode === "naive-docs") return { ...state, text: state.text + e.delta };
      return { ...state, steps: appendText(state.steps, e.delta) };
    case "tool_call": {
      const step: ToolStep = { kind: "tool", callId: e.callId, name: e.name, args: e.args, startedAt: e.t };
      return { ...state, steps: [...state.steps, step] };
    }
    case "tool_result":
      return {
        ...state,
        steps: state.steps.map((s) => (s.kind === "tool" && s.callId === e.callId ? { ...s, result: e.result, endedAt: e.t } : s)),
      };
    case "retrieved":
      return { ...state, retrieved: e.items };
    case "kb":
      return { ...state, kb: kbReduce(state.kb, e.event, e.data) };
    case "done":
      return { ...state, status: "done", elapsedMs: e.elapsedMs };
    case "error":
      return { ...state, status: "error", error: e.message };
  }
}

function appendText(steps: Step[], delta: string): Step[] {
  const last = steps[steps.length - 1];
  if (last && last.kind === "text") return [...steps.slice(0, -1), { kind: "text", text: last.text + delta }];
  return [...steps, { kind: "text", text: delta }];
}

/** The final answer is the text after the last tool call (narration lines are short; the answer is not). */
export function splitAnswer(steps: Step[], done: boolean): { trail: Step[]; answer: string | null } {
  const last = steps[steps.length - 1];
  if (last && last.kind === "text" && (done || last.text.length > 220 || last.text.includes("**Finding"))) {
    return { trail: steps.slice(0, -1), answer: last.text };
  }
  return { trail: steps, answer: null };
}

type Obj = { [k: string]: Json };
const isObj = (v: Json | undefined): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const str = (v: Json | undefined) => (typeof v === "string" ? v : undefined);
const n = (v: Json | undefined) => (typeof v === "number" ? v : undefined);

function firstText(v: Json | undefined): string | undefined {
  if (typeof v === "string") return v;
  if (Array.isArray(v)) for (const x of v) { const t = firstText(x); if (t) return t; }
  if (isObj(v)) {
    if (typeof v.text === "string") return v.text;
    for (const k of ["response", "content", "message"]) { const t = firstText(v[k]); if (t) return t; }
  }
  return undefined;
}

function toSubquery(a: Obj, done: boolean): Subquery | null {
  if (str(a.type) !== "searchIndex") return null;
  const args = isObj(a.searchIndexArguments) ? a.searchIndexArguments : {};
  return { id: n(a.id) ?? 0, search: str(args.search) ?? "…", count: n(a.count), elapsedMs: n(a.elapsedMs), done };
}

export function toReference(r: Json): Reference | null {
  if (!isObj(r)) return null;
  const sd = isObj(r.sourceData) ? r.sourceData : r;
  return {
    ref_id: String(r.ref_id ?? r.id ?? ""),
    subquery: n(r.activitySource) ?? n(r.subquery),
    doc_id: str(sd.doc_id),
    title: str(sd.title),
    section: str(sd.section),
    doc_type: str(sd.doc_type),
    rerankerScore: n(r.rerankerScore),
    content: str(sd.content),
  };
}

function upsert(list: Subquery[], s: Subquery): Subquery[] {
  const i = list.findIndex((x) => x.id === s.id);
  if (i < 0) return [...list, s];
  const copy = list.slice();
  copy[i] = { ...copy[i], ...s, search: s.search === "…" ? (copy[i]?.search ?? "…") : s.search };
  return copy;
}

function applyFull(kb: KbState, resp: Obj): KbState {
  let next = kb;
  if (Array.isArray(resp.activity)) {
    for (const a of resp.activity) {
      if (!isObj(a)) continue;
      if (str(a.type) === "modelQueryPlanning") next = { ...next, planning: "done", planMs: n(a.elapsedMs) };
      const sq = toSubquery(a, true);
      if (sq) next = { ...next, subqueries: upsert(next.subqueries, sq) };
    }
  }
  if (Array.isArray(resp.references)) next = { ...next, references: resp.references.map(toReference).filter((x): x is Reference => !!x) };
  const answer = firstText(resp.response);
  if (answer) next = { ...next, answer };
  return next;
}

export function kbReduce(kb: KbState, event: string, data: Json): KbState {
  switch (event) {
    case "retrieval.started":
      return { ...emptyKb(), planning: "running" };
    case "activity.started":
    case "activity.completed": {
      if (!isObj(data)) return kb;
      const done = event === "activity.completed";
      if (str(data.type) === "modelQueryPlanning")
        return { ...kb, planning: done ? "done" : "running", planMs: done ? n(data.elapsedMs) : kb.planMs };
      const sq = toSubquery(data, done);
      return sq ? { ...kb, planning: "done", subqueries: upsert(kb.subqueries, sq) } : kb;
    }
    case "answer.completed":
      return { ...kb, answer: firstText(data) ?? kb.answer };
    case "references.completed": {
      const arr = Array.isArray(data) ? data : isObj(data) && Array.isArray(data.references) ? data.references : [];
      return { ...kb, references: arr.map(toReference).filter((x): x is Reference => !!x) };
    }
    case "response.completed":
      if (isObj(data) && isObj(data.response)) return applyFull(kb, data.response);
      if (isObj(data)) return applyFull(kb, data);
      return kb;
    default:
      return kb;
  }
}

/** Tool-result payload for search_facilities_knowledge -> KbState, so the trail can reuse the KB view. */
export function kbFromToolResult(result: Json | undefined): KbState | null {
  if (!isObj(result) || result.kind !== "knowledge_retrieval") return null;
  const subqueries = (Array.isArray(result.subqueries) ? result.subqueries : [])
    .filter(isObj)
    .map((s) => ({ id: n(s.id) ?? 0, search: str(s.search) ?? "", count: n(s.count), elapsedMs: n(s.elapsedMs), done: true }));
  const references = (Array.isArray(result.references) ? result.references : []).map(toReference).filter((x): x is Reference => !!x);
  const plan = isObj(result.plan) ? result.plan : null;
  return { planning: "done", planMs: plan ? n(plan.elapsedMs) : undefined, subqueries, references, answer: str(result.answer) ?? "" };
}
