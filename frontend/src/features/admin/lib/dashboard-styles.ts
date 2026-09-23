// The admin dashboard's shared surface language. Every analytics card — the
// executive overview's and the claims dashboard's — is built from these, so the
// two read as one system rather than two screens that happen to be adjacent.

export const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

// A card with its own padding, for sections that need to run a table to the edge.
export const CARD_BARE =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

export const TILE = "rounded-2xl border border-border/60 bg-surface-low p-4";

export const EYEBROW =
  "text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground";

// A dashboard list capped at roughly five tiles, the rest behind a scroll.
//
// These cards sit in a column beside others, and a list that runs to nine or
// ten entries pushes everything below it off the screen — usually to show rows
// reading 0d, since a card is normally sorted with the interesting ones first.
//
// A height cap rather than a slice: every row stays reachable without a click.
// It is inert when the list is shorter than the cap, so a small org sees no
// scrollbar at all.
//
// 26rem is five TILEs plus their space-y-3 gaps: a tile is one text line and a
// bar inside p-4, about 4.6rem, and five of those with four 0.75rem gaps comes
// to just over 26rem — so the sixth is half-visible, which is what tells you
// the list continues.
export const SCROLL_LIST = "nice-scrollbar max-h-[26rem] overflow-y-auto pr-1";
