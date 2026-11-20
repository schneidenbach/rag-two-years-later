export type Json = string | number | boolean | null | Json[] | { [k: string]: Json };
export type Row = Record<string, Json>;

export type Mode = "agent-workorders" | "agent-full" | "naive-wo" | "naive-docs" | "kb";

export type StreamEvent =
  | { type: "start"; t: number; mode: Mode; question: string; replay?: boolean }
  | { type: "text"; t: number; delta: string }
  | { type: "tool_call"; t: number; callId: string; name: string; args: Record<string, Json> }
  | { type: "tool_result"; t: number; callId: string; name?: string; result: Json }
  | { type: "retrieved"; t: number; items: Row[] }
  | { type: "kb"; t: number; event: string; data: Json }
  | { type: "done"; t: number; elapsedMs: number }
  | { type: "error"; t: number; message: string };

export type ToolStep = {
  kind: "tool";
  callId: string;
  name: string;
  args: Record<string, Json>;
  result?: Json;
  startedAt: number;
  endedAt?: number;
};
export type TextStep = { kind: "text"; text: string };
export type Step = ToolStep | TextStep;

export type Subquery = { id: number; search: string; count?: number; elapsedMs?: number; done: boolean };
export type Reference = {
  ref_id: string;
  subquery?: number;
  doc_id?: string;
  title?: string;
  section?: string;
  doc_type?: string;
  rerankerScore?: number;
  content?: string;
};
export type KbState = {
  planning: "pending" | "running" | "done";
  planMs?: number;
  subqueries: Subquery[];
  references: Reference[];
  answer: string;
};

export type RunState = {
  mode: Mode | null;
  question: string;
  status: "idle" | "running" | "done" | "error";
  replay: boolean;
  steps: Step[];
  retrieved: Row[];
  text: string; // naive answer stream
  kb: KbState;
  elapsedMs: number;
  error?: string;
};

export const emptyKb = (): KbState => ({ planning: "pending", subqueries: [], references: [], answer: "" });
export const emptyRun = (): RunState => ({
  mode: null, question: "", status: "idle", replay: false, steps: [], retrieved: [], text: "", kb: emptyKb(), elapsedMs: 0,
});
