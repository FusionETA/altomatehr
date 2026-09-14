// The app boot splash, shown while the session is restored on a hard refresh.
//
// Modelled on the reference app's AppSplash: a branded card with the logo, the
// wordmark and a ring spinner, so a reload lands on something that looks like
// AltomateHR rather than a bare "Loading" line. The spinner is a CSS ring
// (a faint circle with one solid quarter that rotates) rather than an icon, so
// it matches the reference exactly and carries no icon dependency.
export function LoadingScreen({ label = "Opening AltomateHR…" }: { label?: string }) {
  return (
    <div className="flex min-h-[100svh] w-full items-center justify-center bg-background px-6 py-10">
      <div className="w-full max-w-[28rem] rounded-[32px] border border-border/60 bg-card/90 px-8 py-9 text-center shadow-ambient backdrop-blur-xl">
        <div className="flex justify-center">
          <img
            src="/brand-icon.png"
            alt="AltomateHR"
            width={512}
            height={512}
            className="h-auto w-[86px] object-contain"
          />
        </div>

        <p className="mt-6 text-[2rem] font-black tracking-tight text-primary">AltomateHR</p>

        <div className="mt-5 flex justify-center">
          <span className="inline-flex size-11 items-center justify-center rounded-full border border-primary/20 bg-primary/10">
            <span className="size-5 animate-spin rounded-full border-2 border-primary/20 border-t-primary motion-reduce:animate-none" />
          </span>
        </div>

        <p className="mt-5 text-base font-semibold text-foreground">{label}</p>
        <p className="mt-1 text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
          Please wait
        </p>
      </div>
    </div>
  );
}
