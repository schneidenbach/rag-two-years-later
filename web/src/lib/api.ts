import type { Mode, StreamEvent } from "./types";

const base = (import.meta.env.VITE_API_URL as string | undefined) ?? "http://localhost:5158";

async function stream(path: string, body: unknown, onEvent: (e: StreamEvent) => void, signal: AbortSignal) {
  const res = await fetch(`${base}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal,
  });
  if (!res.ok || !res.body) throw new Error(`${path} failed: ${res.status}`);
  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let idx: number;
    while ((idx = buffer.indexOf("\n\n")) >= 0) {
      const chunk = buffer.slice(0, idx);
      buffer = buffer.slice(idx + 2);
      const line = chunk.split("\n").find((l) => l.startsWith("data:"));
      if (line) onEvent(JSON.parse(line.slice(5)) as StreamEvent);
    }
  }
}

export function runLive(mode: Mode, question: string, sessionId: string, onEvent: (e: StreamEvent) => void, signal: AbortSignal) {
  return stream("/api/run", { mode, question, sessionId }, onEvent, signal);
}

export function runReplay(mode: Mode, file: string | null, onEvent: (e: StreamEvent) => void, signal: AbortSignal, speed = 1) {
  return stream("/api/replay", { mode, file, speed }, onEvent, signal);
}

export type Recording = { file: string; mode: Mode; question: string; ok: boolean; pinned: boolean; durationMs?: number };

export async function listRecordings(): Promise<Recording[]> {
  const res = await fetch(`${base}/api/recordings`);
  return (await res.json()) as Recording[];
}

export async function pinRecording(file: string): Promise<void> {
  await fetch(`${base}/api/recordings/pin`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ file }) });
}
