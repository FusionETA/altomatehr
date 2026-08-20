import { type FormEvent, useEffect, useMemo, useRef, useState } from "react";
import {
  ArrowLeft,
  ArrowRight,
  Building2,
  LoaderCircle,
  MapPin,
  Receipt,
  TriangleAlert,
  Upload,
  Wallet,
  X,
} from "lucide-react";
import {
  createClaim,
  updateClaim,
  uploadClaimReceipt,
  type Claim,
  type CreateClaimRequest,
} from "../api";
import {
  getAccounts,
  getOrganization,
  getProjects,
  type ChartOfAccount,
  type Organization,
  type Project,
} from "@/features/settings/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

type FlowStep = "payment" | "type" | "receipt" | "form";
type ClaimType = "EXPENSE" | "MILEAGE";
type PaymentType = "PERSONAL" | "COMPANY";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";
const TEXTAREA =
  "min-h-[112px] w-full rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";
const LABEL = "text-sm font-semibold text-foreground";

export function NewClaimModal({
  onClose,
  onCreated,
  editingClaim,
  onUpdated,
}: {
  onClose: () => void;
  onCreated: (claim: Claim) => void;
  editingClaim?: Claim;
  onUpdated?: (claim: Claim) => void;
}) {
  const isEditing = Boolean(editingClaim);
  const [step, setStep] = useState<FlowStep>(isEditing ? "form" : "payment");
  const [claimType, setClaimType] = useState<ClaimType | null>(
    editingClaim ? toClaimType(editingClaim.claimType) : null,
  );
  const [paymentType, setPaymentType] = useState<PaymentType | null>(
    editingClaim ? toPaymentType(editingClaim.paymentType) : null,
  );
  const [receiptFile, setReceiptFile] = useState<File | null>(null);
  const [supportingFiles, setSupportingFiles] = useState<File[]>([]);

  useEffect(() => {
    const scrollY = window.scrollY;
    const { overflow, position, top, width } = document.body.style;

    document.body.style.overflow = "hidden";
    document.body.style.position = "fixed";
    document.body.style.top = `-${scrollY}px`;
    document.body.style.width = "100%";

    return () => {
      document.body.style.overflow = overflow;
      document.body.style.position = position;
      document.body.style.top = top;
      document.body.style.width = width;
      window.scrollTo(0, scrollY);
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div
        className="flex max-h-[calc(100svh-2rem)] w-full max-w-[720px] flex-col overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl"
        style={{ minHeight: "min(620px, calc(100svh - 2rem))" }}
      >
        <ModalHeader onClose={onClose} isEditing={isEditing} />

        <div className="nice-scrollbar flex-1 overflow-y-auto px-6 pb-6 pt-0 sm:px-8 sm:pb-8">
          {step === "payment" && !isEditing ? (
            <PaymentStep
              onPick={(type) => {
                setPaymentType(type);
                setStep("type");
              }}
            />
          ) : null}

          {step === "type" && paymentType && !isEditing ? (
            <TypeStep
              onBack={() => {
                setPaymentType(null);
                setStep("payment");
              }}
              onPick={(type) => {
                setClaimType(type);
                setStep(type === "MILEAGE" ? "form" : "receipt");
              }}
            />
          ) : null}

          {step === "receipt" && claimType === "EXPENSE" && paymentType && !isEditing ? (
            <ReceiptStep
              receiptFile={receiptFile}
              setReceiptFile={setReceiptFile}
              onBack={() => {
                setClaimType(null);
                setStep("type");
              }}
              onContinue={() => setStep("form")}
            />
          ) : null}

          {step === "form" && claimType && paymentType ? (
            <ClaimDetailsForm
              claimType={claimType}
              paymentType={paymentType}
              receiptFile={receiptFile}
              supportingFiles={supportingFiles}
              setSupportingFiles={setSupportingFiles}
              onBack={() => setStep(claimType === "MILEAGE" ? "type" : "receipt")}
              onClose={onClose}
              onCreated={onCreated}
              editingClaim={editingClaim}
              onUpdated={onUpdated}
            />
          ) : null}
        </div>
      </div>
    </div>
  );
}

function ModalHeader({ onClose, isEditing }: { onClose: () => void; isEditing: boolean }) {
  return (
    <div className="mb-6 flex shrink-0 items-start justify-between gap-4 border-b border-border/60 p-6 pb-4 sm:p-8 sm:pb-4">
      <div>
        <h2 className="text-2xl font-black text-foreground">
          {isEditing ? "Edit claim" : "Submit a claim"}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {isEditing
            ? "Update the details before this claim is approved."
            : "Choose how it was paid, then enter the claim details."}
        </p>
      </div>
      <button
        type="button"
        aria-label="Close"
        onClick={onClose}
        className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}

function PaymentStep({ onPick }: { onPick: (type: PaymentType) => void }) {
  return (
    <section className="space-y-5">
      <StepIntro
        eyebrow="Step 1 - How was this paid?"
        title="Who paid for this?"
        description="Pick whether you paid personally or it was already paid with company money."
      />
      <div className="grid gap-3 sm:grid-cols-2">
        <ChoiceCard
          icon={<Wallet className="h-5 w-5" />}
          title="My own money"
          description="You paid personally and need to be reimbursed."
          onClick={() => onPick("PERSONAL")}
        />
        <ChoiceCard
          icon={<Building2 className="h-5 w-5" />}
          title="Company money"
          description="Already paid from a company card, cash, or bank account."
          onClick={() => onPick("COMPANY")}
        />
      </div>
    </section>
  );
}

function TypeStep({
  onPick,
  onBack,
}: {
  onPick: (type: ClaimType) => void;
  onBack: () => void;
}) {
  return (
    <section className="space-y-5">
      <StepWithBack
        eyebrow="Step 2 - Pick a claim type"
        title="What kind of claim is this?"
        onBack={onBack}
      />
      <div className="grid gap-3 sm:grid-cols-2">
        <ChoiceCard
          icon={<Receipt className="h-5 w-5" />}
          title="Expense claim"
          description="Attach a receipt, then fill amount, account, and business context."
          onClick={() => onPick("EXPENSE")}
        />
        <ChoiceCard
          icon={<MapPin className="h-5 w-5" />}
          title="Mileage claim"
          description="Enter distance, origin, and destination. Amount is calculated from mileage rate."
          onClick={() => onPick("MILEAGE")}
        />
      </div>
    </section>
  );
}

function ReceiptStep({
  receiptFile,
  setReceiptFile,
  onBack,
  onContinue,
}: {
  receiptFile: File | null;
  setReceiptFile: (file: File | null) => void;
  onBack: () => void;
  onContinue: () => void;
}) {
  return (
    <section className="space-y-5">
      <StepWithBack eyebrow="Step 3 - Main document" title="Attach the main receipt" onBack={onBack} />
      <label
        htmlFor="claimReceipt"
        className="flex min-h-44 cursor-pointer flex-col items-center justify-center rounded-[28px] border border-dashed border-border/80 bg-surface-low/50 px-5 py-8 text-center transition hover:border-primary/50"
      >
        <div className="grid h-12 w-12 place-items-center rounded-2xl bg-primary/10 text-primary">
          <Upload className="h-5 w-5" />
        </div>
        <p className="mt-4 text-sm font-bold text-foreground">
          {receiptFile ? "Main receipt attached" : "Upload main receipt"}
        </p>
        <p className="mt-1 max-w-sm text-xs leading-5 text-muted-foreground">
          JPG, PNG, WEBP, HEIC, HEIF, or PDF up to 8 MB. This is the primary document for OCR later.
        </p>
        {receiptFile ? (
          <p className="mt-3 max-w-full truncate rounded-full bg-background px-3 py-1 text-xs font-medium text-foreground">
            {receiptFile.name}
          </p>
        ) : null}
      </label>
      <input
        id="claimReceipt"
        type="file"
        accept="image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf"
        className="sr-only"
        onChange={(event) => setReceiptFile(event.target.files?.[0] ?? null)}
      />
      <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button
          type="button"
          onClick={onContinue}
          className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
        >
          Skip and fill manually
        </button>
        <button
          type="button"
          onClick={onContinue}
          className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90"
        >
          Continue
          <ArrowRight className="h-4 w-4" />
        </button>
      </div>
    </section>
  );
}

function ClaimDetailsForm({
  claimType,
  paymentType,
  receiptFile,
  supportingFiles,
  setSupportingFiles,
  onBack,
  onClose,
  onCreated,
  editingClaim,
  onUpdated,
}: {
  claimType: ClaimType;
  paymentType: PaymentType;
  receiptFile: File | null;
  supportingFiles: File[];
  setSupportingFiles: (files: File[]) => void;
  onBack: () => void;
  onClose: () => void;
  onCreated: (claim: Claim) => void;
  editingClaim?: Claim;
  onUpdated?: (claim: Claim) => void;
}) {
  const isEditing = Boolean(editingClaim);
  const [title, setTitle] = useState(editingClaim?.title ?? "");
  const [description, setDescription] = useState(editingClaim?.description ?? "");
  const [amount, setAmount] = useState(editingClaim ? String(editingClaim.amount) : "");
  const [currency, setCurrency] = useState(editingClaim?.currency ?? "MYR");
  const [spentAt, setSpentAt] = useState(
    editingClaim?.spentAt ? editingClaim.spentAt.slice(0, 10) : new Date().toISOString().slice(0, 10),
  );
  const [projectId, setProjectId] = useState(editingClaim?.projectId ?? "");
  const [chartOfAccountId, setChartOfAccountId] = useState(editingClaim?.chartOfAccountId ?? "");
  const [payViaAccountId, setPayViaAccountId] = useState(editingClaim?.payViaAccountId ?? "");
  const [spendingAt, setSpendingAt] = useState(editingClaim?.spendingAt ?? "");
  const [spendingWith, setSpendingWith] = useState(editingClaim?.spendingWith ?? "");
  const [distance, setDistance] = useState(editingClaim?.distance ? String(editingClaim.distance) : "");
  const [mileageOriginAddress, setMileageOriginAddress] = useState(editingClaim?.mileageOriginAddress ?? "");
  const [mileageDestinationAddress, setMileageDestinationAddress] = useState(editingClaim?.mileageDestinationAddress ?? "");
  const [additionalSupportingFiles, setAdditionalSupportingFiles] = useState<File[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [accounts, setAccounts] = useState<ChartOfAccount[]>([]);
  const [organization, setOrganization] = useState<Organization | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    Promise.all([
      getProjects().catch(() => [] as Project[]),
      getAccounts().catch(() => [] as ChartOfAccount[]),
      getOrganization().catch(() => null),
    ]).then(([projectList, accountList, org]) => {
      if (!active) return;
      setProjects(projectList.filter((p) => !p.isArchived));
      setAccounts(accountList.filter((a) => !a.isArchived));
      setOrganization(org);
      if (!editingClaim && org?.defaultCurrency) setCurrency(org.defaultCurrency);
    });
    return () => {
      active = false;
    };
  }, [editingClaim]);

  const visibleAccounts = useMemo(
    () =>
      accounts.filter((account) =>
        claimType === "MILEAGE"
          ? account.allowMileageClaim
          : account.isSelectable && account.type !== "BANK",
      ),
    [accounts, claimType],
  );
  const bankAccounts = useMemo(
    () => accounts.filter((account) => account.type === "BANK"),
    [accounts],
  );
  const selectedAccount = visibleAccounts.find((a) => a.id === chartOfAccountId) ?? null;
  const mileageRate =
    claimType === "MILEAGE"
      ? selectedAccount?.mileageRate ?? organization?.defaultMileageRate ?? 0
      : 0;
  const distanceNumber = Number(distance);
  const computedMileageAmount =
    claimType === "MILEAGE" && distanceNumber > 0 && mileageRate > 0
      ? Math.round(distanceNumber * mileageRate * 100) / 100
      : 0;
  const liveAmount = claimType === "MILEAGE" ? computedMileageAmount : Number(amount);
  const overLimit =
    selectedAccount?.limitAmount != null &&
    Number.isFinite(liveAmount) &&
    liveAmount > selectedAccount.limitAmount;
  const canSubmit =
    Boolean(title.trim()) &&
    Boolean(description.trim()) &&
    Boolean(spentAt) &&
    Boolean(chartOfAccountId) &&
    (claimType === "EXPENSE"
      ? Number(amount) > 0
      : distanceNumber > 0 &&
        mileageRate > 0 &&
        mileageOriginAddress.trim().length > 0 &&
        mileageDestinationAddress.trim().length > 0) &&
    (paymentType === "PERSONAL" || (payViaAccountId.length > 0 && spendingAt.trim().length > 0));

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);

    try {
      const existingSupportingDocumentUrls = editingClaim?.supportingDocumentUrls ?? [];
      const uploadedReceipt = receiptFile ? await uploadClaimReceipt(receiptFile) : null;
      const filesToUpload = isEditing ? additionalSupportingFiles : supportingFiles;
      const uploadedSupportingDocuments = await Promise.all(filesToUpload.map((file) => uploadClaimReceipt(file)));
      const supportingDocumentUrls = [
        ...existingSupportingDocumentUrls,
        ...uploadedSupportingDocuments.map((doc) => doc.receiptUrl),
      ];
      const body: CreateClaimRequest = {
        title: title.trim(),
        description: description.trim(),
        category: claimType === "MILEAGE" ? "TRANSPORT" : "OTHER",
        currency: currency.toUpperCase(),
        spentAt: new Date(`${spentAt}T00:00:00`).toISOString(),
        claimType,
        paymentType,
        projectId: projectId || undefined,
        chartOfAccountId,
        payViaAccountId: paymentType === "COMPANY" ? payViaAccountId : undefined,
        spendingAt: spendingAt.trim() || undefined,
        spendingWith: spendingWith.trim() || undefined,
        receiptUrl: uploadedReceipt?.receiptUrl ?? editingClaim?.receiptUrl ?? undefined,
        supportingDocumentUrls,
      };

      if (claimType === "EXPENSE") {
        body.amount = Number(amount);
      } else {
        body.distance = Number(distance);
        body.mileageOriginAddress = mileageOriginAddress.trim();
        body.mileageDestinationAddress = mileageDestinationAddress.trim();
      }

      const claim = editingClaim
        ? await updateClaim(editingClaim.id, body)
        : await createClaim(body);
      if (editingClaim) onUpdated?.(claim);
      else onCreated(claim);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : `Could not ${isEditing ? "update" : "create"} claim`);
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-5">
      <StepWithBack
        eyebrow={isEditing ? "Edit before approval" : "Final step - Claim details"}
        title={claimType === "MILEAGE" ? "Mileage details" : "Expense details"}
        onBack={isEditing ? undefined : onBack}
      />

      <div className="rounded-2xl border border-border/70 bg-surface-low/50 px-4 py-3 text-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-semibold text-foreground">
            {paymentType === "COMPANY" ? "Company money" : "My own money"}
          </span>
          <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
            {claimType === "MILEAGE" ? "Mileage claim" : "Expense claim"}
          </span>
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <label className="space-y-3 sm:col-span-2">
          <span className={LABEL}>Title</span>
          <input
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder={claimType === "MILEAGE" ? "Client visit" : "Client dinner"}
            className={INPUT}
          />
        </label>

        <SelectField
          label={claimType === "MILEAGE" ? "Mileage account" : "Chart of account"}
          placeholder={claimType === "MILEAGE" ? "Select mileage account" : "Select chart of account"}
          value={chartOfAccountId}
          onValueChange={setChartOfAccountId}
          options={visibleAccounts.map((account) => ({
            value: account.id,
            label: `${account.code} - ${account.name}`,
          }))}
          emptyMessage={
            claimType === "MILEAGE"
              ? "No mileage account enabled yet."
              : "No selectable claim account enabled yet."
          }
        />

        <SelectField
          label="Project"
          placeholder="No project"
          value={projectId}
          onValueChange={setProjectId}
          options={projects.map((project) => ({ value: project.id, label: project.name }))}
          optional
        />

        {claimType === "EXPENSE" ? (
          <>
            <label className="space-y-3">
              <span className={LABEL}>Amount</span>
              <input
                required
                min="0.01"
                step="0.01"
                type="number"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                className={INPUT}
              />
            </label>
            <label className="space-y-3">
              <span className={LABEL}>Expense date</span>
              <input
                required
                type="date"
                value={spentAt}
                onChange={(e) => setSpentAt(e.target.value)}
                className={INPUT}
              />
            </label>
          </>
        ) : (
          <>
            <label className="space-y-3">
              <span className={LABEL}>
                Distance ({organization?.mileageUnit === "MILE" ? "miles" : "km"})
              </span>
              <input
                required
                min="0.01"
                step="0.01"
                type="number"
                value={distance}
                onChange={(e) => setDistance(e.target.value)}
                className={INPUT}
              />
            </label>
            <label className="space-y-3">
              <span className={LABEL}>Trip date</span>
              <input
                required
                type="date"
                value={spentAt}
                onChange={(e) => setSpentAt(e.target.value)}
                className={INPUT}
              />
            </label>
            <label className="space-y-3">
              <span className={LABEL}>From</span>
              <input
                required
                value={mileageOriginAddress}
                onChange={(e) => setMileageOriginAddress(e.target.value)}
                className={INPUT}
              />
            </label>
            <label className="space-y-3">
              <span className={LABEL}>To</span>
              <input
                required
                value={mileageDestinationAddress}
                onChange={(e) => setMileageDestinationAddress(e.target.value)}
                className={INPUT}
              />
            </label>
          </>
        )}

        <label className="space-y-3">
          <span className={LABEL}>Currency</span>
          <input
            required
            maxLength={3}
            value={currency}
            onChange={(e) => setCurrency(e.target.value.toUpperCase())}
            className={`${INPUT} uppercase`}
          />
        </label>

        {paymentType === "COMPANY" ? (
          <SelectField
            label="Company bank account"
            placeholder="Select company bank account"
            value={payViaAccountId}
            onValueChange={setPayViaAccountId}
            options={bankAccounts.map((account) => ({
              value: account.id,
              label: `${account.code} - ${account.name}`,
            }))}
            emptyMessage="No company bank account enabled yet."
          />
        ) : null}

        <label className="space-y-3">
          <span className={LABEL}>
            Spending at{" "}
            <span className="font-normal text-muted-foreground">
              {paymentType === "COMPANY" ? "(required)" : "(optional)"}
            </span>
          </span>
          <input
            required={paymentType === "COMPANY"}
            maxLength={200}
            value={spendingAt}
            onChange={(e) => setSpendingAt(e.target.value)}
            placeholder="Merchant or vendor"
            className={INPUT}
          />
        </label>

        <label className="space-y-3">
          <span className={LABEL}>
            Spending with <span className="font-normal text-muted-foreground">(optional)</span>
          </span>
          <input
            maxLength={200}
            value={spendingWith}
            onChange={(e) => setSpendingWith(e.target.value)}
            placeholder="Client, vendor, or team"
            className={INPUT}
          />
        </label>

        <label className="space-y-3 sm:col-span-2">
          <span className={LABEL}>Business context</span>
          <textarea
            required
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Describe why this expense was needed for work."
            className={TEXTAREA}
          />
        </label>
      </div>

      {claimType === "MILEAGE" ? (
        <InfoBox>
          {mileageRate > 0
            ? distanceNumber > 0
              ? `${distanceNumber.toFixed(2)} ${organization?.mileageUnit === "MILE" ? "miles" : "km"} x ${mileageRate} = ${computedMileageAmount.toFixed(2)}`
              : `Rate: ${mileageRate} per ${organization?.mileageUnit === "MILE" ? "mile" : "km"}`
            : "No mileage rate configured. Ask your admin to set it in mileage claim settings."}
        </InfoBox>
      ) : null}

      {!isEditing ? (
        <SupportingDocumentsField
          label="Supporting documents"
          hint="Optional: invoices, approvals, secondary receipts, or other supporting files."
          files={supportingFiles}
          onChange={setSupportingFiles}
        />
      ) : null}

      {isEditing ? (
        <SupportingDocumentsField
          label="Add more supporting documents"
          hint={
            editingClaim?.supportingDocumentUrls?.length
              ? "Existing supporting documents will be kept."
              : "No supporting documents attached yet."
          }
          files={additionalSupportingFiles}
          onChange={setAdditionalSupportingFiles}
        />
      ) : null}

      {overLimit && selectedAccount ? (
        <p className="flex items-start gap-2 rounded-2xl border border-amber-300/60 bg-amber-50 px-4 py-3 text-sm text-amber-700">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            This is over the {selectedAccount.code} spend limit of {currency.toUpperCase()}{" "}
            {selectedAccount.limitAmount}. You can still submit it. It will be flagged for review.
          </span>
        </p>
      ) : null}

      {error ? (
        <p className="rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
          {error}
        </p>
      ) : null}

      <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button
          type="button"
          onClick={onClose}
          className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={saving || !canSubmit}
          className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
        >
          {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
          {isEditing ? "Save changes" : "Submit claim"}
        </button>
      </div>
    </form>
  );
}

function SupportingDocumentsField({
  label,
  hint,
  files,
  onChange,
}: {
  label: string;
  hint: string;
  files: File[];
  onChange: (files: File[]) => void;
}) {
  const inputRef = useRef<HTMLInputElement | null>(null);

  return (
    <div className="space-y-2 sm:col-span-2">
      <div>
        <p className={LABEL}>{label}</p>
        <p className="mt-1 text-xs text-muted-foreground">{hint}</p>
      </div>
      <button
        type="button"
        onClick={() => inputRef.current?.click()}
        className="flex h-12 w-full items-center gap-3 rounded-2xl border border-border bg-white/80 px-4 text-left text-sm shadow-sm transition hover:border-primary/50 hover:bg-card"
      >
        <Upload className="h-4 w-4 shrink-0 text-primary" />
        <span className={`min-w-0 flex-1 truncate ${files.length > 0 ? "font-semibold text-foreground" : "text-muted-foreground"}`}>
          {formatSelectedFiles(files)}
        </span>
      </button>
      <input
        ref={inputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf"
        multiple
        tabIndex={-1}
        className="hidden"
        onChange={(event) => {
          onChange(Array.from(event.target.files ?? []).slice(0, 10));
          event.currentTarget.value = "";
          event.currentTarget.blur();
        }}
      />
    </div>
  );
}

function formatSelectedFiles(files: File[]) {
  if (files.length === 0) return "Tap to add supporting documents";
  if (files.length === 1) return files[0].name;
  return `${files.length} files selected - ${files.slice(0, 2).map((file) => file.name).join(", ")}`;
}

function StepIntro({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string;
  title: string;
  description?: string;
}) {
  return (
    <div>
      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
        {eyebrow}
      </p>
      <h3 className="mt-2 text-xl font-black text-foreground sm:text-2xl">{title}</h3>
      {description ? <p className="mt-1 text-sm leading-6 text-muted-foreground">{description}</p> : null}
    </div>
  );
}

function StepWithBack({
  eyebrow,
  title,
  onBack,
}: {
  eyebrow: string;
  title: string;
  onBack?: () => void;
}) {
  return (
    <div className="flex items-start justify-between gap-3">
      <StepIntro eyebrow={eyebrow} title={title} />
      {onBack ? (
        <button
          type="button"
          onClick={onBack}
          className="inline-flex items-center gap-1.5 rounded-full px-3 py-2 text-sm font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" />
          Back
        </button>
      ) : null}
    </div>
  );
}

function ChoiceCard({
  icon,
  title,
  description,
  onClick,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="group flex min-h-40 flex-col items-start gap-3 rounded-2xl border border-border/70 bg-card/94 p-5 text-left shadow-ambient transition hover:border-primary/60"
    >
      <div className="rounded-xl bg-primary/10 p-2 text-primary transition group-hover:bg-primary group-hover:text-primary-foreground">
        {icon}
      </div>
      <div>
        <p className="font-bold text-foreground">{title}</p>
        <p className="mt-1 text-sm leading-6 text-muted-foreground">{description}</p>
      </div>
    </button>
  );
}

function SelectField({
  label,
  placeholder,
  value,
  onValueChange,
  options,
  optional = false,
  emptyMessage,
}: {
  label: string;
  placeholder: string;
  value: string;
  onValueChange: (value: string) => void;
  options: Array<{ value: string; label: string }>;
  optional?: boolean;
  emptyMessage?: string;
}) {
  if (options.length === 0) {
    return (
      <div className="space-y-3">
        <span className={LABEL}>{label}</span>
        <div className="rounded-2xl border border-amber-300/50 bg-amber-50/70 px-4 py-3 text-sm text-amber-900">
          {emptyMessage ?? "No options available."}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <span className={LABEL}>
        {label}{" "}
        {optional ? <span className="font-normal text-muted-foreground">(optional)</span> : null}
      </span>
      <Select value={value || undefined} onValueChange={onValueChange}>
        <SelectTrigger>
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
        <SelectContent searchPlaceholder={`Search ${label.toLowerCase()}...`}>
          {options.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

function InfoBox({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-border/70 bg-surface-low/50 px-4 py-3 text-sm font-medium text-muted-foreground">
      {children}
    </div>
  );
}

function toClaimType(value: string): ClaimType {
  return value === "MILEAGE" ? "MILEAGE" : "EXPENSE";
}

function toPaymentType(value: string): PaymentType {
  return value === "COMPANY" ? "COMPANY" : "PERSONAL";
}
