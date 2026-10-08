import { useState } from "react";
import { Check, Copy, KeyRound, LoaderCircle, Plus, X } from "lucide-react";
import { createApiKey, getApiKeys, revokeApiKey, type ApiKey, type CreatedApiKey } from "../api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const BUTTON =
  "inline-flex h-10 items-center gap-1.5 rounded-xl bg-primary px-4 text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50";
const BUTTON_GHOST =
  "inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary disabled:opacity-50";

// What each scope lets a key do. Keys are the backend's ApiScopes.All; the
// server rejects anything it does not know, so a scope missing here only
// means it cannot be ticked, never that a key gets more than was chosen.
const SCOPES: { scope: string; description: string }[] = [
  { scope: "employees:read", description: "List & view employees" },
  { scope: "employees:write", description: "Create, edit, archive employees" },
  { scope: "claims:read", description: "List & view claims" },
  { scope: "claims:write", description: "Create / edit claims on behalf of employees" },
  { scope: "leave:read", description: "List & view leave requests" },
  { scope: "leave:write", description: "Create / edit leave requests" },
  { scope: "attendance:read", description: "List & view attendance" },
  { scope: "attendance:write", description: "Create, edit attendance records" },
  { scope: "overtime:read", description: "List & view overtime" },
  { scope: "overtime:write", description: "Create / edit overtime" },
  { scope: "projects:read", description: "List & view projects" },
  { scope: "projects:write", description: "Create, edit projects" },
  { scope: "teams:read", description: "List & view teams" },
  { scope: "teams:write", description: "Create, edit teams" },
  { scope: "accounts:read", description: "List & view the chart of accounts" },
  { scope: "accounts:write", description: "Create, edit custom accounts" },
  { scope: "policies:read", description: "List & view employee policies" },
  { scope: "policies:write", description: "Create, edit, archive employee policies" },
  { scope: "organizations:read", description: "View the company and its admins" },
  { scope: "organizations:write", description: "Grant admins; update company details" },
  { scope: "notifications:write", description: "Send notifications to employees" },
  { scope: "sso:write", description: "Sign an admin in from another app (SSO hand-off)" },
  { scope: "payroll:read", description: "List payroll runs, view run details, headcount" },
  { scope: "payroll:write", description: "Submit, approve, reject and revert payroll runs" },
  { scope: "documents:read", description: "List letter templates and letters on file; preview" },
  { scope: "documents:write", description: "Edit letter templates; generate letters" },
];

// Settings → API integrations: the organization's wp_live_ keys, as the
// previous system's superadmin-only "API" tab. Works on whichever company the
// session is in, so from support mode it issues keys for the customer. A token
// is shown ONCE, at creation; the list only ever holds its prefix.
export function ApiIntegrationsSettings({ organizationName }: { organizationName?: string | null }) {
  const keysQuery = useCachedQuery("/api-keys", getApiKeys);
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<CreatedApiKey | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();

  const keys = keysQuery.data ?? [];

  async function onRevoke(key: ApiKey) {
    const ok = await confirm({
      title: `Revoke "${key.name}"?`,
      message: "Anything using this token stops working at once. This can't be undone — create a new token instead.",
      confirmLabel: "Revoke token",
      destructive: true,
    });
    if (!ok) return;
    try {
      await revokeApiKey(key.id);
      await keysQuery.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Couldn't revoke the token.");
    }
  }

  return (
    <div className={`${CARD} space-y-5`}>
      {confirmDialog}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-foreground">API integrations</h2>
          <p className="text-sm text-muted-foreground">
            API tokens for {organizationName ? <strong>{organizationName}</strong> : "this company"}, for
            partner apps such as Altomate and ABPay. Visible to Fusioneta superadmins only.
          </p>
        </div>
        {!creating ? (
          <button type="button" className={BUTTON} onClick={() => { setCreated(null); setCreating(true); }}>
            <Plus className="h-4 w-4" /> New token
          </button>
        ) : null}
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {created ? <CreatedToken token={created} onDone={() => setCreated(null)} /> : null}

      {creating ? (
        <CreateTokenForm
          onCancel={() => setCreating(false)}
          onCreated={async (key) => {
            setCreating(false);
            setCreated(key);
            await keysQuery.refresh();
          }}
        />
      ) : null}

      {keysQuery.loading && keys.length === 0 ? (
        <SkeletonPanel />
      ) : keys.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-border px-4 py-6 text-center text-sm text-muted-foreground">
          No API tokens for this company yet.
        </p>
      ) : (
        <ul className="divide-y divide-border/60 rounded-2xl border border-border/70">
          {keys.map((key) => (
            <li key={key.id} className="flex flex-wrap items-start justify-between gap-3 px-4 py-3">
              <div className="min-w-0 space-y-1.5">
                <p className="flex items-center gap-2 text-sm font-bold text-foreground">
                  <KeyRound className="h-4 w-4 text-muted-foreground" />
                  {key.name}
                  {key.active ? null : (
                    <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
                      Revoked
                    </span>
                  )}
                </p>
                <p className="font-mono text-xs text-muted-foreground">{key.tokenPrefix}…</p>
                <div className="flex flex-wrap gap-1">
                  {key.scopes.map((scope) => (
                    <span key={scope} className="rounded-full bg-muted px-2 py-0.5 font-mono text-[10px] text-muted-foreground">
                      {scope}
                    </span>
                  ))}
                </div>
                <p className="text-[11px] text-muted-foreground">
                  Created {fmt(key.createdAt)} · {key.lastUsedAt ? `last used ${fmt(key.lastUsedAt)}` : "never used"}
                </p>
              </div>
              {key.active ? (
                <button type="button" className={BUTTON_GHOST} onClick={() => void onRevoke(key)}>
                  Revoke
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function CreateTokenForm({
  onCancel,
  onCreated,
}: {
  onCancel: () => void;
  onCreated: (key: CreatedApiKey) => void | Promise<void>;
}) {
  const [name, setName] = useState("");
  const [scopes, setScopes] = useState<Set<string>>(new Set());
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const allSelected = scopes.size === SCOPES.length;

  const toggle = (scope: string) =>
    setScopes((prev) => {
      const next = new Set(prev);
      if (next.has(scope)) next.delete(scope);
      else next.add(scope);
      return next;
    });

  async function submit() {
    setPending(true);
    setError(null);
    try {
      await onCreated(await createApiKey({ name: name.trim(), scopes: [...scopes] }));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Couldn't create the token.");
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="space-y-4 rounded-2xl border border-border/70 bg-muted/30 p-4">
      <div className="flex items-center justify-between">
        <p className="text-sm font-bold text-foreground">New API token</p>
        <button type="button" onClick={onCancel} className="rounded-full p-1.5 text-muted-foreground hover:bg-muted" aria-label="Cancel">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div>
        <label htmlFor="apiTokenName" className="mb-1.5 block text-sm font-semibold text-foreground">Name</label>
        <input
          id="apiTokenName"
          className={INPUT}
          maxLength={120}
          placeholder="e.g. Altomate Accounting"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <p className="mt-1 text-xs text-muted-foreground">Internal label so you can tell tokens apart in the list.</p>
      </div>
      <div>
        <div className="mb-1.5 flex items-center justify-between">
          <p className="text-sm font-semibold text-foreground">Scopes ({scopes.size} selected)</p>
          <button
            type="button"
            className="text-xs font-bold text-primary hover:underline"
            onClick={() => setScopes(allSelected ? new Set() : new Set(SCOPES.map((s) => s.scope)))}
          >
            {allSelected ? "Clear all" : "Select all"}
          </button>
        </div>
        <p className="mb-2 text-xs text-muted-foreground">
          A token gets exactly what you tick — nothing more. Scopes can't be edited later; revoke and create a new token instead.
        </p>
        <div className="grid max-h-72 gap-1 overflow-y-auto rounded-xl border border-border/60 bg-card p-2 sm:grid-cols-2">
          {SCOPES.map(({ scope, description }) => (
            <label key={scope} className="flex cursor-pointer items-start gap-2 rounded-lg px-2 py-1.5 hover:bg-muted">
              <input type="checkbox" className="mt-0.5" checked={scopes.has(scope)} onChange={() => toggle(scope)} />
              <span>
                <span className="block font-mono text-xs font-semibold text-foreground">{scope}</span>
                <span className="block text-[11px] text-muted-foreground">{description}</span>
              </span>
            </label>
          ))}
        </div>
      </div>
      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
      <div className="flex justify-end gap-2">
        <button type="button" className={BUTTON_GHOST} onClick={onCancel}>Cancel</button>
        <button type="button" className={BUTTON} disabled={pending || !name.trim() || scopes.size === 0} onClick={() => void submit()}>
          {pending ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
          Create token
        </button>
      </div>
    </div>
  );
}

// The one time the full token is visible. It is never stored in the page and
// cannot be fetched again.
function CreatedToken({ token, onDone }: { token: CreatedApiKey; onDone: () => void }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="space-y-3 rounded-2xl border border-success/40 bg-success/10 p-4">
      <p className="text-sm font-bold text-foreground">Token created: {token.name}</p>
      <p className="text-xs text-muted-foreground">
        Copy it now and store it safely. This is the only time it is shown — if it is lost, revoke it and create a new token.
      </p>
      <div className="flex items-center gap-2">
        <code className="min-w-0 flex-1 break-all rounded-xl border border-border bg-card px-3 py-2 font-mono text-xs text-foreground">
          {token.token}
        </code>
        <button
          type="button"
          className={BUTTON_GHOST}
          onClick={() => {
            void navigator.clipboard.writeText(token.token).then(() => setCopied(true));
          }}
        >
          {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
      <div className="flex justify-end">
        <button type="button" className={BUTTON_GHOST} onClick={onDone}>I've saved it</button>
      </div>
    </div>
  );
}

function fmt(iso: string) {
  return new Date(iso).toLocaleString("en-MY", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });
}
