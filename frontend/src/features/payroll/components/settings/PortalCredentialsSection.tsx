import { useCallback, useEffect, useState } from "react";
import { Eye, EyeOff, LoaderCircle, Trash2 } from "lucide-react";
import {
  deletePortalCredential,
  getPortalCredentials,
  revealPortalCredential,
  savePortalCredential,
  type PortalCredential,
} from "../../api";
import {
  BUTTON,
  BUTTON_DANGER,
  BUTTON_GHOST,
  CARD,
  ERROR_PANEL,
  HINT,
  INPUT,
  LABEL,
  NOTE_PANEL,
  TEXTAREA,
  WARN_PANEL,
} from "../../lib/ui";

// The logins for the three statutory portals.
//
// Optional: an org whose accountant files for them never needs these. When
// payroll is filed in-house, the person doing it should not be hunting for a
// password in a spreadsheet on the day it is due.
//
// Passwords are encrypted at rest and NEVER come back with the list — only an
// explicit reveal returns one, so the page cannot be shoulder-surfed for all
// three at once.
export function PortalCredentialsSection({
  // Lets the section pill's "N saved" subtitle stay in step with what is
  // actually stored, without lifting the editing itself out of here.
  onChanged,
}: {
  onChanged?: () => void;
}) {
  const [rows, setRows] = useState<PortalCredential[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    return getPortalCredentials()
      .then((next) => {
        setRows(next);
        onChanged?.();
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [onChanged]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) return <p className={NOTE_PANEL}>Loading…</p>;

  return (
    <div className="space-y-5">
      {error ? <p className={ERROR_PANEL}>Error: {error}</p> : null}

      <div className={WARN_PANEL}>
        These are real portal logins. They are encrypted before being stored, and a
        password is only returned when someone asks to see it — but anyone who can open
        this page can ask, so keep admin access tight.
      </div>

      {rows.map((row) => (
        <PortalCard key={row.portal} credential={row} onChanged={() => void load()} />
      ))}
    </div>
  );
}

function PortalCard({
  credential,
  onChanged,
}: {
  credential: PortalCredential;
  onChanged: () => void;
}) {
  const [loginId, setLoginId] = useState(credential.loginId ?? "");
  // Empty means "leave the stored one alone", which is why it is never
  // prefilled — not even with a mask.
  const [password, setPassword] = useState("");
  const [image, setImage] = useState(credential.image ?? "");
  const [secretCode, setSecretCode] = useState(credential.secretCode ?? "");
  const [securityPhrase, setSecurityPhrase] = useState(credential.securityPhrase ?? "");
  const [reminder, setReminder] = useState(credential.passwordReminder ?? "");
  const [notes, setNotes] = useState(credential.notes ?? "");

  const [revealed, setRevealed] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function act<T>(key: string, action: () => Promise<T>): Promise<T | null> {
    setBusy(key);
    setError(null);
    setSaved(false);
    try {
      return await action();
    } catch (err) {
      setError(err instanceof Error ? err.message : "That did not go through.");
      return null;
    } finally {
      setBusy(null);
    }
  }

  const field = (suffix: string) => `${credential.portal}-${suffix}`;

  return (
    <section className={CARD}>
      <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-[15px] font-semibold text-foreground">{credential.portalLabel}</h3>
          <p className={HINT}>
            {credential.isConfigured
              ? credential.hasPassword
                ? "Login and password saved."
                : "Login saved, no password."
              : "Nothing saved yet."}
          </p>
        </div>

        {credential.isConfigured ? (
          <button
            type="button"
            className={`${BUTTON_DANGER} h-9 rounded-xl px-3 text-xs`}
            disabled={busy !== null}
            onClick={() =>
              void act("delete", () => deletePortalCredential(credential.portal)).then(onChanged)
            }
          >
            {busy === "delete" ? (
              <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
            ) : (
              <Trash2 className="size-3.5" aria-hidden />
            )}
            Remove
          </button>
        ) : null}
      </header>

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className={LABEL} htmlFor={field("login")}>
            Login ID
          </label>
          <input
            id={field("login")}
            className={INPUT}
            autoComplete="off"
            value={loginId}
            onChange={(e) => setLoginId(e.target.value)}
          />
        </div>

        <div>
          <label className={LABEL} htmlFor={field("password")}>
            Password
          </label>
          <div className="flex gap-2">
            <input
              id={field("password")}
              className={INPUT}
              type={revealed === null ? "password" : "text"}
              autoComplete="new-password"
              placeholder={credential.hasPassword ? "Saved — leave blank to keep it" : ""}
              value={revealed ?? password}
              onChange={(e) => {
                setRevealed(null);
                setPassword(e.target.value);
              }}
            />
            {credential.hasPassword ? (
              <button
                type="button"
                aria-label={revealed === null ? "Show the saved password" : "Hide the password"}
                className={`${BUTTON_GHOST} h-12 w-12 shrink-0 rounded-2xl px-0`}
                disabled={busy !== null}
                onClick={() => {
                  if (revealed !== null) {
                    setRevealed(null);
                    return;
                  }
                  // Fetched on demand, never with the list.
                  void act("reveal", () => revealPortalCredential(credential.portal)).then(
                    (full) => full && setRevealed(full.password ?? ""),
                  );
                }}
              >
                {busy === "reveal" ? (
                  <LoaderCircle className="size-4 animate-spin" aria-hidden />
                ) : revealed === null ? (
                  <Eye className="size-4" aria-hidden />
                ) : (
                  <EyeOff className="size-4" aria-hidden />
                )}
              </button>
            ) : null}
          </div>
          {credential.hasPassword ? (
            <p className={HINT}>Leaving this blank keeps the saved password.</p>
          ) : null}
        </div>

        <div>
          <label className={LABEL} htmlFor={field("image")}>
            Security image
          </label>
          <input
            id={field("image")}
            className={INPUT}
            value={image}
            onChange={(e) => setImage(e.target.value)}
          />
          <p className={HINT}>What the portal shows back to prove it is really them.</p>
        </div>

        <div>
          <label className={LABEL} htmlFor={field("phrase")}>
            Security phrase
          </label>
          <input
            id={field("phrase")}
            className={INPUT}
            value={securityPhrase}
            onChange={(e) => setSecurityPhrase(e.target.value)}
          />
        </div>

        <div>
          <label className={LABEL} htmlFor={field("secret")}>
            Secret code
          </label>
          <input
            id={field("secret")}
            className={INPUT}
            value={secretCode}
            onChange={(e) => setSecretCode(e.target.value)}
          />
        </div>

        <div>
          <label className={LABEL} htmlFor={field("reminder")}>
            Password reminder
          </label>
          <input
            id={field("reminder")}
            className={INPUT}
            value={reminder}
            onChange={(e) => setReminder(e.target.value)}
          />
        </div>

        <div className="sm:col-span-2">
          <label className={LABEL} htmlFor={field("notes")}>
            Notes
          </label>
          <textarea
            id={field("notes")}
            rows={2}
            maxLength={2000}
            className={TEXTAREA}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </div>
      </div>

      {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <button
          type="button"
          className={BUTTON}
          disabled={busy !== null}
          onClick={() =>
            void act("save", () =>
              savePortalCredential(credential.portal, {
                loginId: loginId.trim() || null,
                // Omitted entirely when untouched, so the server keeps the
                // stored password rather than being told to clear it.
                ...(password === "" ? {} : { password }),
                image: image.trim() || null,
                secretCode: secretCode.trim() || null,
                securityPhrase: securityPhrase.trim() || null,
                passwordReminder: reminder.trim() || null,
                notes: notes.trim() || null,
              }),
            ).then((result) => {
              if (!result) return;
              setPassword("");
              setRevealed(null);
              setSaved(true);
              onChanged();
            })
          }
        >
          {busy === "save" ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
          Save {credential.portalLabel}
        </button>

        {saved ? (
          <span className="text-sm font-medium text-emerald-600 dark:text-emerald-400">
            Saved.
          </span>
        ) : null}
      </div>
    </section>
  );
}
