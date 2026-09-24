import { useEffect, useRef, useState } from "react";
import { Building2, Check, ChevronsUpDown, LoaderCircle, Plus } from "lucide-react";
import { getOrgs, switchOrg } from "@/features/auth/api";
import { getOrganization } from "@/features/settings/api";
import { CreateCompanyDialog } from "@/features/settings/components/CreateCompanyDialog";
import { useCachedQuery } from "@/shared/lib/use-cached-query";

// The company list and the switch itself, shared by the header pill and the
// account-menu list the employee portal shows on phones.
function useOrgSwitch(listEnabled: boolean) {
  const [switchingId, setSwitchingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const currentQuery = useCachedQuery("/organizations/current", getOrganization);
  const orgsQuery = useCachedQuery("/auth/orgs", getOrgs, { enabled: listEnabled });

  const currentId = currentQuery.data?.id ?? null;

  async function choose(id: string, onSame?: () => void) {
    if (id === currentId) {
      onSame?.();
      return;
    }
    setSwitchingId(id);
    setError(null);
    try {
      await switchOrg(id);
      // The refresh cookie now points at the new org; a reload boots into it
      // with every query re-fetching cleanly.
      window.location.reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not switch company.");
      setSwitchingId(null);
    }
  }

  return {
    currentId,
    currentName: currentQuery.data?.name ?? "Company",
    orgs: orgsQuery.data ?? [],
    loading: orgsQuery.loading,
    switchingId,
    error,
    choose,
  };
}

// The company switcher. Shows the active company and, on open, every company the
// account belongs to — picking one re-mints the session for it and reloads so
// every screen re-fetches against the new org. "New company" also lives here
// (the account is made its Owner), mirroring the monolith's switch-company menu.
//
// The employee portal needs this too, not just the admin shell: the legacy
// system gave a person a SEPARATE LOGIN per company, so switching meant signing
// in as someone else. v2 merges those into one account, which makes this the
// only route to a second company — and 11 people, all non-admins, belong to
// more than one.
export function OrgSwitcher({
  // Employees shouldn't be able to spin up a company from their own portal;
  // that is an admin action and the button sits one stray tap away from the
  // list they came here for.
  allowCreate = true,
  // Render nothing for someone who belongs to exactly one company, so the
  // single-company majority gain no control that only ever reloads them onto
  // the org they are already in.
  hideWhenSingle = false,
}: { allowCreate?: boolean; hideWhenSingle?: boolean } = {}) {
  const [open, setOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const ref = useRef<HTMLDivElement | null>(null);

  // Normally fetched only once the menu is opened — the list isn't needed to
  // render the trigger, and most sessions never open it. When the caller wants
  // it hidden for single-company accounts, the count has to be known before
  // the first paint, so fetch eagerly in that case.
  const { currentId, currentName, orgs, loading, switchingId, error, choose } =
    useOrgSwitch(open || hideWhenSingle);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: PointerEvent) {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  // After every hook, never before — an early return above them would change
  // the hook order between renders.
  if (hideWhenSingle && orgs.length <= 1) return null;

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        aria-label="Switch company"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        className="flex max-w-[11rem] items-center gap-2 rounded-full border border-border/60 bg-card/90 px-3 py-2 text-left shadow-ambient transition hover:bg-muted sm:max-w-[15rem]"
      >
        <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
          <Building2 className="h-3.5 w-3.5" />
        </span>
        <span className="min-w-0 truncate text-sm font-bold text-foreground">{currentName}</span>
        <ChevronsUpDown className="h-4 w-4 shrink-0 text-muted-foreground" />
      </button>

      {open ? (
        <div className="absolute left-0 top-[calc(100%+0.6rem)] z-50 w-72 overflow-hidden rounded-2xl border border-border/70 bg-card/98 p-2 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl">
          <p className="px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            Switch company
          </p>

          {loading ? (
            <div className="flex items-center gap-2 px-3 py-3 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" /> Loading…
            </div>
          ) : (
            <ul className="max-h-64 overflow-y-auto">
              {orgs.map((o) => {
                const active = o.organizationId === currentId;
                return (
                  <li key={o.organizationId}>
                    <button
                      type="button"
                      onClick={() => void choose(o.organizationId, () => setOpen(false))}
                      disabled={switchingId !== null}
                      className={`flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm transition hover:bg-muted disabled:opacity-60 ${
                        active ? "font-bold text-foreground" : "font-semibold text-muted-foreground"
                      }`}
                    >
                      <span className="min-w-0 flex-1 truncate">
                        {o.name}
                        <span className="block text-xs font-normal capitalize text-muted-foreground">
                          {o.role.toLowerCase()}
                        </span>
                      </span>
                      {switchingId === o.organizationId ? (
                        <LoaderCircle className="h-4 w-4 shrink-0 animate-spin text-primary" />
                      ) : active ? (
                        <Check className="h-4 w-4 shrink-0 text-primary" />
                      ) : null}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}

          {error ? <p className="px-3 py-1 text-xs font-medium text-destructive">{error}</p> : null}

          <div className={`mt-1 border-t border-border/60 pt-1 ${allowCreate ? "" : "hidden"}`}>
            <button
              type="button"
              onClick={() => {
                setOpen(false);
                setCreating(true);
              }}
              className="flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-foreground transition hover:bg-muted"
            >
              <Plus className="h-4 w-4 shrink-0" />
              New company
            </button>
          </div>
        </div>
      ) : null}

      {creating ? <CreateCompanyDialog onClose={() => setCreating(false)} /> : null}
    </div>
  );
}

// The same switch as rows inside an existing menu. On a phone the header pill
// took a third of the bar — pushing the page title into a clipped sliver — so
// the employee portal hides it there and offers the companies in its account
// menu instead. Renders nothing for a single-company account.
export function OrgSwitcherMenuList({ className = "" }: { className?: string }) {
  const { currentId, orgs, switchingId, error, choose } = useOrgSwitch(true);
  if (orgs.length <= 1) return null;

  return (
    <div className={`mt-1 border-t border-border/60 pt-1 ${className}`}>
      <p className="px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
        Switch company
      </p>
      <ul className="max-h-48 overflow-y-auto">
        {orgs.map((o) => {
          const active = o.organizationId === currentId;
          return (
            <li key={o.organizationId}>
              <button
                type="button"
                onClick={() => void choose(o.organizationId)}
                disabled={switchingId !== null || active}
                className={`flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm transition hover:bg-muted disabled:cursor-default ${
                  active ? "font-bold text-foreground" : "font-semibold text-muted-foreground"
                }`}
              >
                <Building2 className="h-4 w-4 shrink-0" />
                <span className="min-w-0 flex-1 truncate">{o.name}</span>
                {switchingId === o.organizationId ? (
                  <LoaderCircle className="h-4 w-4 shrink-0 animate-spin text-primary" />
                ) : active ? (
                  <Check className="h-4 w-4 shrink-0 text-primary" />
                ) : null}
              </button>
            </li>
          );
        })}
      </ul>
      {error ? <p className="px-3 py-1 text-xs font-medium text-destructive">{error}</p> : null}
    </div>
  );
}
