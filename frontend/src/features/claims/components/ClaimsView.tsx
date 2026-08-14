import { useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import { Pencil, Plus, Search } from "lucide-react";
import { getMyClaims, type Claim } from "../api";
import {
  claimMatchesStatus,
  claimStatusLabels,
  visibleClaimStatuses,
  type ClaimStatusFilter,
} from "../lib/claim-status";
import {
  formatCurrency,
  formatMonthYear,
  formatShortDate,
} from "../lib/claim-formatters";
import { ClaimStatusBadge } from "./ClaimStatusBadge";
import { OverLimitBadge } from "./OverLimitBadge";
import { CLAIMS_PAGE_SIZE, PaginationControls } from "./PaginationControls";
import { NewClaimModal } from "./NewClaimModal";
import { ViewReceiptButton } from "./ViewReceiptButton";
import { ClaimDetailsModal } from "./ClaimDetailsModal";
import { getAccounts, getProjects } from "@/features/settings/api";

export function ClaimsView() {
  const [claims, setClaims] = useState<Claim[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<ClaimStatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedClaim, setSelectedClaim] = useState<Claim | null>(null);
  const [editingClaim, setEditingClaim] = useState<Claim | null>(null);
  const [projectNames, setProjectNames] = useState<Map<string, string>>(new Map());
  const [accountLabels, setAccountLabels] = useState<Map<string, string>>(new Map());

  useEffect(() => {
    getMyClaims()
      .then(setClaims)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  // Claims carry project/account *ids*; resolve them to names from the org's
  // settings. Best-effort — if it fails, ids just don't render as labels.
  useEffect(() => {
    Promise.all([getProjects(), getAccounts()])
      .then(([projects, accounts]) => {
        setProjectNames(new Map(projects.map((p) => [p.id, p.name])));
        setAccountLabels(new Map(accounts.map((a) => [a.id, `${a.code} ${a.name}`])));
      })
      .catch(() => {
        /* labels stay empty */
      });
  }, []);

  const claimMeta = (claim: Claim) => {
    const proj = claim.projectId ? projectNames.get(claim.projectId) : undefined;
    const acc = claim.chartOfAccountId ? accountLabels.get(claim.chartOfAccountId) : undefined;
    return [proj, acc].filter(Boolean).join(" · ");
  };
  const projectLabel = (claim: Claim) =>
    claim.projectId ? projectNames.get(claim.projectId) ?? "Not assigned" : "Not assigned";
  const accountLabel = (claim: Claim) =>
    claim.chartOfAccountId ? accountLabels.get(claim.chartOfAccountId) ?? "Not assigned" : "Not assigned";

  function handleClaimKeyDown(event: KeyboardEvent, claim: Claim) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setSelectedClaim(claim);
    }
  }

  function canEditClaim(claim: Claim) {
    return claim.status === "SUBMITTED" || claim.status === "PENDING";
  }

  function replaceClaim(updatedClaim: Claim) {
    setClaims((current) => current.map((claim) => (claim.id === updatedClaim.id ? updatedClaim : claim)));
    setSelectedClaim((current) => (current?.id === updatedClaim.id ? updatedClaim : current));
  }

  function editFooter(claim: Claim) {
    if (!canEditClaim(claim)) return null;

    return (
      <button
        type="button"
        onClick={() => {
          setEditingClaim(claim);
          setSelectedClaim(null);
        }}
        className="inline-flex h-12 items-center justify-center gap-2 rounded-[18px] bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:bg-primary/90"
      >
        <Pencil className="h-4 w-4" />
        Edit claim
      </button>
    );
  }

  const filteredClaims = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();

    return claims.filter((claim) => {
      const matchesStatus = claimMatchesStatus(claim, status);
      const proj = claim.projectId ? projectNames.get(claim.projectId) : "";
      const acc = claim.chartOfAccountId ? accountLabels.get(claim.chartOfAccountId) : "";
      const matchesQuery =
        query.length === 0
          ? true
          : [claim.claimNumber, claim.title, claim.category, claim.status, proj, acc]
              .filter(Boolean)
              .join(" ")
              .toLowerCase()
              .includes(query);

      return matchesStatus && matchesQuery;
    });
  }, [claims, searchTerm, status, projectNames, accountLabels]);

  useEffect(() => {
    setPage(1);
  }, [searchTerm, status]);

  const totalPages = Math.max(1, Math.ceil(filteredClaims.length / CLAIMS_PAGE_SIZE));

  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const paginatedClaims = useMemo(() => {
    const start = (page - 1) * CLAIMS_PAGE_SIZE;
    return filteredClaims.slice(start, start + CLAIMS_PAGE_SIZE);
  }, [filteredClaims, page]);

  const hasActiveFilters = status !== "ALL" || searchTerm.trim().length > 0;

  return (
    <>
      <div className="no-scrollbar mb-4 overflow-x-auto pb-0.5 md:hidden">
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setStatus("ALL")}
            className={`shrink-0 rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
              status === "ALL"
                ? "border-primary bg-primary text-primary-foreground"
                : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
            }`}
          >
            All
          </button>
          {visibleClaimStatuses.map((claimStatus) => (
            <button
              key={claimStatus}
              type="button"
              onClick={() => setStatus(claimStatus)}
              className={`shrink-0 rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
                status === claimStatus
                  ? "border-primary bg-primary text-primary-foreground"
                  : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
              }`}
            >
              {claimStatusLabels[claimStatus]}
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-4 sm:space-y-6">
        <section className="hidden rounded-[28px] border border-border/70 bg-card/90 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm md:block">
          <div className="space-y-4 px-5 pb-5 pt-3 sm:space-y-5 sm:p-6">
            <div className="relative w-full max-w-sm">
              <Search className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <input
                value={searchTerm}
                onChange={(event) => setSearchTerm(event.target.value)}
                placeholder="Search by claim, title, or account"
                className="h-12 w-full rounded-2xl border border-border bg-card px-4 py-2 pl-10 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
              />
            </div>

            <div className="no-scrollbar flex gap-2 overflow-x-auto">
              <button
                type="button"
                onClick={() => setStatus("ALL")}
                className={`shrink-0 rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
                  status === "ALL"
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                }`}
              >
                All
              </button>
              {visibleClaimStatuses.map((claimStatus) => (
                <button
                  key={claimStatus}
                  type="button"
                  onClick={() => setStatus(claimStatus)}
                  className={`shrink-0 rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
                    status === claimStatus
                      ? "border-primary bg-primary text-primary-foreground"
                      : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                  }`}
                >
                  {claimStatusLabels[claimStatus]}
                </button>
              ))}
            </div>

            <div className="flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
              <p>
                Showing <span className="font-semibold text-foreground">{filteredClaims.length}</span>{" "}
                of <span className="font-semibold text-foreground">{claims.length}</span> claims
              </p>
              {hasActiveFilters ? (
                <button
                  type="button"
                  className="w-fit rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                  onClick={() => {
                    setStatus("ALL");
                    setSearchTerm("");
                  }}
                >
                  Clear filters
                </button>
              ) : null}
            </div>
          </div>
        </section>

        <div className="text-sm text-muted-foreground md:hidden">
          <p>
            Showing <span className="font-semibold text-foreground">{filteredClaims.length}</span> of{" "}
            <span className="font-semibold text-foreground">{claims.length}</span> claims
          </p>
        </div>

        {loading ? (
          <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 text-sm text-muted-foreground shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm">
            Loading claims...
          </section>
        ) : null}

        {error ? (
          <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : null}

        {!loading && !error && filteredClaims.length === 0 ? (
          <section className="rounded-[28px] border border-border/70 bg-card/90 p-8 text-center text-sm text-muted-foreground shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm">
            No claims match the selected status.
          </section>
        ) : null}

        {!loading && !error && filteredClaims.length > 0 ? (
          <div className="grid gap-3 sm:gap-4 md:hidden">
            {paginatedClaims.map((claim) => (
              <article
                key={claim.id}
                role="button"
                tabIndex={0}
                onClick={() => setSelectedClaim(claim)}
                onKeyDown={(event) => handleClaimKeyDown(event, claim)}
                className="cursor-pointer rounded-[28px] border border-border/70 bg-card/90 p-4 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm transition hover:border-primary/40 focus-visible:border-primary/50 focus-visible:outline-none sm:p-5"
              >
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      {claim.claimNumber}
                    </p>
                    <p className="mt-1 text-base font-black">{claim.title}</p>
                    <p className="text-xs text-muted-foreground">{claim.category}</p>
                    {claimMeta(claim) ? (
                      <p className="mt-1 text-xs text-muted-foreground">{claimMeta(claim)}</p>
                    ) : null}
                  </div>
                  <div className="flex shrink-0 flex-col items-end gap-1.5">
                    <ClaimStatusBadge status={claim.status} />
                    {claim.exceedsLimit ? <OverLimitBadge /> : null}
                  </div>
                </div>
                <div className="mt-4 grid grid-cols-2 gap-3">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      Submitted
                    </p>
                    <p className="mt-1 text-sm font-semibold">
                      {formatShortDate(claim.submittedAt || claim.spentAt)}
                    </p>
                  </div>
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      Amount
                    </p>
                    <p className="mt-1 text-sm font-semibold">
                      {formatCurrency(claim.amount, claim.currency)}
                    </p>
                  </div>
                </div>
                {claim.reviewNotes ? (
                  <div className="mt-4 rounded-[20px] border border-border/70 bg-card/94 p-3.5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm">
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      Reviewer note
                    </p>
                    <p className="mt-2 text-xs leading-6 text-muted-foreground">{claim.reviewNotes}</p>
                  </div>
                ) : null}
                {claim.receiptUrl ? (
                  <ViewReceiptButton
                    receiptUrl={claim.receiptUrl}
                    className="mt-4 inline-flex rounded-full bg-muted px-3 py-1.5 text-xs font-semibold text-primary transition hover:bg-secondary"
                  />
                ) : null}
              </article>
            ))}
          </div>
        ) : null}

        {!loading && !error && filteredClaims.length > 0 ? (
          <section className="hidden rounded-[28px] border border-border/70 bg-card/90 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm md:block">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[880px] caption-bottom text-sm">
                <thead>
                  <tr className="border-b border-border/60">
                    <th className="h-12 px-6 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                      Claim
                    </th>
                    <th className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                      Category
                    </th>
                    <th className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                      Submitted
                    </th>
                    <th className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                      Claims run
                    </th>
                    <th className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                      Amount
                    </th>
                    <th className="h-12 px-6 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                      Status
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {paginatedClaims.map((claim) => (
                    <tr
                      key={claim.id}
                      tabIndex={0}
                      onClick={() => setSelectedClaim(claim)}
                      onKeyDown={(event) => handleClaimKeyDown(event, claim)}
                      className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                    >
                      <td className="p-4 pl-6 align-middle">
                        <div>
                          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                            {claim.claimNumber}
                          </p>
                          <p className="mt-1 font-bold">{claim.title}</p>
                          {claimMeta(claim) ? (
                            <p className="mt-1 text-sm text-muted-foreground">{claimMeta(claim)}</p>
                          ) : null}
                          {claim.reviewNotes ? (
                            <p className="mt-2 text-sm text-muted-foreground">
                              Reviewer note: {claim.reviewNotes}
                            </p>
                          ) : null}
                          {claim.receiptUrl ? (
                            <ViewReceiptButton
                              receiptUrl={claim.receiptUrl}
                              className="mt-2 inline-flex text-sm font-semibold text-primary hover:underline"
                            />
                          ) : null}
                        </div>
                      </td>
                      <td className="p-4 align-middle">{claim.category}</td>
                      <td className="p-4 align-middle">
                        {formatShortDate(claim.submittedAt || claim.spentAt)}
                      </td>
                      <td className="p-4 align-middle">
                        {formatMonthYear(claim.submittedAt || claim.spentAt)}
                      </td>
                      <td className="p-4 align-middle">
                        {formatCurrency(claim.amount, claim.currency)}
                      </td>
                      <td className="p-4 pr-6 align-middle">
                        <div className="flex flex-col items-start gap-1.5">
                          <ClaimStatusBadge status={claim.status} />
                          {claim.exceedsLimit ? <OverLimitBadge /> : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <PaginationControls
              className="flex flex-col gap-3 px-5 pb-5 sm:flex-row sm:items-center sm:justify-between sm:px-6 sm:pb-6"
              currentPage={page}
              totalItems={filteredClaims.length}
              onPageChange={setPage}
            />
          </section>
        ) : null}

        {!loading && !error && filteredClaims.length > 0 ? (
          <div className="md:hidden">
            <PaginationControls
              className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
              currentPage={page}
              totalItems={filteredClaims.length}
              onPageChange={setPage}
            />
          </div>
        ) : null}
      </div>

      <button
        type="button"
        aria-label="New claim"
        onClick={() => setCreateOpen(true)}
        className="fixed bottom-32 right-5 z-40 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-[0_18px_48px_rgba(76,26,134,0.10)] transition-transform hover:scale-105 active:scale-95 lg:bottom-8 lg:right-8"
      >
        <Plus className="h-6 w-6" />
      </button>

      {createOpen ? (
        <NewClaimModal
          onClose={() => setCreateOpen(false)}
          onCreated={(claim) => setClaims((current) => [claim, ...current])}
        />
      ) : null}

      {editingClaim ? (
        <NewClaimModal
          editingClaim={editingClaim}
          onClose={() => setEditingClaim(null)}
          onCreated={(claim) => setClaims((current) => [claim, ...current])}
          onUpdated={(claim) => {
            replaceClaim(claim);
            setEditingClaim(null);
          }}
        />
      ) : null}

      {selectedClaim ? (
        <ClaimDetailsModal
          claim={selectedClaim}
          accountLabel={accountLabel(selectedClaim)}
          projectLabel={projectLabel(selectedClaim)}
          onClose={() => setSelectedClaim(null)}
          footer={editFooter(selectedClaim)}
        />
      ) : null}
    </>
  );
}
