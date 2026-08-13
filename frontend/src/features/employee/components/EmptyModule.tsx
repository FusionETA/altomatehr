export function EmptyModule({ title, body }: { title: string; body: string }) {
  return (
    <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm">
      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        Coming next
      </p>
      <h2 className="mt-3 text-2xl font-bold text-foreground">{title}</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 text-muted-foreground">{body}</p>
    </section>
  );
}
