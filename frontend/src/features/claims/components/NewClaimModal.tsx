import { type FormEvent, useEffect, useState } from "react";
import { LoaderCircle, TriangleAlert, Upload, X } from "lucide-react";
import {
  createClaim,
  uploadClaimReceipt,
  type Claim,
} from "../api";
import { claimCategoryOptions } from "../lib/claim-options";
import {
  getAccounts,
  getProjects,
  type ChartOfAccount,
  type Project,
} from "@/features/settings/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

// Radix Select forbids an empty-string option value, so the "no selection"
// rows use this sentinel and map back to "" when the claim is submitted.
const NO_SELECTION = "__none__";

export function NewClaimModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (claim: Claim) => void;
}) {
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState("MEAL");
  const [amount, setAmount] = useState("");
  const [currency, setCurrency] = useState("MYR");
  const [spentAt, setSpentAt] = useState(new Date().toISOString().slice(0, 10));
  const [projectId, setProjectId] = useState("");
  const [chartOfAccountId, setChartOfAccountId] = useState("");
  const [projects, setProjects] = useState<Project[]>([]);
  const [accounts, setAccounts] = useState<ChartOfAccount[]>([]);
  const [receiptFile, setReceiptFile] = useState<File | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Load the org's projects + selectable accounts (from Settings) for the pickers.
  useEffect(() => {
    let active = true;
    Promise.all([getProjects(), getAccounts()])
      .then(([projectList, accountList]) => {
        if (!active) return;
        setProjects(projectList.filter((p) => !p.isArchived));
        setAccounts(accountList.filter((a) => a.isSelectable && !a.isArchived));
      })
      .catch(() => {
        /* pickers just stay empty if settings can't load */
      });
    return () => {
      active = false;
    };
  }, []);

  const selectedAccount = accounts.find((a) => a.id === chartOfAccountId) ?? null;
  const overLimit =
    selectedAccount?.limitAmount != null &&
    Number(amount) > selectedAccount.limitAmount;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);

    try {
      const receipt = receiptFile ? await uploadClaimReceipt(receiptFile) : null;
      const claim = await createClaim({
        title,
        description,
        category,
        amount: Number(amount),
        currency: currency.toUpperCase(),
        spentAt: new Date(`${spentAt}T00:00:00`).toISOString(),
        claimType: "EXPENSE",
        paymentType: "PERSONAL",
        projectId: projectId || undefined,
        chartOfAccountId: chartOfAccountId || undefined,
        receiptUrl: receipt?.receiptUrl,
      });
      onCreated(claim);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create claim");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-[680px] overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-[0_18px_48px_rgba(76,26,134,0.10)] backdrop-blur-xl">
        <form
          onSubmit={handleSubmit}
          className="nice-scrollbar max-h-[90vh] overflow-y-auto p-6 sm:p-8"
        >
        <div className="flex items-start justify-between gap-4 border-b border-border/60 pb-4">
          <div>
            <h2 className="text-2xl font-black text-foreground">Submit a claim</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Fill in the details and attach a receipt when needed.
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

        <div className="mt-5 grid gap-4 sm:grid-cols-2">
          <label className="space-y-2 sm:col-span-2">
            <span className="text-sm font-semibold text-foreground">Title</span>
            <input
              required
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              className="h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          </label>
          <div className="space-y-2">
            <span className="text-sm font-semibold text-foreground">Category</span>
            <Select value={category} onValueChange={setCategory}>
              <SelectTrigger>
                <SelectValue placeholder="Select a category" />
              </SelectTrigger>
              <SelectContent searchPlaceholder="Search categories…">
                {claimCategoryOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <label className="space-y-2">
            <span className="text-sm font-semibold text-foreground">Spent date</span>
            <input
              required
              type="date"
              value={spentAt}
              onChange={(e) => setSpentAt(e.target.value)}
              className="h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-semibold text-foreground">Amount</span>
            <input
              required
              min="0.01"
              step="0.01"
              type="number"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className="h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-semibold text-foreground">Currency</span>
            <input
              required
              maxLength={3}
              value={currency}
              onChange={(e) => setCurrency(e.target.value)}
              className="h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm uppercase shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          </label>
          <div className="space-y-2">
            <span className="text-sm font-semibold text-foreground">
              Project <span className="font-normal text-muted-foreground">(optional)</span>
            </span>
            <Select
              value={projectId || NO_SELECTION}
              onValueChange={(v) => setProjectId(v === NO_SELECTION ? "" : v)}
            >
              <SelectTrigger>
                <SelectValue placeholder="No project" />
              </SelectTrigger>
              <SelectContent searchPlaceholder="Search projects…">
                <SelectItem value={NO_SELECTION}>No project</SelectItem>
                {projects.map((project) => (
                  <SelectItem key={project.id} value={project.id}>
                    {project.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <span className="text-sm font-semibold text-foreground">
              Account <span className="font-normal text-muted-foreground">(optional)</span>
            </span>
            <Select
              value={chartOfAccountId || NO_SELECTION}
              onValueChange={(v) => setChartOfAccountId(v === NO_SELECTION ? "" : v)}
            >
              <SelectTrigger>
                <SelectValue placeholder="No account" />
              </SelectTrigger>
              <SelectContent searchPlaceholder="Search accounts…">
                <SelectItem value={NO_SELECTION}>No account</SelectItem>
                {accounts.map((account) => (
                  <SelectItem
                    key={account.id}
                    value={account.id}
                    textValue={`${account.code} ${account.name}`}
                  >
                    {account.code} · {account.name}
                    {account.limitAmount != null ? ` (limit ${account.limitAmount})` : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {overLimit && selectedAccount ? (
            <p className="flex items-start gap-2 rounded-2xl border border-amber-300/60 bg-amber-50 px-4 py-3 text-sm text-amber-700 sm:col-span-2">
              <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
              <span>
                This is over the {selectedAccount.code} spend limit of{" "}
                {currency.toUpperCase()} {selectedAccount.limitAmount}. You can still
                submit it — it will be flagged for the approver.
              </span>
            </p>
          ) : null}
          <label className="space-y-2 sm:col-span-2">
            <span className="text-sm font-semibold text-foreground">Description</span>
            <textarea
              required
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="min-h-[112px] w-full rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          </label>
          <div className="space-y-2 sm:col-span-2">
            <span className="text-sm font-semibold text-foreground">
              Receipt photo <span className="font-normal text-muted-foreground">(optional)</span>
            </span>
            <label
              htmlFor="receiptFile"
              className="flex min-h-24 cursor-pointer flex-col items-center justify-center rounded-2xl border border-border/70 bg-card/94 px-4 py-5 text-center shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm transition-colors hover:border-primary/40 hover:bg-card"
            >
              <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                <Upload className="h-4 w-4" />
                <span>Upload photo</span>
              </div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">
                JPG, PNG, WEBP, HEIC, HEIF, or PDF up to 8 MB
              </p>
              {receiptFile ? (
                <p className="mt-3 max-w-full truncate rounded-full bg-background px-3 py-1 text-xs font-medium text-foreground">
                  {receiptFile.name}
                </p>
              ) : null}
            </label>
            <input
              id="receiptFile"
              type="file"
              accept="image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf"
              className="sr-only"
              onChange={(event) => setReceiptFile(event.target.files?.[0] ?? null)}
            />
          </div>
        </div>

        {error ? (
          <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
            {error}
          </p>
        ) : null}

        <div className="mt-6 flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
          >
            {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Submit claim
          </button>
        </div>
        </form>
      </div>
    </div>
  );
}
