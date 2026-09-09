import { useState } from "react";
import { createShift, setDefaultShift } from "@/features/shifts/api";
import type { FilterOption } from "./AttendanceFilterBar";

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

// Create a shift on a project.
//
// Project is chosen here and only here: the backend treats a shift's project as
// immutable after creation, so offering it as an editable field later would be
// a control that silently fails.
export function ShiftEditor({
  projects,
  defaultProjectId,
  onClose,
  onCreated,
}: {
  projects: FilterOption[];
  defaultProjectId?: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [projectId, setProjectId] = useState(defaultProjectId ?? projects[0]?.id ?? "");
  const [name, setName] = useState("");
  const [startTime, setStartTime] = useState("09:00");
  const [endTime, setEndTime] = useState("18:00");
  const [days, setDays] = useState<Set<number>>(new Set([1, 2, 3, 4, 5]));
  const [lunchBreakMinutes, setLunchBreakMinutes] = useState(60);
  const [isDefault, setIsDefault] = useState(false);
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
    try {
      const shift = await createShift({
        projectId,
        name: name.trim(),
        startTime,
        endTime,
        workingDays: [...days].sort((a, b) => a - b).join(","),
        lunchBreakMinutes,
      });

      // The server decides the default on create — the first shift for a
      // project gets it — and ignores any flag sent with the body. Claiming it
      // for a later shift is a second, deliberate call. Without this the
      // checkbox would look like it worked and change nothing.
      if (isDefault && !shift.isDefault) await setDefaultShift(shift.id);
      onCreated();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not create that shift.");
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
          <h3 className="mt-1 text-xl font-black text-foreground">Add shift</h3>
        </div>

        <div className="space-y-1.5">
          <label className={LABEL} htmlFor="shift-project">Project</label>
          <select
            id="shift-project"
            className={FIELD}
            value={projectId}
            onChange={(e) => setProjectId(e.target.value)}
          >
            {projects.length === 0 ? <option value="">No projects yet</option> : null}
            {projects.map((project) => (
              <option key={project.id} value={project.id}>{project.name}</option>
            ))}
          </select>
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
            onChange={(e) => setIsDefault(e.target.checked)}
            className="mt-0.5"
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
            {saving ? "Adding…" : "Add shift"}
          </button>
        </div>
      </form>
    </div>
  );
}
