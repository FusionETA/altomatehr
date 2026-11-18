import { useCallback, useEffect, useState } from "react";
import { LoaderCircle, RefreshCw } from "lucide-react";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { ApiError } from "@/shared/lib/api-client";
import { getLoginBlocks, unblockLogin, type LoginBlock } from "../api";

// Who can't sign in right now after too many wrong passwords — an account
// (5 in a row) or a whole network (10 from one IP) — and lifting one early for
// someone who really did forget their password.
//
// Not cached: a block lasts 15 minutes, so an old answer is a wrong one. Read
// fresh on every visit and on Refresh.
export function LoginBlocksCard() {
  const [blocks, setBlocks] = useState<LoginBlock[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setBlocks(await getLoginBlocks());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load blocked sign-ins.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function unblock(block: LoginBlock) {
    const who = describe(block);
    const ok = await confirm({
      title: block.kind === "ACCOUNT" ? `Unlock ${who}?` : `Unblock ${who}?`,
      message:
        block.kind === "ACCOUNT"
          ? "They can sign in again straight away. Make sure it's really them asking — a lock is also what stops someone guessing their password."
          : "Everyone on this network can sign in again straight away. If it's blocked again today, it's for an hour.",
      confirmLabel: block.kind === "ACCOUNT" ? "Unlock" : "Unblock",
    });
    if (!ok) return;

    setBusyKey(block.key);
    setNotice(null);
    setError(null);
    try {
      await unblockLogin(block);
      setNotice(`${who} can sign in again.`);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setNotice(`${who} was no longer blocked.`);
      } else {
        setError(err instanceof Error ? err.message : "Could not unblock.");
      }
    } finally {
      setBusyKey(null);
      void load();
    }
  }

  return (
    <section className="rounded-[28px] border border-border/60 bg-card p-5 shadow-ambient sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-lg font-black text-foreground">Blocked sign-ins</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            An account is locked for 15 minutes after 5 wrong passwords. A network (IP) is blocked
            for 15 minutes after 10, or an hour if it was blocked earlier the same day. Lifting one is
            recorded in the activity log and the alert chat.
          </p>
        </div>
        <button
          type="button"
          onClick={() => {
            setNotice(null);
            void load();
          }}
          disabled={loading}
          className="inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-60"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} aria-hidden />
          Refresh
        </button>
      </div>

      {notice ? <p className="mt-3 text-sm font-medium text-foreground">{notice}</p> : null}
      {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

      {loading && blocks === null ? (
        <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
          <LoaderCircle className="h-4 w-4 animate-spin" /> Loading blocked sign-ins…
        </div>
      ) : (blocks ?? []).length === 0 ? (
        <p className="py-10 text-center text-sm text-muted-foreground">
          {error ? "" : "Nobody is blocked from signing in right now."}
        </p>
      ) : (
        <div className="mt-4 overflow-x-auto rounded-2xl border border-border/50">
          <table className="w-full min-w-[720px] text-sm">
            <thead>
              <tr className="border-b border-border/60 text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                <th className="px-4 py-2.5 font-semibold">Blocked</th>
                <th className="px-4 py-2.5 font-semibold">Why</th>
                <th className="px-4 py-2.5 font-semibold">Until</th>
                <th className="px-4 py-2.5 text-right font-semibold">Action</th>
              </tr>
            </thead>
            <tbody>
              {(blocks ?? []).map((block) => (
                <tr key={`${block.kind}:${block.key}`} className="border-b border-border/30 align-top last:border-0">
                  <td className="px-4 py-3">
                    <span className="mr-2 whitespace-nowrap rounded-full border border-border/70 px-2 py-0.5 text-[11px] font-semibold text-muted-foreground">
                      {block.kind === "ACCOUNT" ? "Account" : "Network"}
                    </span>
                    {block.kind === "ACCOUNT" ? (
                      <>
                        <span className="font-semibold text-foreground">{block.name ?? block.email}</span>
                        {block.name ? (
                          <span className="block text-xs text-muted-foreground">{block.email}</span>
                        ) : null}
                        <span className="block text-xs text-muted-foreground">
                          {block.name ? (block.company ?? "No company") : "No AltomateHR account has this email"}
                        </span>
                      </>
                    ) : (
                      <span className="font-semibold tabular-nums text-foreground">{block.ip}</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {block.kind === "ACCOUNT" ? (
                      "5 wrong passwords in a row"
                    ) : (
                      <>
                        10 wrong passwords
                        {block.emailsTried.length > 0 ? (
                          <span className="block text-xs">
                            Tried: {block.emailsTried.slice(0, 5).join(", ")}
                            {block.emailsTried.length > 5 ? ` and ${block.emailsTried.length - 5} more` : ""}
                          </span>
                        ) : null}
                      </>
                    )}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-muted-foreground">
                    {utc(block.untilUtc).toLocaleTimeString("en-MY", { hour: "numeric", minute: "2-digit" })}
                    <span className="block text-xs">{minutesLeft(block.untilUtc)}</span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex justify-end">
                      <button
                        type="button"
                        onClick={() => void unblock(block)}
                        disabled={busyKey !== null}
                        className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-60"
                      >
                        {busyKey === block.key ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
                        {block.kind === "ACCOUNT" ? "Unlock" : "Unblock"}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {confirmDialog}
    </section>
  );
}

function describe(block: LoginBlock): string {
  if (block.kind === "IP") return block.ip ?? "this network";
  return block.name ?? block.email ?? "this account";
}

function minutesLeft(untilUtc: string): string {
  const minutes = Math.max(1, Math.ceil((utc(untilUtc).getTime() - Date.now()) / 60000));
  return minutes === 1 ? "1 minute left" : `${minutes} minutes left`;
}

// Read as UTC even if the server leaves off the trailing Z.
function utc(iso: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`);
}
