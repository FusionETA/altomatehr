import { useState } from "react";
import { Download, LoaderCircle, Trash2 } from "lucide-react";
import { saveFile } from "@/shared/lib/api-client";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { Skeleton } from "@/shared/components/Skeleton";
import {
  deleteGeneratedDocument,
  downloadGeneratedDocument,
  generatedPath,
  getGeneratedDocuments,
  type GeneratedDocument,
} from "../api";
import { categoryLabel, formatDate, formatFileSize } from "../lib/categories";

// Letters kept on file — for one employee, or the whole company. Admin-only:
// the endpoint behind this is Admin/Owner, and these never appear in the
// employee's own portal.
export function GeneratedLettersList({
  employeeUserId,
  showEmployee,
  canManage,
  emptyText = "No letters on file yet.",
}: {
  employeeUserId?: string | null;
  showEmployee: boolean;
  canManage: boolean;
  emptyText?: string;
}) {
  const path = generatedPath(employeeUserId);
  const query = useCachedQuery(path, () => getGeneratedDocuments(employeeUserId), {
    refetchOnInvalidate: true,
  });
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();
  const letters = query.data ?? [];

  async function download(doc: GeneratedDocument) {
    setBusyId(doc.id);
    setError(null);
    try {
      saveFile(await downloadGeneratedDocument(doc));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not download the letter.");
    } finally {
      setBusyId(null);
    }
  }

  async function remove(doc: GeneratedDocument) {
    const ok = await confirm({
      title: "Delete this letter?",
      message: `"${doc.templateName}" from ${formatDate(doc.createdAt)} is removed from the file for good.`,
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;
    setBusyId(doc.id);
    setError(null);
    try {
      await deleteGeneratedDocument(doc.id);
      await query.refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not delete the letter.");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="space-y-2">
      {error || query.error ? (
        <p className="text-sm font-medium text-destructive">{error ?? query.error}</p>
      ) : null}
      {query.loading ? (
        <div className="space-y-2">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </div>
      ) : letters.length === 0 ? (
        <p className="text-sm text-muted-foreground">{emptyText}</p>
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {letters.map((doc) => (
            <li key={doc.id} className="flex flex-wrap items-center justify-between gap-3 bg-card px-4 py-3">
              <button
                type="button"
                onClick={() => void download(doc)}
                className="min-w-0 flex-1 text-left"
              >
                <span className="flex items-center gap-2">
                  <span className="truncate text-sm font-semibold text-foreground hover:underline">
                    {doc.templateName}
                  </span>
                  <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-muted-foreground">
                    {categoryLabel(doc.category)}
                  </span>
                </span>
                <span className="block text-xs text-muted-foreground">
                  {showEmployee && doc.employeeName ? `${doc.employeeName} · ` : ""}
                  {formatDate(doc.createdAt)}
                  {doc.generatedByName ? ` · by ${doc.generatedByName}` : ""} · {formatFileSize(doc.sizeBytes)}
                </span>
              </button>
              <div className="flex shrink-0 items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => void download(doc)}
                  disabled={busyId === doc.id}
                  aria-label="Download"
                  className="grid h-8 w-8 place-items-center rounded-xl border border-border bg-card text-foreground transition hover:border-primary hover:text-primary disabled:opacity-50"
                >
                  {busyId === doc.id ? (
                    <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Download className="h-3.5 w-3.5" />
                  )}
                </button>
                {canManage ? (
                  <button
                    type="button"
                    onClick={() => void remove(doc)}
                    disabled={busyId === doc.id}
                    aria-label="Delete"
                    className="grid h-8 w-8 place-items-center rounded-xl border border-border bg-card text-destructive transition hover:border-destructive disabled:opacity-50"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}
      {confirmDialog}
    </div>
  );
}
