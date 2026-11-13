import { useCallback, useEffect, useRef, useState } from "react";
import { ChevronRight, LoaderCircle } from "lucide-react";
import { Switch } from "@/shared/components/ui/switch";
import { SkeletonPanel } from "@/shared/components/Skeleton";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useMyAccess } from "@/features/settings/lib/module-access";
import { SettingsReadOnlyNote } from "@/features/settings/components/SettingsReadOnlyNote";
import {
  getNotificationSettings,
  updateNotificationSetting,
  type NotificationAdmin,
  type NotificationSetting,
} from "../api";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 px-5 py-4 shadow-ambient backdrop-blur-sm sm:px-6 sm:py-5";
const BOX = "min-w-0 rounded-[22px] border border-border/70 p-4 sm:p-5";
const SELECT =
  "h-10 rounded-2xl border border-border bg-card px-3 text-sm font-semibold text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50";

// Settings → Notifications: who hears about what. One row per notification
// the company can configure. Closed, a row shows its settings in one line and
// its on/off switch; open, it shows the full form. One row open at a time.
export function NotificationsSettings() {
  const { canChangeSettings } = useMyAccess();
  const query = useCachedQuery("/notification-settings", getNotificationSettings);
  const [openKind, setOpenKind] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();
  // Which rows have unsaved edits — asked before closing one.
  const dirtyKinds = useRef(new Set<string>());

  const onDirtyChange = useCallback((kind: string, dirty: boolean) => {
    if (dirty) dirtyKinds.current.add(kind);
    else dirtyKinds.current.delete(kind);
  }, []);

  async function toggleOpen(kind: string) {
    if (openKind && dirtyKinds.current.has(openKind)) {
      const title = query.data?.notifications.find((n) => n.kind === openKind)?.title;
      const ok = await confirm({
        title: "Discard your changes?",
        message: `Your changes to ${title ?? "this notification"} aren't saved yet.`,
        confirmLabel: "Discard",
        cancelLabel: "Keep editing",
        destructive: true,
      });
      if (!ok) return;
    }
    setOpenKind((current) => (current === kind ? null : kind));
  }

  if (query.loading && !query.data) return <SkeletonPanel />;
  if (!query.data) {
    return (
      <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        {query.error ?? "Could not load notification settings."}
      </div>
    );
  }

  const { admins, notifications } = query.data;
  return (
    <div className="min-w-0 space-y-4">
      {!canChangeSettings ? <SettingsReadOnlyNote /> : null}
      <p className="text-sm text-muted-foreground">
        Choose who hears about what. Open a notification to change its settings; new ones are added to
        this list as they arrive.
      </p>
      {notifications.map((n) => (
        <NotificationRow
          key={n.kind}
          setting={n}
          admins={admins}
          readOnly={!canChangeSettings}
          open={openKind === n.kind}
          onToggleOpen={() => void toggleOpen(n.kind)}
          onDirtyChange={onDirtyChange}
        />
      ))}
      <div className="rounded-[28px] border border-dashed border-border px-5 py-4 text-sm text-muted-foreground sm:px-6">
        Other notifications will be listed here, each with its own admins and staff switch.
      </div>
      {confirmDialog}
    </div>
  );
}

// The closed row's one-line summary, from the saved settings.
function summarize(s: NotificationSetting, admins: NotificationAdmin[]): string {
  if (!s.enabled) return "Off · nobody is reminded";
  const when =
    s.secondReminderDays !== null
      ? `${s.firstReminderDays} and ${s.secondReminderDays} days before`
      : `${s.firstReminderDays} days before`;
  const names = s.adminUserIds
    .map((id) => admins.find((a) => a.userId === id))
    .filter((a): a is NotificationAdmin => !!a)
    .map((a) => a.name || a.email);
  const who =
    names.length === 0
      ? "Owner (nobody ticked)"
      : names.length <= 2
        ? names.join(" and ")
        : `${names[0]} + ${names.length - 1} more`;
  return `${when} · ${who} · staff email ${s.emailStaff ? "on" : "off"}`;
}

function NotificationRow({
  setting,
  admins,
  readOnly,
  open,
  onToggleOpen,
  onDirtyChange,
}: {
  setting: NotificationSetting;
  admins: NotificationAdmin[];
  readOnly: boolean;
  open: boolean;
  onToggleOpen: () => void;
  onDirtyChange: (kind: string, dirty: boolean) => void;
}) {
  const [saved, setSaved] = useState(setting);
  const [picked, setPicked] = useState<string[]>(setting.adminUserIds);
  const [emailStaff, setEmailStaff] = useState(setting.emailStaff);
  const [firstDays, setFirstDays] = useState(setting.firstReminderDays);
  const [secondDays, setSecondDays] = useState<number | null>(setting.secondReminderDays);
  const [saving, setSaving] = useState(false);
  const [switching, setSwitching] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [justSaved, setJustSaved] = useState(false);

  // On/off is the row's switch and saves straight away; the rest is the form
  // inside, saved with its Save button.
  const enabled = saved.enabled;
  const dirty =
    emailStaff !== saved.emailStaff ||
    firstDays !== saved.firstReminderDays ||
    secondDays !== saved.secondReminderDays ||
    [...picked].sort().join() !== [...saved.adminUserIds].sort().join();

  useEffect(() => onDirtyChange(setting.kind, dirty), [onDirtyChange, setting.kind, dirty]);

  // The page is first drawn from the cache, then refreshed. When the fresh
  // copy differs (someone else saved since), take it — unless this admin has
  // started editing, in which case their edits stay and only the starting
  // point moves. Otherwise a cached value would sit on screen as an unsaved
  // edit, and Save would quietly undo the other admin's change.
  const dirtyRef = useRef(dirty);
  dirtyRef.current = dirty;
  useEffect(() => {
    setSaved(setting);
    if (dirtyRef.current) return;
    setPicked(setting.adminUserIds);
    setEmailStaff(setting.emailStaff);
    setFirstDays(setting.firstReminderDays);
    setSecondDays(setting.secondReminderDays);
  }, [setting]);

  function reset() {
    setPicked(saved.adminUserIds);
    setEmailStaff(saved.emailStaff);
    setFirstDays(saved.firstReminderDays);
    setSecondDays(saved.secondReminderDays);
    setError(null);
  }

  // Closing the row (the parent asked first if there were edits) drops them.
  const resetRef = useRef(reset);
  resetRef.current = reset;
  useEffect(() => {
    if (!open) {
      resetRef.current();
      setJustSaved(false);
    }
  }, [open]);

  function toggle(userId: string, on: boolean) {
    setJustSaved(false);
    setPicked((current) => (on ? [...current, userId] : current.filter((id) => id !== userId)));
  }

  // The row's switch: saves on/off alone, with the other settings as saved.
  // Unsaved edits in the open form stay as they are.
  async function switchOnOff(on: boolean) {
    const before = saved;
    setSaved({ ...saved, enabled: on });
    setSwitching(true);
    setError(null);
    try {
      const updated = await updateNotificationSetting(setting.kind, {
        enabled: on,
        adminUserIds: before.adminUserIds,
        emailStaff: before.emailStaff,
        firstReminderDays: before.firstReminderDays,
        secondReminderDays: before.secondReminderDays,
      });
      setSaved(updated);
    } catch (err) {
      setSaved(before);
      setError(err instanceof Error ? err.message : "Could not switch it.");
    } finally {
      setSwitching(false);
    }
  }

  async function save() {
    setSaving(true);
    setError(null);
    try {
      const updated = await updateNotificationSetting(setting.kind, {
        enabled,
        adminUserIds: picked,
        emailStaff,
        firstReminderDays: firstDays,
        secondReminderDays: secondDays,
      });
      setSaved(updated);
      setPicked(updated.adminUserIds);
      setJustSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save.");
    } finally {
      setSaving(false);
    }
  }

  const panelId = `notification-${setting.kind}`;
  const busy = saving || switching;
  return (
    <section className={CARD}>
      <div className="flex items-center gap-3 sm:gap-4">
        <h2 className="min-w-0 flex-1">
          <button
            type="button"
            onClick={onToggleOpen}
            aria-expanded={open}
            aria-controls={panelId}
            className="-m-2 flex w-[calc(100%+1rem)] min-w-0 items-center gap-3 rounded-2xl p-2 text-left hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <ChevronRight
              aria-hidden
              className={`h-5 w-5 shrink-0 text-muted-foreground transition-transform ${open ? "rotate-90" : ""}`}
            />
            <span className="min-w-0">
              <span className="block text-base font-bold text-foreground sm:text-lg">{setting.title}</span>
              <span
                className={`mt-0.5 block text-xs sm:truncate sm:text-sm ${enabled ? "text-muted-foreground" : "text-muted-foreground/80"}`}
              >
                {summarize(saved, admins)}
              </span>
            </span>
          </button>
        </h2>
        <label className="flex shrink-0 items-center gap-3 text-sm font-semibold text-foreground">
          {switching ? <LoaderCircle className="h-4 w-4 animate-spin text-muted-foreground" /> : null}
          <span className="hidden sm:inline">{enabled ? "On" : "Off"}</span>
          <Switch
            checked={enabled}
            disabled={readOnly || busy}
            onCheckedChange={(v) => void switchOnOff(v)}
            aria-label={`${setting.title} on or off`}
          />
        </label>
      </div>

      {error && !open ? (
        <p className="mt-2 text-xs font-medium text-destructive" aria-live="polite">
          {error}
        </p>
      ) : null}

      {open ? (
        <fieldset id={panelId} disabled={readOnly || busy} className="mt-5 min-w-0 space-y-5">
          <p className="text-sm text-muted-foreground">{setting.description}</p>

          <fieldset
            className={`${BOX} ${enabled ? "" : "opacity-60"}`}
            disabled={!enabled || readOnly || busy}
          >
            <legend className="px-1.5 text-sm font-bold text-foreground">When</legend>
            <div className="flex flex-wrap items-center gap-x-2 gap-y-3 text-sm text-foreground">
              <label className="flex items-center gap-2">
                Remind
                <select
                  value={firstDays}
                  onChange={(e) => {
                    const days = Number(e.target.value);
                    setFirstDays(days);
                    // The second has to stay closer to the date than the first.
                    if (secondDays !== null && secondDays >= days) setSecondDays(null);
                    setJustSaved(false);
                  }}
                  className={SELECT}
                >
                  {setting.reminderDayOptions.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
                days before,
              </label>
              <label className="flex items-center gap-2">
                and again
                <select
                  value={secondDays ?? ""}
                  onChange={(e) => {
                    setSecondDays(e.target.value === "" ? null : Number(e.target.value));
                    setJustSaved(false);
                  }}
                  className={SELECT}
                >
                  <option value="">—</option>
                  {setting.reminderDayOptions
                    .filter((d) => d < firstDays)
                    .map((d) => (
                      <option key={d} value={d}>
                        {d}
                      </option>
                    ))}
                </select>
                days before,
              </label>
              <span>and on the expiry day.</span>
            </div>
            <p className="mt-2 text-xs text-muted-foreground">
              The same for everyone who gets it. A date entered when it's already inside a window gets
              one reminder straight away.
            </p>
          </fieldset>

          <div className={`grid gap-4 lg:grid-cols-[3fr_2fr] ${enabled ? "" : "opacity-60"}`}>
            <fieldset className={BOX} disabled={!enabled || readOnly || busy}>
              <legend className="px-1.5 text-sm font-bold text-foreground">Admins who get it</legend>
              <p className="mb-2 text-xs text-muted-foreground">
                Email (one a day), in the app and as a phone push.
              </p>
              <ul className="divide-y divide-border/60">
                {admins.map((a) => (
                  <li key={a.userId}>
                    <label className="flex cursor-pointer items-center gap-3 py-2.5">
                      <input
                        type="checkbox"
                        checked={picked.includes(a.userId)}
                        onChange={(e) => toggle(a.userId, e.target.checked)}
                        className="h-5 w-5 shrink-0 rounded border-border accent-primary"
                      />
                      <span className="min-w-0">
                        <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-foreground">
                          {a.name || a.email}
                          {a.role === "Owner" ? (
                            <span className="rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-bold text-primary">
                              OWNER
                            </span>
                          ) : null}
                        </span>
                        <span className={`block truncate text-xs ${a.isLimited ? "text-tertiary" : "text-muted-foreground"}`}>
                          {a.email}
                          {a.isLimited ? " · only the staff their access covers" : " · all staff"}
                        </span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
              <p className="mt-3 rounded-2xl bg-muted/50 px-3.5 py-3 text-xs leading-relaxed text-foreground">
                {picked.length === 0
                  ? `Nobody is ticked, so ${admins.find((a) => a.role === "Owner")?.name || "the owner"} (the owner) gets it.`
                  : "If nobody is ticked, the owner gets it, so a reminder is never lost."}{" "}
                An admin whose access is limited only hears about the staff it covers; anyone no
                ticked admin covers goes to the owner.
              </p>
            </fieldset>

            <fieldset className={`${BOX} self-start space-y-3`} disabled={!enabled || readOnly || busy}>
              <legend className="px-1.5 text-sm font-bold text-foreground">The staff member</legend>
              <label className="flex items-center gap-3">
                <span className="min-w-0 flex-1">
                  <span className="block text-sm font-semibold text-foreground">Also email them</span>
                  <span className="block text-xs text-muted-foreground">Sent to the email they sign in with.</span>
                </span>
                <Switch
                  checked={emailStaff}
                  onCheckedChange={(v) => {
                    setEmailStaff(v);
                    setJustSaved(false);
                  }}
                  aria-label="Also email the staff member"
                />
              </label>
              <p className="rounded-2xl bg-muted/50 px-3.5 py-3 text-xs leading-relaxed text-foreground">
                They always get it in the app and as a phone push. This switch only adds the email.
              </p>
            </fieldset>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <span className="mr-auto text-xs text-muted-foreground" aria-live="polite">
              {error ? (
                <span className="font-medium text-destructive">{error}</span>
              ) : justSaved ? (
                "Saved."
              ) : saved.updatedAt ? (
                `Changed${saved.updatedByName ? ` by ${saved.updatedByName}` : ""} · ${new Date(saved.updatedAt).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" })}`
              ) : (
                "Using the defaults."
              )}
            </span>
            {!readOnly ? (
              <>
                <button
                  type="button"
                  onClick={reset}
                  disabled={!dirty || busy}
                  className="h-11 rounded-full border border-border bg-card px-5 text-sm font-bold text-foreground disabled:opacity-50"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={save}
                  disabled={!dirty || busy}
                  className="inline-flex h-11 items-center gap-2 rounded-full bg-primary px-6 text-sm font-bold text-primary-foreground disabled:opacity-50"
                >
                  {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                  Save
                </button>
              </>
            ) : null}
          </div>
        </fieldset>
      ) : null}
    </section>
  );
}
