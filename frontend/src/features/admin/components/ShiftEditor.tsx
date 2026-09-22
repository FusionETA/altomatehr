import { useState } from "react";
import { createShift, setDefaultShift, updateShift, type Shift } from "@/features/shifts/api";
import type { FilterOption } from "./AttendanceFilterBar";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const WEEKDAYS = [
  { iso: 1, label: "Mon" },
  { iso: 2, label: "Tue" },
  { iso: 3, label: "Wed" },
  { iso: 4, label: "Thu" },
  { iso: 5, label: "Fri" },
  { iso: 6, label: "Sat" },
  { iso: 7, label: "Sun" },
];

const FIELD =
  "h-11 w-full rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";
const LABEL = "text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground";

// Create a shift on a project, or edit an existing one when `shift` is given.
//
// Project is chosen at create time and only then: the backend treats a shift's
// project as immutable afterwards, so the field is shown read-only while
// editing rather than as a control that would silently fail.
export function ShiftEditor({
  projects,
  defaultProjectId,
  shift,
  onClose,
  onCreated,
}: {
  projects: FilterOption[];
  defaultProjectId?: string;
  // Absent = create. Present = edit that shift.
  shift?: Shift;
  onClose: () => void;
  onCreated: () => void;
}) {
  const editing = shift !== undefined;
  const [projectId, setProjectId] = useState(
    shift?.projectId ?? defaultProjectId ?? projects[0]?.id ?? "",
  );
  const [name, setName] = useState(shift?.name ?? "");
  const [startTime, setStartTime] = useState(shift?.startTime ?? "09:00");
  const [endTime, setEndTime] = useState(shift?.endTime ?? "18:00");
  // A null `workingDays` means Mon-Fri (see the Shift type), so the fallback
  // here has to match that rather than being an empty set.
  const [days, setDays] = useState<Set<number>>(
    new Set(
      shift?.workingDays
        ? shift.workingDays.split(",").map(Number).filter((n) => n >= 1 && n <= 7)
        : [1, 2, 3, 4, 5],
    ),
  );
  const [lunchBreakMinutes, setLunchBreakMinutes] = useState(shift?.lunchBreakMinutes ?? 60);
  const [isDefault, setIsDefault] = useState(shift?.isDefault ?? false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggleDay = (iso: number) => {
    const next = new Set(days);
    if (next.has(iso)) next.delete(iso);
    else next.add(iso);
    setDays(next);
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!projectId) {
      setError("Pick a project for this shift.");
      return;
    }
    if (days.size === 0) {
      // A shift that runs on no days would make every day "not scheduled",
      // which quietly turns off late detection for everyone on it.
      setError("Choose at least one working day.");
      return;
    }

    setSaving(true);
    setError(null);
    const fields = {
      name: name.trim(),
      startTime,
      endTime,
      workingDays: [...days].sort((a, b) => a - b).join(","),
      lunchBreakMinutes,
    };
    try {
      // Either way the default is claimed by its own call, never by a flag on
      // the body: the server owns that rule (a project's first shift gets it)
      // and ignores any `isDefault` sent here, so without the second call the
      // checkbox would look like it worked and change nothing.
      //
      // Only ever set, never cleared — a project must always have exactly one
      // default, so unticking the box has nothing to promote in its place.
      // That is why the box is disabled once it is on.
      const saved = editing
        ? await updateShift(shift.id, fields)
        : await createShift({ projectId, ...fields });

      if (isDefault && !saved.isDefault) await setDefaultShift(saved.id);
      onCreated();
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : `Could not ${editing ? "save" : "create"} that shift.`,
      );
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm">
      <form
        onSubmit={submit}
        className="nice-scrollbar max-h-[90vh] w-full max-w-[520px] space-y-4 overflow-y-auto rounded-[26px] border border-white/40 bg-card p-6 shadow-[0_18px_48px_rgba(76,26,134,0.16)]"
      >
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            Attendance
          </p>
          <h3 className="mt-1 text-xl font-black text-foreground">
            {editing ? "Edit shift" : "Add shift"}
          </h3>
        </div>

        <div className="space-y-1.5">
          <span className={LABEL}>Project</span>
          {editing ? (
            // Read-only text, not a disabled <select>: the project cannot move,
            // and a greyed-out dropdown reads as "temporarily unavailable"
            // rather than "fixed for the life of this shift".
            <p className={`${FIELD} flex items-center text-muted-foreground`}>
              {projects.find((p) => p.id === projectId)?.name ?? "—"}
            </p>
          ) : (
            // With no projects there is nothing to pick, so the empty state is
            // the trigger's PLACEHOLDER rather than an option: Radix reserves
            // the empty string for "nothing selected", and an item carrying it
            // throws. Disabled too — an open, empty list explains nothing.
            <Select
              value={projectId}
              disabled={projects.length === 0}
              onValueChange={setProjectId}
            >
              <SelectTrigger id="shift-project" aria-label="Project" className="h-11 px-3">
                <SelectValue placeholder="No projects yet" />
              </SelectTrigger>
              <SelectContent>
                {projects.map((project) => (
                  <SelectItem key={project.id} value={project.id}>
                    {project.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </div>

        <div className="space-y-1.5">
          <label className={LABEL} htmlFor="shift-name">Name</label>
          <input
            id="shift-name"
            className={FIELD}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Day shift"
            required
          />
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <label className={LABEL} htmlFor="shift-start">Starts</label>
            <input id="shift-start" type="time" className={FIELD} value={startTime}
              onChange={(e) => setStartTime(e.target.value)} required />
          </div>
          <div className="space-y-1.5">
            <label className={LABEL} htmlFor="shift-end">Ends</label>
            {/* No "end after start" check: a night shift legitimately ends the
                next morning, and the backend reads it that way. */}
            <input id="shift-end" type="time" className={FIELD} value={endTime}
              onChange={(e) => setEndTime(e.target.value)} required />
          </div>
        </div>

        <div className="space-y-1.5">
          <span className={LABEL}>Working days</span>
          <div className="flex flex-wrap gap-2">
            {WEEKDAYS.map((day) => (
              <button
                key={day.iso}
                type="button"
                onClick={() => toggleDay(day.iso)}
                aria-pressed={days.has(day.iso)}
                className={`rounded-full border px-3 py-1.5 text-xs font-bold transition-colors ${
                  days.has(day.iso)
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                }`}
              >
                {day.label}
              </button>
            ))}
          </div>
        </div>

        <div className="space-y-1.5">
          <label className={LABEL} htmlFor="shift-break">Unpaid break (minutes)</label>
          <input
            id="shift-break"
            type="number"
            min={0}
            max={240}
            className={FIELD}
            value={lunchBreakMinutes}
            onChange={(e) => setLunchBreakMinutes(Number(e.target.value))}
          />
        </div>

        <label className="flex items-start gap-2 text-xs text-muted-foreground">
          <input
            type="checkbox"
            checked={isDefault}
            // Already the default: there is nothing to promote in its place, so
            // the only way off is to promote a different shift.
            disabled={shift?.isDefault === true}
            onChange={(e) => setIsDefault(e.target.checked)}
            className="mt-0.5 disabled:opacity-60"
          />
          <span>
            <span className="font-semibold text-foreground">Default for this project.</span>{" "}
            Anyone on the project without their own shift is measured against this
            one. Setting it clears whichever shift held it before. A project's
            first shift becomes the default whether or not this is ticked.
          </span>
        </label>

        {error ? (
          <p className="rounded-2xl border border-destructive/20 bg-destructive/5 p-3 text-sm font-medium text-destructive">
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="rounded-2xl border border-border/70 bg-card px-4 py-2 text-sm font-bold text-foreground hover:bg-muted"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="rounded-2xl bg-primary px-4 py-2 text-sm font-bold text-primary-foreground disabled:opacity-60"
          >
            {saving ? (editing ? "Saving…" : "Adding…") : editing ? "Save changes" : "Add shift"}
          </button>
        </div>
      </form>
    </div>
  );
}
