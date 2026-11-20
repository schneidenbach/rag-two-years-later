import type { ReactNode } from "react";

export function Modal({ open, title, children, onClose, width = "max-w-3xl" }: {
  open: boolean;
  title: string;
  children: ReactNode;
  onClose: () => void;
  width?: string;
}) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/30 px-4 py-10" onClick={onClose}>
      <div
        className={`w-full ${width} overflow-hidden rounded-xl border border-line bg-surface shadow-[0_20px_60px_-24px_rgba(0,0,0,0.25)]`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-line px-6 py-4">
          <h2 className="text-[15px] font-semibold tracking-tight text-ink">{title}</h2>
          <button type="button" onClick={onClose} className="text-sm text-ink-muted transition hover:text-ink">Close</button>
        </div>
        <div className="max-h-[78vh] overflow-y-auto scrollbar-thin">{children}</div>
      </div>
    </div>
  );
}
