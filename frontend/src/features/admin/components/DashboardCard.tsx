import type { LucideIcon } from "lucide-react";
import { EYEBROW, TILE } from "../lib/dashboard-styles";

// The parts every admin analytics card is assembled from. Shared so the claims
// dashboard and the executive overview cannot drift apart.

export function CardHead({
  icon: Icon,
  title,
  meta,
  tone = "text-primary",
  toneBg = "bg-primary/10",
}: {
  icon: LucideIcon;
  title: string;
  meta?: string;
  tone?: string;
  toneBg?: string;
}) {
  return (
    <div className="flex flex-row items-center justify-between gap-3 pb-3">
      <div className="flex items-center gap-3">
        <div className={`rounded-2xl ${toneBg} p-2.5 ${tone}`}>
          <Icon className="h-[18px] w-[18px]" />
        </div>
        <h3 className="text-base font-black text-foreground">{title}</h3>
      </div>
      {meta ? <span className={EYEBROW}>{meta}</span> : null}
    </div>
  );
}

export function EmptyState({ text }: { text: string }) {
  return (
    <p className="rounded-2xl bg-surface-low px-4 py-6 text-center text-sm text-muted-foreground">
      {text}
    </p>
  );
}

export function Stat({
  label,
  value,
  tone = "text-foreground",
}: {
  label: string;
  value: string;
  tone?: string;
}) {
  return (
    <div className={`min-w-0 ${TILE}`}>
      <p className={`text-xl font-black leading-tight tabular-nums break-words ${tone}`}>{value}</p>
      <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </p>
    </div>
  );
}
