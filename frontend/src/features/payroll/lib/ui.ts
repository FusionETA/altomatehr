// The shared Tailwind strings the payroll screens are built from.
//
// Lifted verbatim from the claims and settings surfaces so payroll does not
// arrive looking like a different application. Kept here rather than
// repeated per component, because a card radius that drifts by 4px across
// four files is the kind of thing nobody notices and everybody sees.

export const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

// The keyboard focus treatment every interactive element on this surface
// carries. Factored out because it was retyped on every field, button and
// icon button — and, on a couple of them, quietly forgotten. Compose it;
// don't respell it.
export const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background";

// The field look, WITHOUT a size.
//
// Tailwind emits its utilities grouped by property, not in the order the
// classes appear on the element — so `${INPUT} h-10 w-32` lost to the
// `h-12 w-full` baked into INPUT and silently rendered full-size. Height
// now comes from the variant, and a narrower field gets its width from a
// wrapper rather than from a class that cannot win.
const FIELD =
  `rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground ${FOCUS_RING} disabled:opacity-50`;

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

// Shared by every button style — layout and interaction only. Size and colour
// come from the tokens below, deliberately: a height baked in here could not be
// overridden by an appended `h-9` (Tailwind orders by property, not by source —
// the same trap the FIELD note describes), so the dense variants would silently
// render full-size. Keeping size out means a *_SM token can actually win.
//
// The focus ring is not decoration: the whole payroll surface is forms and
// tables, so it is genuinely keyboard-driven, and it is the convention the
// app's own `select.tsx` and `switch.tsx` already use. The pressed state
// gives a click somewhere to land — without it a Generate that takes three
// seconds reads as a button that did nothing.
const BUTTON_BASE =
  `inline-flex items-center justify-center gap-2 font-semibold shadow-sm transition active:scale-[0.98] ${FOCUS_RING} disabled:cursor-not-allowed disabled:opacity-50 disabled:active:scale-100`;

const SIZE_MD = "h-11 rounded-2xl px-5 text-sm";
// A dense size for toolbars and repeating rows.
const SIZE_SM = "h-9 rounded-xl px-3 text-xs";

const COLOR_PRIMARY = "bg-primary text-primary-foreground hover:bg-primary/90";
const COLOR_GHOST = "border border-border bg-card text-foreground hover:bg-muted/60";
// Reverting a filed month and deleting a draft both destroy work. They read
// as destructive so neither is pressed by muscle memory.
const COLOR_DANGER =
  "border border-destructive/30 bg-destructive/5 text-destructive hover:bg-destructive/10 focus-visible:ring-destructive";

export const BUTTON = `${BUTTON_BASE} ${SIZE_MD} ${COLOR_PRIMARY}`;
export const BUTTON_GHOST = `${BUTTON_BASE} ${SIZE_MD} ${COLOR_GHOST}`;
export const BUTTON_DANGER = `${BUTTON_BASE} ${SIZE_MD} ${COLOR_DANGER}`;

// The same three, dense — for toolbar and per-row actions. Compose these
// directly; never append a size to a full BUTTON (the h-11 wins).
export const BUTTON_SM = `${BUTTON_BASE} ${SIZE_SM} ${COLOR_PRIMARY}`;
export const BUTTON_GHOST_SM = `${BUTTON_BASE} ${SIZE_SM} ${COLOR_GHOST}`;
export const BUTTON_DANGER_SM = `${BUTTON_BASE} ${SIZE_SM} ${COLOR_DANGER}`;

// An icon-only control — a drawer's close, a row's remove. No label and no
// fill, so it shares only the focus ring with the buttons above; it must keep
// it, or tabbing onto a close button lands on nothing visible.
export const ICON_BUTTON =
  `rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50 ${FOCUS_RING}`;

// The same, for an icon whose action destroys something: it reddens on hover.
export const ICON_BUTTON_DANGER =
  `rounded-full p-2 text-muted-foreground transition hover:bg-destructive/10 hover:text-destructive disabled:cursor-not-allowed disabled:opacity-50 ${FOCUS_RING}`;

// A button that reads as a link — inline, no fill, primary text — for a
// "see all" affordance inside a card rather than a form's main action.
export const LINK_BUTTON =
  `inline-flex items-center gap-1.5 rounded-lg text-sm font-semibold text-primary transition hover:text-primary/80 ${FOCUS_RING}`;

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
