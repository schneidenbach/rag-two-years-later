import { useEffect, useState } from "react";
import type { Mode } from "../lib/types";
import { cx } from "../lib/cx";
import { listRecordings, pinRecording, type Recording } from "../lib/api";
import { seconds } from "../lib/format";
import { Modal } from "./Modal";

const smallButton = "inline-flex items-center justify-center rounded-md border border-line bg-surface px-2 py-1 text-[11px] font-medium text-ink-soft transition hover:border-line-strong hover:text-ink";

export function RecordingsModal({ open, onClose, onReplay }: { open: boolean; onClose: () => void; onReplay: (file: string, mode: Mode) => void }) {
  const [items, setItems] = useState<Recording[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = () => listRecordings().then(setItems).catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)));
  useEffect(() => { if (open) void load(); }, [open]);

  return (
    <Modal open={open} title="Recordings" onClose={onClose}>
      <div className="px-6 py-3">
        <p className="mb-2 text-[12px] text-ink-muted">
          Every live run is recorded. Pin a good run per mode; in Replay mode the buttons play the pinned run with its original timing.
        </p>
        {error && <p className="mb-2 text-[12px] text-step-action">{error}</p>}
        {items.length === 0 && <p className="text-[12px] text-ink-muted">No recordings yet.</p>}
        <ul className="flex flex-col">
          {items.map((r) => (
            <li key={r.file} className="flex items-center gap-2 border-b border-line py-1.5 last:border-b-0">
              <span className={cx("h-1.5 w-1.5 rounded-full", r.ok ? "bg-step-segment" : "bg-step-action")} />
              <span className="w-[120px] font-mono text-[11px] text-ink-soft">{r.mode}</span>
              <span className="min-w-0 flex-1 truncate text-[12px] text-ink" title={r.question}>{r.question}</span>
              {r.durationMs !== undefined && <span className="text-[11px] tabular-nums text-ink-faint">{seconds(r.durationMs)}</span>}
              {r.pinned ? (
                <span className="rounded-full bg-surface-deep px-2 py-0.5 text-[10.5px] font-medium text-ink-muted">pinned</span>
              ) : (
                <button type="button" className={smallButton} onClick={() => void pinRecording(r.file).then(load)}>Pin</button>
              )}
              <button type="button" className={smallButton} onClick={() => onReplay(r.file, r.mode)}>Replay</button>
            </li>
          ))}
        </ul>
      </div>
    </Modal>
  );
}
