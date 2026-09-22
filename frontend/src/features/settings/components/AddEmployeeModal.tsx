import { type FormEvent, useState } from "react";
import { createPortal } from "react-dom";
import { Check, Copy, LoaderCircle, Sparkles, X } from "lucide-react";
import { createEmployee, STAFF_ROLES, type Employee } from "@/features/employees/api";
import type { Policy } from "@/features/policies/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";
const NONE = "__none__";

// Readable random password (no ambiguous characters) the admin can share with the hire.
function generatePassword() {
  const chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
  const rnd = crypto.getRandomValues(new Uint32Array(12));
  let out = "";
  for (let i = 0; i < 12; i++) out += chars[rnd[i] % chars.length];
  return out;
}

// Mirrors the monolith's "Add employee" dialog, mapped to what our slim account model
// supports: email + initial password + role + policy. If the email already
// belongs to a user, the backend reuses that identity (the multi-org case).
export function AddEmployeeModal({
  policies,
  onClose,
  onCreated,
}: {
  policies: Policy[];
  onClose: () => void;
  onCreated: (created: Employee) => void;
}) {
  const [name, setName] = useState("");
  const [employeeNumber, setEmployeeNumber] = useState("");
  const [jobTitle, setJobTitle] = useState("");
  const [joinDate, setJoinDate] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [dateOfBirth, setDateOfBirth] = useState("");
  const [sendWelcome, setSendWelcome] = useState(false);
  const [role, setRole] = useState<string>("Employee");
  const [policyId, setPolicyId] = useState<string>(NONE);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const created = await createEmployee({
        email: email.trim(),
        password: password.trim() || undefined,
        // Blank stays undefined rather than "": an empty string means
        // "clear this" to the API, which is not what an untouched field means.
        name: name.trim() || undefined,
        employeeNumber: employeeNumber.trim() || undefined,
        jobTitle: jobTitle.trim() || undefined,
        joinDate: joinDate || undefined,
        dateOfBirth: dateOfBirth || undefined,
        sendWelcomeEmail: sendWelcome,
        role,
        policyId: policyId === NONE ? null : policyId,
      });
      onCreated(created);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not add the employee.");
      setSaving(false); // keep the modal open so they can fix it
    }
  }

  function copyPassword() {
    if (!password) return;
    void navigator.clipboard.writeText(password).then(() => {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    });
  }

  // Portaled: EmployeesSettings renders this inside its card, and that card sets
  // backdrop-filter — any ancestor with a filter becomes the containing block for
  // position:fixed, so inset-0/90vh would resolve against the card and hang the
  // dialog off both edges of the screen instead of centring it.
  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="flex max-h-[90vh] w-full max-w-[620px] flex-col overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl">
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col">
          <div className="flex shrink-0 items-start justify-between gap-4 border-b border-border/60 px-6 py-5 sm:px-8">
            <div>
              <h2 className="text-2xl font-black text-foreground">Add employee</h2>
              <p className="mt-1 text-sm text-muted-foreground">
                Add a member to this company. If the email already exists, that person is reused —
                no password needed.
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          <div className="nice-scrollbar min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-6 sm:px-8">
            <div className="space-y-2">
              <label htmlFor="add-name" className={LABEL}>
                Full name
              </label>
              <input
                id="add-name"
                required
                className={INPUT}
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Ahmad bin Ali"
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <label htmlFor="add-employee-number" className={LABEL}>
                  Employee ID
                </label>
                <input
                  id="add-employee-number"
                  className={INPUT}
                  value={employeeNumber}
                  onChange={(e) => setEmployeeNumber(e.target.value)}
                  placeholder="Optional"
                />
              </div>
              <div className="space-y-2">
                <label htmlFor="add-job-title" className={LABEL}>
                  Job title
                </label>
                <input
                  id="add-job-title"
                  className={INPUT}
                  value={jobTitle}
                  onChange={(e) => setJobTitle(e.target.value)}
                  placeholder="Optional"
                />
              </div>
            </div>

            <div className="space-y-2">
              <label htmlFor="add-join-date" className={LABEL}>
                Join date
              </label>
              <input
                id="add-join-date"
                type="date"
                className={INPUT}
                value={joinDate}
                onChange={(e) => setJoinDate(e.target.value)}
              />
              <p className="text-xs text-muted-foreground">
                Drives pro-rated leave — setting it recomputes what they have earned this year.
              </p>
            </div>

            <div className="space-y-2">
              <label htmlFor="add-email" className={LABEL}>
                Email
              </label>
              <input
                id="add-email"
                type="email"
                required
                className={INPUT}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="person@company.com"
              />
            </div>

            <div className="space-y-2">
              <label htmlFor="add-password" className={LABEL}>
                Initial password
              </label>
              <div className="flex gap-2">
                <input
                  id="add-password"
                  className={INPUT}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Only for a brand-new account"
                />
                <button
                  type="button"
                  onClick={() => setPassword(generatePassword())}
                  className="inline-flex h-12 shrink-0 items-center gap-1.5 rounded-2xl border border-border bg-card px-3 text-xs font-semibold text-foreground transition hover:border-primary hover:text-primary"
                >
                  <Sparkles className="h-4 w-4" /> Generate
                </button>
                <button
                  type="button"
                  onClick={copyPassword}
                  disabled={!password}
                  aria-label="Copy password"
                  className="inline-flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-border bg-card text-muted-foreground transition hover:border-primary hover:text-primary disabled:opacity-40"
                >
                  {copied ? <Check className="h-4 w-4 text-primary" /> : <Copy className="h-4 w-4" />}
                </button>
              </div>
              <p className="text-xs text-muted-foreground">
                Leave blank and their password is their{" "}
                <strong className="text-foreground">email followed by their birthday as MMDD</strong>
                {dateOfBirth ? (
                  <>
                    {" — "}
                    <span className="font-mono text-foreground">
                      {email.trim() || "email"}
                      {dateOfBirth.slice(5, 7)}
                      {dateOfBirth.slice(8, 10)}
                    </span>
                  </>
                ) : null}
                . Type one here to override it, or leave blank if they already have an account.
              </p>
            </div>

            <div className="space-y-2">
              <label htmlFor="add-dob" className={LABEL}>
                Date of birth
              </label>
              <input
                id="add-dob"
                type="date"
                className={INPUT}
                value={dateOfBirth}
                onChange={(e) => setDateOfBirth(e.target.value)}
              />
              <p className="text-xs text-muted-foreground">
                Needed for a brand-new account, because the first password is built from it. Not
                needed if you typed a password above, or if they already have an account.
              </p>
            </div>

            <label className="flex items-start gap-3 rounded-2xl border border-border/60 bg-surface-low p-3">
              <input
                type="checkbox"
                checked={sendWelcome}
                onChange={(e) => setSendWelcome(e.target.checked)}
                className="mt-0.5 h-4 w-4 shrink-0 accent-[var(--color-primary)]"
              />
              <span className="text-xs text-muted-foreground">
                <span className="font-semibold text-foreground">Send a welcome email</span> — where
                to sign in and how their password is formed. It describes the rule rather than
                printing the password, so the email on its own isn't a working login.
              </span>
            </label>

            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <label htmlFor="add-role" className={LABEL}>
                  Role
                </label>
                <Select value={role} onValueChange={setRole}>
                  <SelectTrigger id="add-role">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {STAFF_ROLES.map((r) => (
                      <SelectItem key={r} value={r}>
                        {r}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <label htmlFor="add-policy" className={LABEL}>
                  Policy
                </label>
                {/* NONE is already a non-empty sentinel, which Radix needs —
                    it reserves the empty string for "nothing selected". */}
                <Select value={policyId} onValueChange={setPolicyId}>
                  <SelectTrigger id="add-policy">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NONE}>Default policy</SelectItem>
                    {policies
                      .filter((p) => !p.isArchived)
                      .map((p) => (
                        <SelectItem key={p.id} value={p.id}>
                          {p.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
          </div>

          {error ? (
            <p className="mx-6 mt-4 shrink-0 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive sm:mx-8">
              {error}
            </p>
          ) : null}

          <div className="flex shrink-0 justify-end gap-3 border-t border-border/60 px-6 py-4 sm:px-8">
            <button
              type="button"
              onClick={onClose}
              className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={saving}
              className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              Add employee
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body,
  );
}
