import { type FormEvent, useState } from "react";
import { LoaderCircle, X } from "lucide-react";
import { createPolicy, updatePolicy, type Policy, type SavePolicy } from "@/features/policies/api";
import type { LeaveType } from "@/features/leave/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";
const SECTION = "text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";

const empty: SavePolicy = {
  name: "",
  description: "",
  canAccessAttendance: true,
  canAccessClaims: true,
  canAccessLeave: true,
  requireGeofence: true,
  requireSelfie: false,
  requireClockOutSelfie: false,
  salaryType: "MONTHLY",
  otEnabled: true,
  otDailyThresholdMinutes: 480,
  otMethod: "CASH",
  temporary: false,
  leaveEntitlements: [],
};

function Check({
  label,
  checked,
  onChange,
  hint,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  hint?: string;
}) {
  return (
    <label className="flex items-start gap-2 text-sm font-medium text-foreground">
      <input
        type="checkbox"
        className="mt-0.5 h-4 w-4 rounded border-border accent-primary"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span>
        {label}
        {hint ? <span className="block text-xs font-normal text-muted-foreground">{hint}</span> : null}
      </span>
    </label>
  );
}

export function PolicyEditorModal({
  policy,
  leaveTypes,
  onClose,
  onSaved,
}: {
  policy: Policy | null;
  leaveTypes: LeaveType[];
  onClose: () => void;
  onSaved: (p: Policy) => void;
}) {
  const [form, setForm] = useState<SavePolicy>(() =>
    policy ? { ...policy, description: policy.description ?? "" } : { ...empty },
  );
  // leaveTypeId -> override input value ("" = inherit the type's default).
  const [ents, setEnts] = useState<Record<string, string>>(() =>
    Object.fromEntries((policy?.leaveEntitlements ?? []).map((e) => [e.leaveTypeId, String(e.defaultDays)])),
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const set = <K extends keyof SavePolicy>(key: K, value: SavePolicy[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!form.name.trim()) return;
    setSaving(true);
    setError(null);

    const leaveEntitlements = Object.entries(ents)
      .filter(([, v]) => v.trim() !== "")
      .map(([leaveTypeId, v]) => ({ leaveTypeId, defaultDays: Number(v) }));

    const body: SavePolicy = { ...form, name: form.name.trim(), leaveEntitlements };
    try {
      const saved = policy ? await updatePolicy(policy.id, body) : await createPolicy(body);
      onSaved(saved);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the policy.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-[620px] overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl">
        <form onSubmit={handleSubmit} className="nice-scrollbar max-h-[90vh] overflow-y-auto p-6 sm:p-8">
          <div className="flex items-start justify-between gap-4 border-b border-border/60 pb-4">
            <h2 className="text-2xl font-black text-foreground">{policy ? "Edit policy" : "New policy"}</h2>
            <button
              type="button"
              aria-label="Close"
              onClick={onClose}
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          <div className="mt-5 space-y-5">
            <div className="grid gap-4 sm:grid-cols-2">
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Name</span>
                <input
                  required
                  className={INPUT}
                  value={form.name}
                  onChange={(e) => set("name", e.target.value)}
                  placeholder="Full-time"
                />
              </label>
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">
                  Description <span className="font-normal text-muted-foreground">(optional)</span>
                </span>
                <input
                  className={INPUT}
                  value={form.description ?? ""}
                  onChange={(e) => set("description", e.target.value)}
                />
              </label>
            </div>

            <div className="space-y-2">
              <p className={SECTION}>Module access</p>
              <div className="grid gap-2 sm:grid-cols-3">
                <Check label="Attendance" checked={form.canAccessAttendance} onChange={(v) => set("canAccessAttendance", v)} />
                <Check label="Claims" checked={form.canAccessClaims} onChange={(v) => set("canAccessClaims", v)} />
                <Check label="Leave" checked={form.canAccessLeave} onChange={(v) => set("canAccessLeave", v)} />
              </div>
            </div>

            <div className="space-y-2">
              <p className={SECTION}>Attendance</p>
              <div className="grid gap-2.5 sm:grid-cols-2">
                <Check
                  label="Require geofence"
                  hint="Must be inside the project geofence to clock in"
                  checked={form.requireGeofence}
                  onChange={(v) => set("requireGeofence", v)}
                />
                <Check label="Require selfie (clock-in)" checked={form.requireSelfie} onChange={(v) => set("requireSelfie", v)} />
                <Check
                  label="Require selfie (clock-out)"
                  checked={form.requireClockOutSelfie}
                  onChange={(v) => set("requireClockOutSelfie", v)}
                />
              </div>
            </div>

            <div className="space-y-2">
              <p className={SECTION}>Pay &amp; overtime</p>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <span className="text-sm font-semibold text-foreground">Salary type</span>
                  <Select value={form.salaryType} onValueChange={(v) => set("salaryType", v as SavePolicy["salaryType"])}>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="MONTHLY">Monthly</SelectItem>
                      <SelectItem value="HOURLY">Hourly</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                {form.otEnabled ? (
                  <label className="block space-y-2">
                    <span className="text-sm font-semibold text-foreground">OT threshold (min/day)</span>
                    <input
                      type="number"
                      min="0"
                      max="1440"
                      className={INPUT}
                      value={form.otDailyThresholdMinutes}
                      onChange={(e) => set("otDailyThresholdMinutes", Number(e.target.value))}
                    />
                  </label>
                ) : null}
              </div>
              <div className="grid gap-2.5 pt-1 sm:grid-cols-2">
                <Check label="Overtime enabled" checked={form.otEnabled} onChange={(v) => set("otEnabled", v)} />
                <Check label="Temporary (probation / fixed-term)" checked={form.temporary} onChange={(v) => set("temporary", v)} />
              </div>
            </div>

            <div className="space-y-2">
              <p className={SECTION}>Leave entitlements</p>
              <p className="text-xs text-muted-foreground">
                Days per year for this policy. Leave blank to inherit the leave type's default.
              </p>
              <div className="space-y-2">
                {leaveTypes.map((t) => (
                  <div key={t.id} className="flex items-center justify-between gap-3">
                    <span className="text-sm font-medium text-foreground">
                      {t.name} <span className="text-xs text-muted-foreground">(default {t.defaultDays})</span>
                    </span>
                    <input
                      type="number"
                      min="0"
                      step="0.5"
                      placeholder={String(t.defaultDays)}
                      value={ents[t.id] ?? ""}
                      onChange={(e) => setEnts((m) => ({ ...m, [t.id]: e.target.value }))}
                      className="h-10 w-28 rounded-2xl border border-border bg-white/80 px-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                    />
                  </div>
                ))}
                {leaveTypes.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No leave types yet — add them under the Leave tab.</p>
                ) : null}
              </div>
            </div>
          </div>

          {error ? (
            <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              {error}
            </p>
          ) : null}

          <div className="mt-6 flex justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={saving || !form.name.trim()}
              className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              {policy ? "Save changes" : "Create policy"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
