// The shared Tailwind strings the payroll screens are built from.
//
// Lifted verbatim from the claims and settings surfaces so payroll does not
// arrive looking like a different application. Kept here rather than
// repeated per component, because a card radius that drifts by 4px across
// four files is the kind of thing nobody notices and everybody sees.

export const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

// The field look, WITHOUT a size.
//
// Tailwind emits its utilities grouped by property, not in the order the
// classes appear on the element — so `${INPUT} h-10 w-32` lost to the
// `h-12 w-full` baked into INPUT and silently rendered full-size. Height
// now comes from the variant, and a narrower field gets its width from a
// wrapper rather than from a class that cannot win.
const FIELD =
  "rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background disabled:opacity-50";

export const INPUT = `h-12 w-full ${FIELD}`;

// For dense rows — a repeating line item, a toolbar.
export const INPUT_SM = `h-10 w-full ${FIELD}`;

// Grows with its content, so no height at all.
export const TEXTAREA = `w-full py-3 ${FIELD}`;

// The bottom margin is not decoration. Every control below carries a
// `focus-visible:ring-2` at `ring-offset-2`, which paints 4px OUTSIDE the
// box — with the label sitting flush on top, the focus ring struck through
// its text.
export const LABEL = "mb-1.5 block text-sm font-semibold text-foreground";

export const HINT = "mt-1 text-xs text-muted-foreground";

// Shared by all three button styles.
//
// The focus ring is not decoration: the whole payroll surface is forms and
// tables, so it is genuinely keyboard-driven, and it is the convention the
// app's own `select.tsx` and `switch.tsx` already use. The pressed state
// gives a click somewhere to land — without it a Generate that takes three
// seconds reads as a button that did nothing.
const BUTTON_BASE =
  "inline-flex h-11 items-center justify-center gap-2 rounded-2xl px-5 text-sm font-semibold shadow-sm transition active:scale-[0.98] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background disabled:cursor-not-allowed disabled:opacity-50 disabled:active:scale-100";

export const BUTTON = `${BUTTON_BASE} bg-primary text-primary-foreground hover:bg-primary/90`;

export const BUTTON_GHOST = `${BUTTON_BASE} border border-border bg-card text-foreground hover:bg-muted/60`;

// Reverting a filed month and deleting a draft both destroy work. They read
// as destructive so neither is pressed by muscle memory.
export const BUTTON_DANGER = `${BUTTON_BASE} border border-destructive/30 bg-destructive/5 text-destructive hover:bg-destructive/10 focus-visible:ring-destructive`;

export const BADGE =
  "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-semibold";

export const ERROR_PANEL =
  "rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive";

export const NOTE_PANEL =
  "rounded-[28px] border border-border/70 bg-card/90 p-6 text-sm text-muted-foreground shadow-ambient backdrop-blur-sm";

// A warning the admin should act on but which does not block them.
export const WARN_PANEL =
  "rounded-2xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-800 dark:text-amber-300";

export const TH =
  "whitespace-nowrap px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-muted-foreground";

export const TD = "whitespace-nowrap px-3 py-2.5 text-sm";

// Money columns are right-aligned and tabular so digits line up down the
// column — a payroll table is read by scanning for an outlier.
export const TD_NUM = `${TD} text-right tabular-nums`;
export const TH_NUM = `${TH} text-right`;
