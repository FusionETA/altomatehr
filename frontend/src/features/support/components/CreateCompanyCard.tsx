import { useState } from "react";
import { Check, LoaderCircle, Plus } from "lucide-react";
import { MIN_PASSWORD_LENGTH } from "@/features/auth/api";
import { Switch } from "@/shared/components/ui/switch";
import { createSupportCompany, type Plan, type Tier } from "../api";

const input =
  "h-11 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

const choice = (active: boolean) =>
  `flex-1 rounded-2xl border px-3 py-2 text-sm font-semibold transition ${
    active
      ? "border-primary bg-primary/10 text-primary"
      : "border-border bg-card text-muted-foreground hover:text-foreground"
  }`;

// Provision a new company and its Owner's sign-in. An owner email that already
// has an account is simply made Owner of the new company, keeping its own
// password — so the password is only needed for a new account.
export function CreateCompanyCard({ onCreated }: { onCreated: () => void }) {
  const [orgName, setOrgName] = useState("");
  const [ownerName, setOwnerName] = useState("");
  const [ownerEmail, setOwnerEmail] = useState("");
  const [password, setPassword] = useState("");
  const [plan, setPlan] = useState<Plan>("DIY");
  const [tier, setTier] = useState<Tier>("PAID");
  const [claims, setClaims] = useState(true);
  const [attendance, setAttendance] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);

  const ready = orgName.trim().length >= 2 && ownerName.trim() && ownerEmail.trim();

  async function submit() {
    if (!ready) return;
    if (password && password.length < MIN_PASSWORD_LENGTH) {
      setError(`The password needs at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    setSaving(true);
    setError(null);
    setDone(null);
    try {
      const res = await createSupportCompany({
        orgName: orgName.trim(),
        ownerName: ownerName.trim(),
        ownerEmail: ownerEmail.trim(),
        password: password || undefined,
        plan,
        tier: plan === "DIY" ? tier : null,
        claims,
        attendance,
      });
      setDone(
        res.ownerCreated
          ? `${res.organizationName} is ready. ${ownerEmail.trim()} can sign in with the password you set.`
          : `${res.organizationName} is ready. ${ownerEmail.trim()} already had an account and is now its Owner.`,
      );
      setOrgName("");
      setOwnerName("");
      setOwnerEmail("");
      setPassword("");
      onCreated();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create the company.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="rounded-[28px] border border-border/60 bg-card p-5 shadow-ambient sm:p-6">
      <h2 className="text-lg font-black text-foreground">Provision a new company</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Creates the company and makes the owner its Owner.
      </p>

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <label className="block space-y-1.5 sm:col-span-2">
          <span className="text-sm font-semibold text-foreground">Company name</span>
          <input value={orgName} onChange={(e) => setOrgName(e.target.value)} placeholder="Acme Sdn Bhd" className={input} />
        </label>
        <label className="block space-y-1.5">
          <span className="text-sm font-semibold text-foreground">Owner name</span>
          <input value={ownerName} onChange={(e) => setOwnerName(e.target.value)} className={input} />
        </label>
        <label className="block space-y-1.5">
          <span className="text-sm font-semibold text-foreground">Owner email</span>
          <input
            type="email"
            value={ownerEmail}
            onChange={(e) => setOwnerEmail(e.target.value)}
            placeholder="owner@company.com"
            autoComplete="off"
            className={input}
          />
        </label>
        <label className="block space-y-1.5 sm:col-span-2">
          <span className="text-sm font-semibold text-foreground">Password for a new account</span>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
            className={input}
          />
          <span className="text-xs text-muted-foreground">
            At least {MIN_PASSWORD_LENGTH} characters. Leave blank if the email already has an account.
          </span>
        </label>

        <div className="space-y-1.5">
          <p className="text-sm font-semibold text-foreground">Plan</p>
          <div className="flex gap-2">
            {(["DIY", "EXPERT"] as const).map((p) => (
              <button key={p} type="button" onClick={() => setPlan(p)} className={choice(plan === p)}>
                {p}
              </button>
            ))}
          </div>
        </div>
        {plan === "DIY" ? (
          <div className="space-y-1.5">
            <p className="text-sm font-semibold text-foreground">Tier</p>
            <div className="flex gap-2">
              {(["FREE", "PAID"] as const).map((t) => (
                <button key={t} type="button" onClick={() => setTier(t)} className={choice(tier === t)}>
                  {t}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div />
        )}

        <label className="flex items-center justify-between gap-3 rounded-2xl border border-border/60 px-4 py-2.5">
          <span className="text-sm font-medium text-foreground">Claims</span>
          <Switch checked={claims} onCheckedChange={setClaims} />
        </label>
        <label className="flex items-center justify-between gap-3 rounded-2xl border border-border/60 px-4 py-2.5">
          <span className="text-sm font-medium text-foreground">Attendance</span>
          <Switch checked={attendance} onCheckedChange={setAttendance} />
        </label>
      </div>

      {error ? <p className="mt-4 text-sm font-medium text-destructive">{error}</p> : null}
      {done ? (
        <p className="mt-4 flex items-start gap-2 rounded-2xl border border-emerald-500/30 bg-emerald-500/10 p-3 text-sm text-emerald-800 dark:text-emerald-300">
          <Check className="mt-0.5 h-4 w-4 shrink-0" />
          {done}
        </p>
      ) : null}

      <div className="mt-5 flex justify-end">
        <button
          type="button"
          onClick={() => void submit()}
          disabled={saving || !ready}
          className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
        >
          {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Create company
        </button>
      </div>
    </section>
  );
}
