import { useEffect, useRef, useState } from "react";
import { LoaderCircle } from "lucide-react";
import { Switch } from "@/shared/components/ui/switch";
import { SkeletonPanel } from "@/shared/components/Skeleton";
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
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const BOX = "min-w-0 rounded-[22px] border border-border/70 p-4 sm:p-5";

// Settings → Notifications: who hears about what. One card per notification
// the company can configure; each has its own admins and staff switch.
export function NotificationsSettings() {
  const { canChangeSettings } = useMyAccess();
  const query = useCachedQuery("/notification-settings", getNotificationSettings);

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
    <div className="min-w-0 space-y-5">
      {!canChangeSettings ? <SettingsReadOnlyNote /> : null}
      <p className="text-sm text-muted-foreground">
        Choose who hears about what. Each notification has its own settings, and new ones are added to
        this list as they arrive.
      </p>
      {notifications.map((n) => (
        <NotificationCard key={n.kind} setting={n} admins={admins} readOnly={!canChangeSettings} />
      ))}
      <div className="rounded-[28px] border border-dashed border-border px-5 py-5 text-sm text-muted-foreground sm:px-6">
        Other notifications will be listed here, each with its own admins and staff switch.
      </div>
    </div>
  );
}

function NotificationCard({
  setting,
  admins,
  readOnly,
}: {
  setting: NotificationSetting;
  admins: NotificationAdmin[];
  readOnly: boolean;
}) {
  const [saved, setSaved] = useState(setting);
  const [enabled, setEnabled] = useState(setting.enabled);
  const [picked, setPicked] = useState<string[]>(setting.adminUserIds);
  const [emailStaff, setEmailStaff] = useState(setting.emailStaff);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [justSaved, setJustSaved] = useState(false);

  const dirty =
    enabled !== saved.enabled ||
    emailStaff !== saved.emailStaff ||
    [...picked].sort().join() !== [...saved.adminUserIds].sort().join();

  // The page is first drawn from the cache, then refreshed. When the fresh
  // copy differs (someone else saved since), take it — unless this admin has
  // started editing, in which case their edits stay and only the starting
  // point moves. Otherwise a cached "On" would sit on screen as an unsaved
  // edit, and Save would quietly undo the other admin's change.
  const dirtyRef = useRef(dirty);
  dirtyRef.current = dirty;
  useEffect(() => {
    setSaved(setting);
    if (dirtyRef.current) return;
    setEnabled(setting.enabled);
    setPicked(setting.adminUserIds);
    setEmailStaff(setting.emailStaff);
  }, [setting]);

  const owner = admins.find((a) => a.role === "Owner");

  function toggle(userId: string, on: boolean) {
    setJustSaved(false);
    setPicked((current) => (on ? [...current, userId] : current.filter((id) => id !== userId)));
  }

  function reset() {
    setEnabled(saved.enabled);
    setPicked(saved.adminUserIds);
    setEmailStaff(saved.emailStaff);
    setError(null);
  }

  async function save() {
    setSaving(true);
    setError(null);
    try {
      const updated = await updateNotificationSetting(setting.kind, {
        enabled,
        adminUserIds: picked,
        emailStaff,
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

  const titleId = `notification-${setting.kind}`;
  return (
    <section className={CARD} aria-labelledby={titleId}>
      <fieldset disabled={readOnly || saving} className="min-w-0 space-y-5">
        <div className="flex flex-wrap items-start gap-4">
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-lg font-bold text-foreground">
              {setting.title}
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">{setting.description}</p>
          </div>
          <label className="flex items-center gap-3 text-sm font-semibold text-foreground">
            {enabled ? "On" : "Off"}
            <Switch
              checked={enabled}
              onCheckedChange={(v) => {
                setEnabled(v);
                setJustSaved(false);
              }}
              aria-label={`${setting.title} on or off`}
            />
          </label>
        </div>

        <div className={`grid gap-4 lg:grid-cols-[3fr_2fr] ${enabled ? "" : "opacity-60"}`}>
          <fieldset className={BOX} disabled={!enabled || readOnly || saving}>
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
                ? `Nobody is ticked, so ${owner?.name || "the owner"} (the owner) gets it.`
                : "If nobody is ticked, the owner gets it, so a reminder is never lost."}{" "}
              An admin whose access is limited only hears about the staff it covers; anyone no
              ticked admin covers goes to the owner.
            </p>
          </fieldset>

          <fieldset className={`${BOX} self-start space-y-3`} disabled={!enabled || readOnly || saving}>
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
                disabled={!dirty || saving}
                className="h-11 rounded-full border border-border bg-card px-5 text-sm font-bold text-foreground disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={save}
                disabled={!dirty || saving}
                className="inline-flex h-11 items-center gap-2 rounded-full bg-primary px-6 text-sm font-bold text-primary-foreground disabled:opacity-50"
              >
                {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Save
              </button>
            </>
          ) : null}
        </div>
      </fieldset>
    </section>
  );
}
