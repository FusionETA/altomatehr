import type { SignedInUser } from "@/shared/types/session";
import { buildName } from "../lib/employee-formatters";

export function DashboardView({ user }: { user: SignedInUser }) {
  const cards = [
    { label: "Claims", value: "Ready", note: "My claims list is connected to the API." },
    { label: "Attendance", value: "Next", note: "Clock-in flow will come after claims polish." },
    { label: "Leave", value: "Next", note: "Balances and applications will be ported later." },
  ];

  return (
    <div className="space-y-6">
      <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm sm:p-8">
        <p className="text-xs font-semibold uppercase tracking-[0.16em] text-primary">
          Employee Portal
        </p>
        <h2 className="mt-3 text-3xl font-black tracking-tight text-foreground">
          Welcome back, {buildName(user.email)}
        </h2>
        <p className="mt-2 max-w-2xl text-sm leading-6 text-muted-foreground">
          This is the new Vite employee shell. We will port the real employee workflows one slice
          at a time while keeping the backend clean.
        </p>
      </section>

      <div className="grid gap-4 md:grid-cols-3">
        {cards.map((card) => (
          <article
            key={card.label}
            className="rounded-[24px] border border-border/70 bg-card/90 p-5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm"
          >
            <p className="text-sm font-semibold text-muted-foreground">{card.label}</p>
            <p className="mt-3 text-2xl font-black text-foreground">{card.value}</p>
            <p className="mt-2 text-sm leading-5 text-muted-foreground">{card.note}</p>
          </article>
        ))}
      </div>
    </div>
  );
}
