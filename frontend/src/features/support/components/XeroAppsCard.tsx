import { useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { Check, Copy, LoaderCircle, Plus, X } from "lucide-react";
import { SearchInput } from "@/shared/components/SearchInput";
import { Switch } from "@/shared/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  addXeroApp,
  deleteXeroApp,
  getXeroAppsOverview,
  setXeroCompanyOwner,
  setXeroOwnerChoice,
  updateXeroApp,
  XERO_APPS_PATH,
  type XeroAppChoice,
  type XeroAppCompany,
  type XeroAppsOverview,
  type XeroAppSummary,
  type XeroOwnerGroup,
} from "../api";

const INPUT =
  "h-11 w-full rounded-2xl border border-border bg-background px-4 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

// The owner's choice as one select value: "Shared", "Original", or
// "Standalone:<appId>" ("Standalone:" = their own app, not added yet).
const encode = (o: XeroOwnerGroup) => (o.choice === "Standalone" ? `Standalone:${o.xeroAppId ?? ""}` : o.choice);
function decode(value: string): { choice: XeroAppChoice; xeroAppId: string | null } {
  if (value.startsWith("Standalone:")) return { choice: "Standalone", xeroAppId: value.slice(11) || null };
  return { choice: value as XeroAppChoice, xeroAppId: null };
}

// Which Xero app each owner's companies connect through. Changing an owner's
// app never breaks a connected company: it keeps working on the app that
// connected it until an admin there presses Reconnect.
export function XeroAppsCard() {
  const query = useCachedQuery(XERO_APPS_PATH, getXeroAppsOverview);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<XeroAppSummary | "new" | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const data = query.data;
  const owners = useMemo(() => {
    const term = search.trim().toLowerCase();
    const all = data?.owners ?? [];
    if (!term) return all;
    return all.filter(
      (o) =>
        o.name.toLowerCase().includes(term) ||
        o.email.toLowerCase().includes(term) ||
        o.companies.some((c) => c.name.toLowerCase().includes(term)),
    );
  }, [data, search]);

  async function run(key: string, action: () => Promise<unknown>) {
    setBusy(key);
    setError(null);
    try {
      await action();
      await query.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save that change.");
    } finally {
      setBusy(null);
    }
  }

  async function copyRedirect() {
    if (!data?.redirectUri) return;
    try {
      await navigator.clipboard.writeText(data.redirectUri);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      setError("Couldn't copy. Select the URL and copy it instead.");
    }
  }

  return (
    <section className="rounded-[28px] border border-border/60 bg-card p-5 shadow-ambient sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-lg font-black text-foreground">Xero apps</h2>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Which Xero app each owner's companies connect through. Moving an owner doesn't break a
            connected company: it keeps working until an admin there presses Reconnect.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setEditing("new")}
          className="inline-flex items-center gap-1.5 rounded-full bg-primary px-4 py-2 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90"
        >
          <Plus className="h-3.5 w-3.5" /> Add app
        </button>
      </div>

      {query.loading && !data ? (
        <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
          <LoaderCircle className="h-4 w-4 animate-spin" /> Loading Xero apps…
        </div>
      ) : query.error && !data ? (
        <p className="py-10 text-sm font-medium text-destructive">{query.error}</p>
      ) : data ? (
        <div className="mt-4 space-y-5">
          <div className="rounded-2xl border border-border/60 bg-surface-low/50 px-4 py-3 text-sm">
            <p className="font-semibold text-foreground">Redirect URI for every app</p>
            <p className="mt-0.5 text-xs text-muted-foreground">
              Add this in the app's settings at developer.xero.com, or Xero refuses the sign-in.
            </p>
            <div className="mt-2 flex items-center gap-2">
              <code className="min-w-0 flex-1 truncate rounded-xl bg-background px-3 py-2 text-xs">
                {data.redirectUri || "Not set (Xero:RedirectUri)"}
              </code>
              <button
                type="button"
                onClick={() => void copyRedirect()}
                disabled={!data.redirectUri}
                className="inline-flex items-center gap-1 rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-50"
              >
                {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                {copied ? "Copied" : "Copy"}
              </button>
            </div>
          </div>

          <AppsTable
            data={data}
            busy={busy}
            onEdit={setEditing}
            onDelete={(app) => {
              if (window.confirm(`Delete the Xero app "${app.name}"?`)) {
                void run(`del:${app.id}`, () => deleteXeroApp(app.id));
              }
            }}
          />

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          <div className="space-y-2">
            <h3 className="text-sm font-black text-foreground">Owners</h3>
            <SearchInput value={search} onChange={setSearch} placeholder="Search by owner or company" />
            <div className="overflow-x-auto rounded-2xl border border-border/50">
              <table className="w-full min-w-[760px] text-sm">
                <thead>
                  <tr className="border-b border-border/60 text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                    <th className="px-4 py-2.5 font-semibold">Owner</th>
                    <th className="px-4 py-2.5 font-semibold">Companies</th>
                    <th className="w-64 px-4 py-2.5 font-semibold">Xero app</th>
                  </tr>
                </thead>
                <tbody>
                  {owners.map((o) => (
                    <tr key={o.userId} className="border-b border-border/30 align-top last:border-0">
                      <td className="px-4 py-3">
                        <span className="block font-semibold text-foreground">{o.name}</span>
                        <span className="block text-xs text-muted-foreground">{o.email}</span>
                      </td>
                      <td className="px-4 py-3">
                        <ul className="space-y-1.5">
                          {o.companies.map((c) => (
                            <CompanyLine
                              key={c.id}
                              company={c}
                              data={data}
                              groupOwner={o.userId}
                              busy={busy === `co:${c.id}`}
                              onMove={(ownerUserId) =>
                                void run(`co:${c.id}`, () => setXeroCompanyOwner(c.id, ownerUserId))
                              }
                            />
                          ))}
                        </ul>
                      </td>
                      <td className="px-4 py-3">
                        <Select
                          value={encode(o)}
                          disabled={busy === `own:${o.userId}`}
                          onValueChange={(v) =>
                            void run(`own:${o.userId}`, () => setXeroOwnerChoice(o.userId, decode(v)))
                          }
                        >
                          <SelectTrigger className="h-10 rounded-2xl">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="Shared">Shared app</SelectItem>
                            <SelectItem value="Original">Original app (server settings)</SelectItem>
                            {data.apps.map((a) => (
                              <SelectItem key={a.id} value={`Standalone:${a.id}`}>
                                Own app: {a.name}
                              </SelectItem>
                            ))}
                            <SelectItem value="Standalone:">Own app, not added yet (blocks Connect)</SelectItem>
                          </SelectContent>
                        </Select>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {data.companiesWithoutOwner.length > 0 ? (
              <p className="text-xs text-muted-foreground">
                {data.companiesWithoutOwner.length} companies have no owner and use the shared app.
              </p>
            ) : null}
          </div>
        </div>
      ) : null}

      {editing ? (
        <AppDialog
          app={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void query.refresh();
          }}
        />
      ) : null}
    </section>
  );
}

function AppsTable({
  data,
  busy,
  onEdit,
  onDelete,
}: {
  data: XeroAppsOverview;
  busy: string | null;
  onEdit: (app: XeroAppSummary) => void;
  onDelete: (app: XeroAppSummary) => void;
}) {
  const shared = <span className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-semibold text-primary">Shared</span>;
  return (
    <div className="overflow-x-auto rounded-2xl border border-border/50">
      <table className="w-full min-w-[640px] text-sm">
        <thead>
          <tr className="border-b border-border/60 text-left text-[11px] uppercase tracking-wider text-muted-foreground">
            <th className="px-4 py-2.5 font-semibold">App</th>
            <th className="px-4 py-2.5 font-semibold">Client id</th>
            <th className="px-4 py-2.5 text-right font-semibold">Owners</th>
            <th className="px-4 py-2.5 text-right font-semibold">Connected</th>
            <th className="px-4 py-2.5 text-right font-semibold">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr className="border-b border-border/30">
            <td className="px-4 py-3 font-semibold text-foreground">
              Original app {data.originalApp.isShared ? shared : null}
              <span className="block text-xs font-normal text-muted-foreground">From the server settings</span>
            </td>
            <td className="px-4 py-3 font-mono text-xs text-muted-foreground">
              {data.originalApp.configured ? data.originalApp.clientIdEnding : "Not configured"}
            </td>
            <td className="px-4 py-3 text-right text-muted-foreground">
              {data.owners.filter((o) => o.choice === "Original").length}
            </td>
            <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{data.originalApp.connections}</td>
            <td className="px-4 py-3" />
          </tr>
          {data.apps.map((a) => (
            <tr key={a.id} className="border-b border-border/30 last:border-0">
              <td className="px-4 py-3 font-semibold text-foreground">
                {a.name} {a.isShared ? shared : null}
              </td>
              <td className="px-4 py-3 font-mono text-xs text-muted-foreground">{a.clientIdEnding}</td>
              <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{a.owners}</td>
              <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{a.connections}</td>
              <td className="px-4 py-3">
                <div className="flex justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => onEdit(a)}
                    className="rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    disabled={busy === `del:${a.id}`}
                    onClick={() => onDelete(a)}
                    className="rounded-full border border-destructive/40 px-3 py-1.5 text-xs font-semibold text-destructive transition hover:bg-destructive/5 disabled:opacity-50"
                  >
                    Delete
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function CompanyLine({
  company,
  data,
  groupOwner,
  busy,
  onMove,
}: {
  company: XeroAppCompany;
  data: XeroAppsOverview;
  groupOwner: string;
  busy: boolean;
  onMove: (ownerUserId: string) => void;
}) {
  const via =
    company.connectedVia === "original"
      ? "Original app"
      : data.apps.find((a) => a.id === company.connectedVia)?.name ?? "an app";
  return (
    <li className="text-sm">
      <span className="font-medium text-foreground">{company.name}</span>
      {company.connectedTenantName ? (
        <span className="block text-xs text-muted-foreground">
          Connected to {company.connectedTenantName} via {via}
          {company.needsReconnect ? " · needs reconnect" : ""}
          {company.reconnectToMoveApp ? " · moves on reconnect" : ""}
        </span>
      ) : (
        <span className="block text-xs text-muted-foreground">Not connected</span>
      )}
      {company.owners.length > 1 ? (
        <label className="mt-1 flex items-center gap-2 text-xs text-muted-foreground">
          Follows
          <select
            value={groupOwner}
            disabled={busy}
            onChange={(e) => onMove(e.target.value)}
            className="rounded-lg border border-border bg-background px-2 py-1 text-xs text-foreground"
          >
            {company.owners.map((owner) => (
              <option key={owner.userId} value={owner.userId}>
                {owner.name}
              </option>
            ))}
          </select>
        </label>
      ) : null}
    </li>
  );
}

// Add or edit an app. The secret is write-only: blank on an edit keeps it.
function AppDialog({
  app,
  onClose,
  onSaved,
}: {
  app: XeroAppSummary | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(app?.name ?? "");
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [isShared, setIsShared] = useState(app?.isShared ?? false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Adding needs both; on an edit a blank client id or secret keeps the saved one.
  const canSave =
    name.trim().length > 0 &&
    (app !== null || (clientId.trim().length >= 8 && clientSecret.trim().length > 0));

  async function save() {
    setSaving(true);
    setError(null);
    try {
      const body = {
        name: name.trim(),
        clientId: clientId.trim() || undefined,
        clientSecret: clientSecret.trim() || undefined,
        isShared,
      };
      if (app) await updateXeroApp(app.id, body);
      else await addXeroApp(body);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the app.");
      setSaving(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm" onClick={onClose}>
      <div
        className="w-full max-w-md rounded-[28px] border border-border/70 bg-card p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3">
          <h3 className="text-base font-black text-foreground">{app ? `Edit ${app.name}` : "Add a Xero app"}</h3>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 space-y-4">
          <label className="block space-y-1.5">
            <span className="text-sm font-semibold text-foreground">Name</span>
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} placeholder="e.g. Ayu Borneo" className={INPUT} />
          </label>
          <label className="block space-y-1.5">
            <span className="text-sm font-semibold text-foreground">Client id</span>
            <input
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              maxLength={80}
              autoComplete="off"
              placeholder={app ? `Leave blank to keep (ends ${app.clientIdEnding})` : "From developer.xero.com"}
              className={`${INPUT} font-mono`}
            />
          </label>
          <label className="block space-y-1.5">
            <span className="text-sm font-semibold text-foreground">Client secret</span>
            <input
              type="password"
              value={clientSecret}
              onChange={(e) => setClientSecret(e.target.value)}
              maxLength={200}
              autoComplete="new-password"
              placeholder={app ? "Leave blank to keep the saved one" : "Shown once by Xero when generated"}
              className={`${INPUT} font-mono`}
            />
          </label>
          <label className="flex items-center justify-between gap-3 rounded-2xl border border-border/60 px-4 py-2.5">
            <span className="text-sm font-medium text-foreground">
              Shared app
              <span className="block text-xs font-normal text-muted-foreground">
                Owners on "Shared app" connect through this one.
              </span>
            </span>
            <Switch checked={isShared} onCheckedChange={setIsShared} />
          </label>

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              disabled={saving}
              className="rounded-2xl bg-muted px-4 py-2 text-sm font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={() => void save()}
              disabled={saving || !canSave}
              className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              {app ? "Save app" : "Add app"}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
