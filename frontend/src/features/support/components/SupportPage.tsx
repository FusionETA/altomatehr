import { useMemo, useState } from "react";
import { ArrowLeft, LifeBuoy, LoaderCircle } from "lucide-react";
import { enterSupport } from "@/features/auth/api";
import { SearchInput } from "@/shared/components/SearchInput";
import { TablePager } from "@/shared/components/TablePager";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { usePaged } from "@/shared/lib/use-paged";
import { getSupportOrganizations, SUPPORT_ORGS_PATH, type SupportOrganization } from "../api";
import { matchesSearch, planLabel } from "../lib/plan";
import { CreateCompanyCard } from "./CreateCompanyCard";
import { ManagePlanDialog } from "./ManagePlanDialog";

// Fusioneta support: every company on the platform, to step into as support,
// re-package, or provision. Superadmins only — the server refuses anyone else.
export function SupportPage({ onBack }: { onBack: () => void }) {
  const query = useCachedQuery(SUPPORT_ORGS_PATH, getSupportOrganizations);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<SupportOrganization | null>(null);
  const [enteringId, setEnteringId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const all = useMemo(() => query.data ?? [], [query.data]);
  const filtered = useMemo(() => all.filter((o) => matchesSearch(o, search)), [all, search]);
  const paged = usePaged(filtered, 10);

  async function enter(org: SupportOrganization) {
    setEnteringId(org.id);
    setError(null);
    try {
      await enterSupport(org.id);
      // The refresh cookie now points into that company as support; reload
      // into its admin portal with every screen re-fetching against it.
      window.location.assign(window.location.pathname);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not enter support mode.");
      setEnteringId(null);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="sticky top-0 z-30 border-b border-border/55 bg-background/82 backdrop-blur-xl">
        <div className="mx-auto flex w-full max-w-6xl items-center gap-3 px-4 py-4 sm:px-6 lg:px-8">
          <button
            type="button"
            onClick={onBack}
            aria-label="Back"
            className="flex h-10 w-10 items-center justify-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <ArrowLeft className="h-4 w-4" />
          </button>
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Fusioneta</p>
            <h1 className="flex items-center gap-2 truncate text-2xl font-black tracking-tight text-foreground">
              <LifeBuoy className="h-5 w-5 text-primary" /> Support
            </h1>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-6xl space-y-6 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        <section className="rounded-[28px] border border-border/60 bg-card p-5 shadow-ambient sm:p-6">
          <h2 className="text-lg font-black text-foreground">Companies</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Enter a company as support to act as its admin. Your actions show there as "System
            (Support)"; the real person is recorded internally.
          </p>

          <div className="mt-4 space-y-1.5">
            <SearchInput value={search} onChange={setSearch} placeholder="Search by company, owner name or email" />
            <p className="text-xs text-muted-foreground">
              {filtered.length} of {all.length} companies
            </p>
          </div>

          {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

          {query.loading ? (
            <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" /> Loading companies…
            </div>
          ) : query.error ? (
            <p className="py-10 text-sm font-medium text-destructive">{query.error}</p>
          ) : filtered.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">No companies match.</p>
          ) : (
            <>
              <div className="mt-4 overflow-x-auto rounded-2xl border border-border/50">
                <table className="w-full min-w-[760px] text-sm">
                  <thead>
                    <tr className="border-b border-border/60 text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                      <th className="px-4 py-2.5 font-semibold">Company</th>
                      <th className="px-4 py-2.5 font-semibold">Owner</th>
                      <th className="px-4 py-2.5 text-right font-semibold">Employees</th>
                      <th className="px-4 py-2.5 font-semibold">Plan</th>
                      <th className="px-4 py-2.5 text-right font-semibold">Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {paged.pageItems.map((org) => (
                      <tr key={org.id} className="border-b border-border/30 last:border-0">
                        <td className="px-4 py-3 font-semibold text-foreground">{org.name}</td>
                        <td className="px-4 py-3 text-muted-foreground">
                          {org.ownerEmail ? (
                            <>
                              <span className="block text-foreground">{org.ownerName ?? org.ownerEmail}</span>
                              {org.ownerName ? <span className="block text-xs">{org.ownerEmail}</span> : null}
                            </>
                          ) : (
                            "No owner"
                          )}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{org.employeeCount}</td>
                        <td className="px-4 py-3">
                          <span className="rounded-full border border-border/70 px-2 py-0.5 text-xs font-semibold text-muted-foreground">
                            {planLabel(org)}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex justify-end gap-2">
                            <button
                              type="button"
                              onClick={() => setEditing(org)}
                              className="rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted"
                            >
                              Manage plan
                            </button>
                            <button
                              type="button"
                              onClick={() => void enter(org)}
                              disabled={enteringId !== null}
                              className="inline-flex items-center gap-1.5 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-60"
                            >
                              {enteringId === org.id ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
                              Enter as support
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="mt-3">
                <TablePager paged={paged} noun="organization" />
              </div>
            </>
          )}
        </section>

        <CreateCompanyCard onCreated={() => void query.refresh()} />
      </main>

      {editing ? (
        <ManagePlanDialog
          org={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void query.refresh();
          }}
        />
      ) : null}
    </div>
  );
}
