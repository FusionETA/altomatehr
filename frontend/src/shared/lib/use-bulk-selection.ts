import { useEffect, useRef, useState } from "react";

// The per-id report every bulk endpoint returns. A run where eighteen of twenty
// landed is not a failed request, so the response is a report rather than a
// single verdict — and the UI has to be able to say which two didn't.
export type BulkResultItem = { id: string; ok: boolean; error?: string | null };

export type BulkResult = {
  succeeded: number;
  failed: number;
  items: BulkResultItem[];
};

export type BulkSelection<T> = {
  // Selection mode, at every width. Off by default: outside it neither the
  // cards nor the desktop table carry a checkbox, so a tap or click opens a row
  // as it always did. Desktop used to show a permanent checkbox column instead
  // — with most rows already decided that was a column of greyed-out boxes
  // reading as broken, and nothing named what it was for.
  mode: boolean;
  enter: () => void;
  exit: () => void;

  // Every visible row that may be approved in a batch, and the subset ticked.
  selectable: T[];
  selected: T[];
  has: (id: string) => boolean;
  canSelect: (row: T) => boolean;

  toggle: (id: string) => void;
  // Set a whole batch of ids on or off in one write. Not `ids.forEach(toggle)`:
  // toggling a half-ticked batch flips each one, so a "select this day" control
  // over a day with one of two events already picked would deselect that one.
  setMany: (ids: string[], selected: boolean) => void;
  toggleAll: () => void;
  clear: () => void;
  allSelected: boolean;

  // For the select-all box. `indeterminate` is a DOM property with no HTML
  // attribute, so it cannot be set from JSX and has to be written to the node.
  selectAllRef: React.RefObject<HTMLInputElement | null>;
};

// One selection model behind every approval queue — claims, leave, overtime and
// attendance. It was written for claims first and copied nowhere: four copies of
// "prune the selection when the filter changes" is four chances to forget it in
// one of them, and forgetting it lets an approver submit rows they can no longer
// see.
//
// `rows` is the FILTERED list, not the current page. Selection deliberately
// survives paging — an approver ticking rows across three pages then hitting
// Approve is the whole point — but never survives a row leaving the filter.
export function useBulkSelection<T>(
  rows: T[],
  idOf: (row: T) => string,
  canSelect: (row: T) => boolean,
): BulkSelection<T> {
  const [mode, setMode] = useState(false);
  const [ids, setIds] = useState<Set<string>>(new Set());

  const selectable = rows.filter(canSelect);
  const selected = selectable.filter((row) => ids.has(idOf(row)));

  // Keyed on the ids themselves rather than the array identity: a re-render that
  // rebuilds the same list must not re-run the prune, and a genuine change to
  // WHICH rows are selectable must. The key is only ever compared, never parsed
  // back apart — the effect reads the live ids off a ref, so no id has to avoid
  // whatever character a separator would have claimed.
  const selectableIds = selectable.map(idOf);
  const selectableKey = JSON.stringify(selectableIds);
  const selectableIdsRef = useRef(selectableIds);
  selectableIdsRef.current = selectableIds;

  // Dropping a row out of the filtered view drops it out of the selection too.
  // Returning the same Set when nothing changed lets React bail out instead of
  // re-rendering on every pass.
  useEffect(() => {
    const visible = new Set(selectableIdsRef.current);
    setIds((current) => {
      const next = new Set([...current].filter((id) => visible.has(id)));
      return next.size === current.size ? current : next;
    });
  }, [selectableKey]);

  // Nothing left to select (a filter change emptied the queue) → the mode has no
  // meaning any more, so it should not linger with a Done button.
  const empty = selectable.length === 0;
  useEffect(() => {
    if (empty) setMode(false);
  }, [empty]);

  const allSelected = selectable.length > 0 && selected.length === selectable.length;

  // Some-but-not-all reads as a dash rather than an empty box, so "select all"
  // does not look untouched when half the queue is already ticked.
  const someSelected = selected.length > 0 && !allSelected;
  const selectAllRef = useRef<HTMLInputElement | null>(null);
  useEffect(() => {
    if (selectAllRef.current) selectAllRef.current.indeterminate = someSelected;
  }, [someSelected]);

  return {
    mode,
    enter: () => setMode(true),
    exit: () => {
      setMode(false);
      setIds(new Set());
    },
    selectable,
    selected,
    has: (id) => ids.has(id),
    canSelect,
    toggle: (id) =>
      setIds((current) => {
        const next = new Set(current);
        if (next.has(id)) next.delete(id);
        else next.add(id);
        return next;
      }),
    setMany: (many, on) =>
      setIds((current) => {
        const next = new Set(current);
        for (const id of many) {
          if (on) next.add(id);
          else next.delete(id);
        }
        return next.size === current.size ? current : next;
      }),
    toggleAll: () => setIds(allSelected ? new Set() : new Set(selectableIds)),
    clear: () => setIds(new Set()),
    allSelected,
    selectAllRef,
  };
}
