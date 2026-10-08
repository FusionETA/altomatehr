import { useMemo, useRef, useState } from "react";
import { FilePlus2, FileText, FileUp, LoaderCircle, Plus, Sparkles } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { SkeletonPanel } from "@/shared/components/Skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useMyAccess } from "@/features/settings/lib/module-access";
import { getEmployees, STAFF_ROLES } from "@/features/employees/api";
import {
  addSampleTemplates,
  deleteTemplate,
  getMergeFields,
  getTemplates,
  importTemplateFile,
  MERGE_FIELDS_PATH,
  TEMPLATE_IMPORT_ACCEPT,
  TEMPLATE_IMPORT_MAX_BYTES,
  TEMPLATES_PATH,
  type DocumentTemplate,
  type TemplateImport,
} from "../api";
import { categoryLabel, formatDate } from "../lib/categories";
import { GenerateLetterDialog } from "./GenerateLetterDialog";
import { GeneratedLettersList } from "./GeneratedLettersList";
import { TemplateEditor } from "./TemplateEditor";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const PILL =
  "rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50";

type OnOpen = (parentId: string, childId: string) => void;

// Documents → Templates: the company's letter library. The editor opens in
// place of the list (local state, like a payroll run's drill-down), and
// generating a letter is a dialog over either.
export function DocumentTemplatesView({ onOpen }: { onOpen?: OnOpen }) {
  const canManage = useMyAccess().canManage("documents");
  const templatesQuery = useCachedQuery(TEMPLATES_PATH, getTemplates);
  const fieldsQuery = useCachedQuery(MERGE_FIELDS_PATH, getMergeFields);
  const templates = templatesQuery.data ?? [];

  const [editing, setEditing] = useState<DocumentTemplate | "new" | null>(null);
  // An uploaded file, converted — opens the editor unsaved, like "new".
  const [imported, setImported] = useState<(TemplateImport & { fileName: string }) | null>(null);
  const [generating, setGenerating] = useState<DocumentTemplate | null>(null);
  const [addingSamples, setAddingSamples] = useState(false);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();

  const openPayroll = onOpen ? () => onOpen("payroll", "payroll") : undefined;

  async function addSamples() {
    setAddingSamples(true);
    setError(null);
    setNotice(null);
    try {
      const result = await addSampleTemplates();
      await templatesQuery.refresh();
      setNotice(
        result.added === 0
          ? "You already have every sample template."
          : `Added ${result.added} sample template${result.added === 1 ? "" : "s"}. Replace the sample wording before using them.`,
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not add the sample templates.");
    } finally {
      setAddingSamples(false);
    }
  }

  // Word / text file → markup, then the editor opens on it for review. Nothing
  // is saved until the admin presses Save template.
  async function upload(file: File) {
    setError(null);
    setNotice(null);
    if (file.size > TEMPLATE_IMPORT_MAX_BYTES) {
      setError("The file is larger than 5 MB.");
      return;
    }
    setUploading(true);
    try {
      const result = await importTemplateFile(file);
      setImported({ ...result, fileName: file.name });
      setEditing("new");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not read that file.");
    } finally {
      setUploading(false);
    }
  }

  const uploadButton = (primary: boolean) => (
    <button
      type="button"
      onClick={() => fileInputRef.current?.click()}
      disabled={uploading}
      title="A Word (.docx), .txt or .md file — converted into the editor for you to review"
      className={`inline-flex items-center gap-2 rounded-2xl border border-border bg-card ${
        primary ? "px-5" : "px-4"
      } py-2.5 text-sm font-semibold text-foreground transition hover:border-primary hover:text-primary disabled:opacity-50`}
    >
      {uploading ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <FileUp className="h-4 w-4" />}
      Upload Word file
    </button>
  );

  async function remove(template: DocumentTemplate) {
    const ok = await confirm({
      title: `Delete "${template.name}"?`,
      message: "Letters already generated from it stay on file.",
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;
    setBusyId(template.id);
    setError(null);
    try {
      await deleteTemplate(template.id);
      await templatesQuery.refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not delete the template.");
    } finally {
      setBusyId(null);
    }
  }

  if (editing) {
    return (
      <TemplateEditor
        template={editing === "new" ? null : editing}
        imported={editing === "new" ? imported : null}
        mergeFields={fieldsQuery.data ?? []}
        readOnly={!canManage}
        onClose={() => {
          setEditing(null);
          setImported(null);
        }}
        onSaved={() => {
          setEditing(null);
          setImported(null);
          void templatesQuery.refresh();
        }}
      />
    );
  }

  const loading = templatesQuery.loading;

  return (
    <div className={`${CARD} space-y-5`}>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="text-lg font-black text-foreground">Letter templates</h2>
          <p className="text-sm text-muted-foreground">
            Offer, confirmation, resignation and warning letters for your company. Merge fields fill in
            the employee's details when you generate a PDF.
          </p>
        </div>
        {canManage && templates.length > 0 ? (
          <div className="flex shrink-0 flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void addSamples()}
              disabled={addingSamples}
              className="inline-flex items-center gap-2 rounded-2xl border border-border bg-card px-4 py-2.5 text-sm font-semibold text-foreground transition hover:border-primary hover:text-primary disabled:opacity-50"
            >
              {addingSamples ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
              Add sample templates
            </button>
            {uploadButton(false)}
            <button
              type="button"
              onClick={() => setEditing("new")}
              className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90"
            >
              <Plus className="h-4 w-4" />
              New template
            </button>
          </div>
        ) : null}
      </div>

      {error || templatesQuery.error ? (
        <p className="text-sm font-medium text-destructive">{error ?? templatesQuery.error}</p>
      ) : null}
      {notice ? <p className="text-sm font-medium text-foreground">{notice}</p> : null}

      {loading ? (
        <SkeletonPanel />
      ) : templates.length === 0 ? (
        <div className="flex flex-col items-center rounded-3xl border border-dashed border-border/80 bg-surface-low/40 px-6 py-10 text-center">
          <span className="grid h-12 w-12 place-items-center rounded-2xl bg-primary/10 text-primary">
            <FileText className="h-6 w-6" />
          </span>
          <h3 className="mt-4 text-base font-black text-foreground">No letter templates yet</h3>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            Start from four sample letters — offer, confirmation of employment, resignation acceptance
            and a first written warning — then replace the sample wording with your own.
          </p>
          {canManage ? (
            <div className="mt-5 flex flex-wrap justify-center gap-2">
              <button
                type="button"
                onClick={() => void addSamples()}
                disabled={addingSamples}
                className="inline-flex items-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
              >
                {addingSamples ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                Add sample templates
              </button>
              <button
                type="button"
                onClick={() => setEditing("new")}
                className="inline-flex items-center gap-2 rounded-2xl border border-border bg-card px-5 py-2.5 text-sm font-semibold text-foreground transition hover:border-primary hover:text-primary"
              >
                <Plus className="h-4 w-4" />
                Start from blank
              </button>
              {uploadButton(true)}
            </div>
          ) : (
            <p className="mt-4 text-xs text-muted-foreground">Your access to Documents is view only.</p>
          )}
        </div>
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {templates.map((t) => (
            <li key={t.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
              <button type="button" onClick={() => setEditing(t)} className="min-w-0 flex-1 text-left">
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold text-foreground hover:underline">{t.name}</span>
                  <span className="rounded-full bg-primary/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.12em] text-primary">
                    {categoryLabel(t.category)}
                  </span>
                  {t.isSample ? (
                    <span className="rounded-full bg-warning/40 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.12em] text-warning-foreground">
                      Sample
                    </span>
                  ) : null}
                </span>
                <span className="block text-xs text-muted-foreground">
                  Updated {formatDate(t.updatedAt)} · {t.fields.length} merge field{t.fields.length === 1 ? "" : "s"}
                </span>
              </button>
              <div className="flex shrink-0 items-center gap-2">
                {canManage ? (
                  <button
                    type="button"
                    onClick={() => setGenerating(t)}
                    className="inline-flex items-center gap-1.5 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:opacity-90"
                  >
                    <FilePlus2 className="h-3.5 w-3.5" />
                    Generate
                  </button>
                ) : null}
                <button type="button" onClick={() => setEditing(t)} className={PILL}>
                  {canManage ? "Edit" : "View"}
                </button>
                {canManage ? (
                  <button
                    type="button"
                    disabled={busyId === t.id}
                    onClick={() => void remove(t)}
                    className={PILL}
                  >
                    Delete
                  </button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept={TEMPLATE_IMPORT_ACCEPT}
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = ""; // the same file can be picked again
          if (file) void upload(file);
        }}
      />

      {generating ? (
        <GenerateLetterDialog
          template={generating}
          onClose={() => setGenerating(null)}
          onOpenPayroll={openPayroll}
        />
      ) : null}
      {confirmDialog}
    </div>
  );
}

// Documents → Letters: every letter kept on file, filterable by employee.
// Also reachable per person on their Manage Employee record.
export function GeneratedLettersView({ onOpen }: { onOpen?: OnOpen }) {
  const canManage = useMyAccess().canManage("documents");
  const employeesQuery = useCachedQuery("/employees", getEmployees);
  const [employeeUserId, setEmployeeUserId] = useState<string>("all");
  const [generating, setGenerating] = useState(false);

  const employees = useMemo(
    () =>
      (employeesQuery.data ?? [])
        .filter((e) => (STAFF_ROLES as readonly string[]).includes(e.role))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [employeesQuery.data],
  );
  const filter = employeeUserId === "all" ? null : employeeUserId;

  return (
    <div className={`${CARD} space-y-5`}>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="text-lg font-black text-foreground">Letters on file</h2>
          <p className="text-sm text-muted-foreground">
            Letters generated and kept for your employees. Admins only — employees don't see these in
            their portal.
          </p>
        </div>
        {canManage ? (
          <button
            type="button"
            onClick={() => setGenerating(true)}
            className="inline-flex shrink-0 items-center gap-2 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90"
          >
            <FilePlus2 className="h-4 w-4" />
            Generate letter
          </button>
        ) : null}
      </div>

      <div className="max-w-sm">
        <Select value={employeeUserId} onValueChange={setEmployeeUserId}>
          <SelectTrigger className="h-11 bg-card">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All employees</SelectItem>
            {employees.map((e) => (
              <SelectItem key={e.id} value={e.id}>
                {e.name || e.email}
                {e.employeeNumber ? ` · ${e.employeeNumber}` : ""}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <GeneratedLettersList
        key={filter ?? "all"}
        employeeUserId={filter}
        showEmployee={filter === null}
        canManage={canManage}
        emptyText={filter ? "No letters on file for this employee." : "No letters on file yet."}
      />

      {generating ? (
        <GenerateLetterDialog
          employeeUserId={filter}
          onClose={() => setGenerating(false)}
          onOpenPayroll={onOpen ? () => onOpen("payroll", "payroll") : undefined}
        />
      ) : null}
    </div>
  );
}
