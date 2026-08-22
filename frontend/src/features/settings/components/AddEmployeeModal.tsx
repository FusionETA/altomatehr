import { type FormEvent, useState } from "react";
import { Check, Copy, LoaderCircle, Sparkles, X } from "lucide-react";
import { createEmployee, ROLES, type Employee } from "@/features/employees/api";
import type { Policy } from "@/features/policies/api";

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
// supports: email + initial password + role + supervisor + policy. If the email already
// belongs to a user, the backend reuses that identity (the multi-org case).
export function AddEmployeeModal({
  employees,
  policies,
  onClose,
  onCreated,
}: {
  employees: Employee[];
  policies: Policy[];
  onClose: () => void;
  onCreated: (created: Employee) => void;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState<string>("Employee");
  const [supervisorId, setSupervisorId] = useState<string>(NONE);
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
        role,
        supervisorId: supervisorId === NONE ? null : supervisorId,
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

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-[560px] overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl">
        <form
          onSubmit={handleSubmit}
          className="nice-scrollbar max-h-[90vh] space-y-5 overflow-y-auto p-6 pl-1 sm:p-8 sm:pl-1"
        >
          <div className="flex items-start justify-between gap-4">
            <div>
              <h2 className="text-lg font-black text-foreground">Add employee</h2>
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
              Share this with the new hire so they can log in. Leave blank if they already have an
              account.
            </p>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <label htmlFor="add-role" className={LABEL}>
                Role
              </label>
              <select
                id="add-role"
                className={INPUT}
                value={role}
                onChange={(e) => setRole(e.target.value)}
              >
                {ROLES.map((r) => (
                  <option key={r} value={r}>
                    {r}
                  </option>
                ))}
              </select>
            </div>
            <div className="space-y-2">
              <label htmlFor="add-policy" className={LABEL}>
                Policy
              </label>
              <select
                id="add-policy"
                className={INPUT}
                value={policyId}
                onChange={(e) => setPolicyId(e.target.value)}
              >
                <option value={NONE}>Default policy</option>
                {policies
                  .filter((p) => !p.isArchived)
                  .map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
              </select>
            </div>
          </div>

          <div className="space-y-2">
            <label htmlFor="add-supervisor" className={LABEL}>
              Supervisor
            </label>
            <select
              id="add-supervisor"
              className={INPUT}
              value={supervisorId}
              onChange={(e) => setSupervisorId(e.target.value)}
            >
              <option value={NONE}>No supervisor</option>
              {employees.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.email}
                </option>
              ))}
            </select>
          </div>

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          <div className="flex justify-end gap-3 pt-1">
            <button
              type="button"
              onClick={onClose}
              className="rounded-2xl border border-border bg-card px-5 py-2.5 text-sm font-semibold text-foreground transition hover:bg-muted"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={saving}
              className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              Add employee
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
