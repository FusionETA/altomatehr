import { useEffect, useState } from "react";
import { Building2, Link2, LoaderCircle, Unplug } from "lucide-react";
import { createPortal } from "react-dom";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { disconnectXero, getXeroConnectUrl, getXeroStatus, type XeroStatus } from "../api";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

// Connect, reconnect and disconnect Xero, and say which org is connected.
//
// The app told admins to "Connect Xero in System Settings first" while System
// Settings had no such control: the backend had /xero/connect-url, /callback
// and /disconnect, and nothing in the UI called any of them. The only way in
// was to hit the API by hand.
//
// Modelled on the previous system's XeroConnectionCard — the tenant named with
// its id underneath, when it was connected, and Reconnect kept separate from
// Disconnect. Reconnect re-runs the same OAuth flow without tearing anything
// down, which is how an admin swaps the signed-in Xero user or recovers a
// refresh token that Xero has expired.
export function XeroConnectionCard() {
  const [status, setStatus] = useState<XeroStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);

  useEffect(() => {
    getXeroStatus()
      .then(setStatus)
      .catch(() => setStatus({ connected: false, tenantName: null, tenantId: null, connectedAt: null }));
  }, []);

  async function startConnect() {
    setBusy(true);
    setError(null);
    try {
      // Xero has to be reached as a full page navigation: it's a third-party
      // consent screen, not something that can be fetched.
      const { url } = await getXeroConnectUrl(window.location.href);
      window.location.href = url;
    } catch (e: unknown) {
      setError(message(e, "Could not start the Xero connection."));
      setBusy(false);
    }
  }

  async function confirmDisconnect() {
    setBusy(true);
    setError(null);
    try {
      await disconnectXero();
      setStatus({ connected: false, tenantName: null, tenantId: null, connectedAt: null });
      setConfirmOpen(false);
    } catch (e: unknown) {
      setError(message(e, "Could not disconnect Xero."));
    } finally {
      setBusy(false);
    }
  }

  const connected = status?.connected === true;

  return (
    <section className={CARD}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-black text-foreground">
            <Link2 className="h-5 w-5 text-primary" />
            Xero connection
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {connected
              ? "Connected for this company. Xero owns the chart of accounts and projects while it is."
              : "Connect Xero to push approved claims as bills and import the chart of accounts."}
          </p>
        </div>

        {status !== null && !connected ? (
          <button
            type="button"
            disabled={busy}
            onClick={startConnect}
            className="inline-flex h-11 shrink-0 items-center gap-2 rounded-2xl bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Link2 className="h-4 w-4" />}
            Connect this company to Xero
          </button>
        ) : null}
      </div>

      {status === null ? (
        <p className="mt-4 text-sm text-muted-foreground">Checking Xero…</p>
      ) : connected ? (
        <div className="mt-4 flex flex-wrap items-start justify-between gap-4 rounded-[20px] border border-border/70 bg-surface-low p-4">
          <div className="min-w-0">
            <p className="flex items-center gap-2 font-bold text-foreground">
              <Building2 className="h-4 w-4 shrink-0 text-primary" />
              {status.tenantName ?? "Xero organisation"}
            </p>
            {status.tenantId ? (
              <p className="mt-0.5 truncate text-sm text-muted-foreground">{status.tenantId}</p>
            ) : null}
            {status.connectedAt ? (
              <p className="mt-0.5 text-xs text-muted-foreground">
                Connected:{" "}
                {new Intl.DateTimeFormat("en-MY", {
                  dateStyle: "medium",
                  timeStyle: "short",
                }).format(new Date(status.connectedAt))}
              </p>
            ) : null}
          </div>

          <div className="flex shrink-0 items-center gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={startConnect}
              className="inline-flex h-10 items-center gap-1.5 rounded-full border border-border/70 bg-card px-4 text-xs font-bold text-muted-foreground shadow-sm transition hover:text-foreground disabled:opacity-50"
            >
              <Link2 className="h-3.5 w-3.5" />
              Reconnect
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => setConfirmOpen(true)}
              className="inline-flex h-10 items-center gap-1.5 rounded-full border border-destructive/40 bg-destructive/5 px-4 text-xs font-bold text-destructive transition hover:bg-destructive/10 disabled:opacity-50"
            >
              <Unplug className="h-3.5 w-3.5" />
              Disconnect
            </button>
          </div>
        </div>
      ) : null}

      {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

      {confirmOpen ? <DisconnectDialog busy={busy} onCancel={() => setConfirmOpen(false)} onConfirm={confirmDisconnect} /> : null}
    </section>
  );
}

// Disconnecting is not just "forget the token" — it takes the Xero-owned rows
// with it, and claims keep their history but lose the links. The previous
// system spelled that out before asking, which is the only way the choice is
// an informed one.
function DisconnectDialog({
  busy,
  onCancel,
  onConfirm,
}: {
  busy: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  useBodyScrollLock();

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-lg overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="text-xl font-black text-foreground">Disconnect Xero?</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Approved claims can no longer be pushed as bills, and the chart of accounts stops being
          kept in step.
        </p>

        <div className="mt-4 rounded-2xl border border-amber-300/50 bg-amber-50/70 p-4 text-sm dark:bg-amber-500/10">
          <p className="font-semibold text-amber-900 dark:text-amber-200">These stay, but lose their link</p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-amber-900/90 dark:text-amber-200/90">
            <li>Claims keep their history; their chart-of-account link stops resolving</li>
            <li>Receipts held in Xero Files can no longer be opened from here</li>
          </ul>
        </div>

        <p className="mt-3 text-xs text-muted-foreground">
          You can reconnect the same Xero org at any time and re-import the chart of accounts.
        </p>

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-destructive text-sm font-bold text-destructive-foreground transition hover:opacity-90 disabled:opacity-60"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Unplug className="h-4 w-4" />}
            Disconnect
          </button>
          <button
            type="button"
            onClick={onCancel}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
