import { ArrowRight, FileText, MapPin, ReceiptText, X } from "lucide-react";
import type { ReactNode } from "react";
import type { Claim } from "../api";
import { formatCurrency, formatShortDate } from "../lib/claim-formatters";
import { ClaimStatusBadge } from "./ClaimStatusBadge";
import { OverLimitBadge } from "./OverLimitBadge";
import { ViewReceiptButton } from "./ViewReceiptButton";

export function ClaimDetailsModal({
  claim,
  accountLabel,
  projectLabel,
  employeeLabel,
  onClose,
  footer,
}: {
  claim: Claim;
  accountLabel?: string;
  projectLabel?: string;
  employeeLabel?: string;
  onClose: () => void;
  footer?: ReactNode;
}) {
  const isMileage = claim.claimType === "MILEAGE";
  const paidWith = claim.paymentType === "COMPANY" ? "Company money" : "My own money";
  const supportingDocuments = claim.supportingDocumentUrls ?? [];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="nice-scrollbar max-h-[90vh] w-full max-w-[720px] overflow-y-auto rounded-[28px] border border-white/40 bg-card/95 p-6 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl sm:p-8">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              {claim.claimNumber}
            </p>
            <h2 className="mt-1 truncate text-2xl font-black text-foreground">{claim.title}</h2>
          </div>
          <button
            type="button"
            aria-label="Close claim details"
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <section className="mt-5 rounded-[22px] border border-border/70 bg-surface-low/60 p-5">
          <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Amount
              </p>
              <p className="mt-2 text-3xl font-black leading-none text-foreground">
                {formatCurrency(claim.amount, claim.currency)}
              </p>
              <div className="mt-4 flex flex-wrap items-center gap-2">
                <ClaimStatusBadge status={claim.status} />
                {claim.exceedsLimit ? <OverLimitBadge /> : null}
              </div>
            </div>
            <div className="grid grid-cols-2 gap-x-6 gap-y-3 sm:min-w-[260px]">
              <Fact label="Type" value={isMileage ? "Mileage" : "Expense"} />
              <Fact label="Paid with" value={paidWith} />
              <Fact label="Spent" value={formatShortDate(claim.spentAt)} />
              <Fact label="Submitted" value={formatShortDate(claim.submittedAt)} />
            </div>
          </div>
        </section>

        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <InfoPanel title="Claim Info" icon={<ReceiptText className="h-4 w-4" />}>
            <InfoRow label="Employee" value={employeeLabel ?? claim.employeeEmail ?? claim.employeeId} />
            <InfoRow label="Category" value={claim.category} />
            <InfoRow label="Account" value={accountLabel ?? "Not assigned"} />
            <InfoRow label="Project" value={projectLabel ?? "Not assigned"} />
          </InfoPanel>

          <InfoPanel title="Payment" icon={<FileText className="h-4 w-4" />}>
            <InfoRow label="Source" value={paidWith} />
            <InfoRow label="Company bank" value={claim.payViaAccountId ?? "Not required"} />
            <InfoRow label="Spending at" value={claim.spendingAt ?? "Not provided"} />
            <InfoRow label="Spending with" value={claim.spendingWith ?? "Not provided"} />
          </InfoPanel>
        </div>

        {isMileage ? (
          <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
            <div className="flex items-center gap-2 text-primary">
              <MapPin className="h-4 w-4" />
              <p className="text-xs font-semibold uppercase tracking-[0.18em]">Mileage Route</p>
            </div>
            <div className="mt-4 grid gap-4 sm:grid-cols-[1fr_auto_1fr] sm:items-center">
              <RoutePoint label="From" value={claim.mileageOriginAddress ?? "Not set"} />
              <div className="hidden h-10 w-10 items-center justify-center rounded-full bg-secondary text-primary sm:flex">
                <ArrowRight className="h-4 w-4" />
              </div>
              <RoutePoint label="To" value={claim.mileageDestinationAddress ?? "Not set"} />
            </div>
            <div className="mt-4 grid grid-cols-2 gap-4 border-t border-border/60 pt-4">
              <Fact label="Distance" value={formatDistance(claim)} />
              <Fact label="Rate used" value={formatMileageRate(claim)} />
            </div>
          </section>
        ) : null}

        <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            Business context
          </p>
          <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">
            {claim.description || "No description provided."}
          </p>
        </section>

        {claim.reviewNotes ? (
          <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Reviewer note
            </p>
            <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">
              {claim.reviewNotes}
            </p>
          </section>
        ) : null}

        <div className="mt-5 flex flex-col gap-4 border-t border-border/60 pt-5 sm:flex-row sm:items-start sm:justify-between">
          <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Main receipt
              </span>
              {claim.receiptUrl ? (
                <ViewReceiptButton
                  receiptUrl={claim.receiptUrl}
                  label="View main receipt"
                  className="inline-flex rounded-full bg-muted px-4 py-2 text-sm font-semibold text-primary transition hover:bg-secondary"
                />
              ) : (
                <span className="text-sm text-muted-foreground">No main receipt attached</span>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Supporting
              </span>
              {supportingDocuments.length > 0 ? (
                supportingDocuments.map((url, index) => (
                  <ViewReceiptButton
                    key={url}
                    receiptUrl={url}
                    label={supportingDocuments.length === 1 ? "View supporting document" : `Supporting ${index + 1}`}
                    className="inline-flex rounded-full bg-muted px-4 py-2 text-sm font-semibold text-primary transition hover:bg-secondary"
                  />
                ))
              ) : (
                <span className="text-sm text-muted-foreground">No supporting documents attached</span>
              )}
            </div>
          </div>
          {footer}
        </div>
      </div>
    </div>
  );
}

function Fact({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="min-w-0">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-1 break-words text-sm font-bold text-foreground">{value}</p>
    </div>
  );
}

function InfoPanel({
  title,
  icon,
  children,
}: {
  title: string;
  icon: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="rounded-[22px] border border-border/70 bg-card/70 p-5">
      <div className="mb-2 flex items-center gap-2 text-primary">
        {icon}
        <p className="text-xs font-semibold uppercase tracking-[0.18em]">{title}</p>
      </div>
      {children}
    </section>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex gap-4 border-b border-border/50 py-3 last:border-b-0">
      <p className="w-24 shrink-0 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </p>
      <p className="min-w-0 flex-1 break-words text-sm font-bold text-foreground">{value}</p>
    </div>
  );
}

function RoutePoint({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0 rounded-2xl bg-surface-low/70 p-4">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-1 break-words text-sm font-bold leading-5 text-foreground">{value}</p>
    </div>
  );
}

function formatDistance(claim: Claim) {
  if (claim.distance == null) return "Not set";
  const unit = claim.mileageUnitUsed === "MILE" ? "miles" : "km";
  return `${claim.distance.toFixed(2)} ${unit}`;
}

function formatMileageRate(claim: Claim) {
  if (claim.mileageRateUsed == null) return "Not set";
  const unit = claim.mileageUnitUsed === "MILE" ? "mile" : "km";
  return `${claim.currency} ${claim.mileageRateUsed.toFixed(4)} / ${unit}`;
}
