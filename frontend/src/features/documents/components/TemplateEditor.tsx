import { useMemo, useRef, useState } from "react";
import { ArrowLeft, Check, Copy, Eye, FileDown, FileUp, LoaderCircle, PencilLine, Plus, Undo2, WandSparkles } from "lucide-react";
import { ApiError, saveFile } from "@/shared/lib/api-client";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  createTemplate,
  draftTemplateWithAi,
  previewTemplate,
  updateTemplate,
  type DocumentCategory,
  type DocumentTemplate,
  type MergeField,
  type MergeFieldSource,
  type TemplateAiDraft,
  type TemplateAiMessage,
  type TemplateImport,
} from "../api";
import { DOCUMENT_CATEGORIES } from "../lib/categories";
import { fieldsIn, inputKeyFor, isValidInputField, placeholdersIn, replacePlaceholder } from "../lib/markup";
import { LetterPreview } from "./LetterPreview";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-11 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-60";
const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";

const GROUPS: { source: MergeFieldSource; title: string }[] = [
  { source: "Employee", title: "Employee" },
  { source: "Company", title: "Company" },
  { source: "Signatory", title: "Signatory" },
  { source: "System", title: "Letter" },
];

// Create or edit one template: name, category, the body in a textarea with a
// side panel of merge fields to insert at the cursor, and a preview.
//
// Deliberately a textarea, not a rich-text editor: the body is a tiny markdown
// subset (see lib/markup.ts) that the PDF renderer understands exactly.
//
// `imported` opens a new, unsaved template pre-filled from an uploaded Word /
// text file, with what didn't convert and a picker for its placeholders.
// `aiDraft` does the same for a draft chosen in "Write with AI", and carries the
// conversation on into the Ask AI panel.
export function TemplateEditor({
  template,
  imported,
  aiDraft,
  mergeFields,
  readOnly,
  onClose,
  onSaved,
}: {
  template: DocumentTemplate | null;
  imported?: (TemplateImport & { fileName: string }) | null;
  aiDraft?: { draft: TemplateAiDraft; conversation: TemplateAiMessage[] } | null;
  mergeFields: MergeField[];
  readOnly: boolean;
  onClose: () => void;
  onSaved: (template: DocumentTemplate) => void;
}) {
  const [name, setName] = useState(template?.name ?? imported?.suggestedName ?? aiDraft?.draft.name ?? "");
  const [category, setCategory] = useState<DocumentCategory>(
    template?.category ?? aiDraft?.draft.category ?? "OTHER",
  );
  const [body, setBody] = useState(template?.body ?? imported?.body ?? aiDraft?.draft.body ?? "");
  const [copied, setCopied] = useState(false);
  const [mode, setMode] = useState<"edit" | "preview">("edit");
  const [inputName, setInputName] = useState("");
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [serverUnknown, setServerUnknown] = useState<string[]>([]);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [confirm, confirmDialog] = useConfirm();

  const known = useMemo(() => new Set(mergeFields.map((f) => f.key)), [mergeFields]);
  const used = useMemo(() => fieldsIn(body), [body]);
  // Any {{…}} the PDF can't fill — including ones the strict pattern skips,
  // like {{Employee Name}} from a Word file.
  const unknown = useMemo(
    () => placeholdersIn(body).filter((k) => !known.has(k) && !isValidInputField(k)),
    [body, known],
  );
  const inputs = used.filter((k) => isValidInputField(k));

  function replaceField(from: string, to: string) {
    setBody((b) => replacePlaceholder(b, from, to));
    setServerUnknown((list) => list.filter((k) => k !== from));
  }

  async function copyFieldList() {
    const text = mergeFields.map((f) => `{{${f.key}}}  ${f.label}`).join("\n");
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setError("Couldn't copy to the clipboard. Pick the fields from the list instead.");
    }
  }
  const dirty =
    name !== (template?.name ?? "") ||
    category !== (template?.category ?? "OTHER") ||
    body !== (template?.body ?? "");

  function insert(text: string) {
    const el = textareaRef.current;
    if (!el || mode !== "edit") {
      setBody((b) => b + text);
      return;
    }
    const start = el.selectionStart ?? body.length;
    const end = el.selectionEnd ?? body.length;
    const next = body.slice(0, start) + text + body.slice(end);
    setBody(next);
    // Put the cursor after what was inserted, once React has re-rendered.
    requestAnimationFrame(() => {
      el.focus();
      el.setSelectionRange(start + text.length, start + text.length);
    });
  }

  function insertInput() {
    // "Last working day" → lastWorkingDay
    const key = inputKeyFor(inputName);
    if (!key) return;
    insert(`{{${key}}}`);
    setInputName("");
  }

  async function close() {
    if (dirty && !readOnly) {
      const ok = await confirm({
        title: "Discard changes?",
        message: "Your edits to this template haven't been saved.",
        confirmLabel: "Discard",
        destructive: true,
      });
      if (!ok) return;
    }
    onClose();
  }

  async function save() {
    setSaving(true);
    setError(null);
    setServerUnknown([]);
    try {
      const payload = { name: name.trim(), category, body };
      const saved = template ? await updateTemplate(template.id, payload) : await createTemplate(payload);
      onSaved(saved);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not save the template.");
      if (e instanceof ApiError && e.body && typeof e.body === "object" && "unknownFields" in e.body) {
        const list = (e.body as { unknownFields?: unknown }).unknownFields;
        if (Array.isArray(list)) setServerUnknown(list.filter((x): x is string => typeof x === "string"));
      }
    } finally {
      setSaving(false);
    }
  }

  async function previewPdf() {
    setPreviewing(true);
    setError(null);
    try {
      // An existing template previews the edits on screen, saved or not.
      saveFile(await previewTemplate(template?.id ?? null, { name: name.trim() || undefined, body }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not build the preview.");
    } finally {
      setPreviewing(false);
    }
  }

  const flagged = [...new Set([...unknown, ...serverUnknown])];
  const canSave = !readOnly && name.trim() !== "" && body.trim() !== "" && unknown.length === 0 && !saving;

  return (
    <div className="space-y-4">
      <div className={`${CARD} space-y-4`}>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex min-w-0 items-start gap-3">
            <button
              type="button"
              onClick={() => void close()}
              aria-label="Back to templates"
              className="mt-0.5 grid h-9 w-9 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
            >
              <ArrowLeft className="h-4 w-4" />
            </button>
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                {template
                  ? "Edit template"
                  : imported
                    ? "New template · from upload"
                    : aiDraft
                      ? "New template · written with AI"
                      : "New template"}
              </p>
              <h2 className="truncate text-lg font-black text-foreground">{name.trim() || "Untitled letter"}</h2>
            </div>
          </div>
          <div className="flex shrink-0 flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => void previewPdf()}
              disabled={previewing || body.trim() === "" || unknown.length > 0}
              className="inline-flex h-10 items-center gap-2 rounded-2xl border border-border bg-card px-4 text-sm font-semibold text-foreground transition hover:border-primary hover:text-primary disabled:pointer-events-none disabled:opacity-50"
            >
              {previewing ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <FileDown className="h-4 w-4" />}
              Preview PDF
            </button>
            {readOnly ? null : (
              <button
                type="button"
                onClick={() => void save()}
                disabled={!canSave}
                className="inline-flex h-10 items-center gap-2 rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:pointer-events-none disabled:opacity-50"
              >
                {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Save template
              </button>
            )}
          </div>
        </div>

        {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

        <div className="grid gap-4 sm:grid-cols-[1fr_220px]">
          <label className="block">
            <span className={LABEL}>Name</span>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={160}
              disabled={readOnly}
              placeholder="e.g. Offer Letter — Executive"
              className={`${INPUT} mt-1.5`}
            />
          </label>
          <div>
            <span className={LABEL}>Category</span>
            <Select
              value={category}
              onValueChange={(v) => setCategory(v as DocumentCategory)}
              disabled={readOnly}
            >
              <SelectTrigger className="mt-1.5 h-11 bg-card">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DOCUMENT_CATEGORIES.map((c) => (
                  <SelectItem key={c.value} value={c.value}>
                    {c.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>
      </div>

      {imported ? (
        <div className={`${CARD} space-y-3`}>
          <div className="flex items-start gap-3">
            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-2xl bg-primary/10 text-primary">
              <FileUp className="h-4 w-4" />
            </span>
            <div className="min-w-0">
              <h3 className="truncate text-sm font-black text-foreground">Imported from {imported.fileName}</h3>
              <p className="text-xs text-muted-foreground">
                Check the text below, then save. Nothing is kept until you press Save template. The PDF
                adds the letterhead, date and signature block — delete them here if they came across
                from the file.
              </p>
            </div>
          </div>
          {imported.warnings.length > 0 ? (
            <ul className="list-disc space-y-1 rounded-2xl border border-warning-foreground/20 bg-warning/20 py-3 pl-8 pr-4 text-xs text-warning-foreground">
              {imported.warnings.map((w) => (
                <li key={w}>{w}</li>
              ))}
            </ul>
          ) : null}
          {unknown.length > 0 && !readOnly ? (
            <PlaceholderPicker unknown={unknown} mergeFields={mergeFields} onReplace={replaceField} />
          ) : null}
        </div>
      ) : null}

      {aiDraft ? (
        <div className={`${CARD} space-y-3`}>
          <div className="flex items-start gap-3">
            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-2xl bg-primary/10 text-primary">
              <WandSparkles className="h-4 w-4" />
            </span>
            <div className="min-w-0">
              <h3 className="truncate text-sm font-black text-foreground">Written with AI</h3>
              <p className="text-xs text-muted-foreground">
                Read it through and adjust the wording to your company's policies before saving — it
                isn't legal advice. Nothing is kept until you press Save template. Ask AI on the right
                can keep changing it.
              </p>
            </div>
          </div>
          {aiDraft.draft.warnings.length > 0 ? (
            <ul className="list-disc space-y-1 rounded-2xl border border-warning-foreground/20 bg-warning/20 py-3 pl-8 pr-4 text-xs text-warning-foreground">
              {aiDraft.draft.warnings.map((w) => (
                <li key={w}>{w}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_300px]">
        <div className={`${CARD} space-y-3`}>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="inline-flex rounded-2xl border border-border/60 bg-surface-low/60 p-1">
              {(["edit", "preview"] as const).map((m) => (
                <button
                  key={m}
                  type="button"
                  onClick={() => setMode(m)}
                  className={`inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-bold transition ${
                    mode === m ? "bg-card text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  {m === "edit" ? <PencilLine className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                  {m === "edit" ? "Write" : "Preview"}
                </button>
              ))}
            </div>
            <p className="text-xs text-muted-foreground">
              <code className="rounded bg-muted px-1"># Heading</code>{" "}
              <code className="rounded bg-muted px-1">**bold**</code>{" "}
              <code className="rounded bg-muted px-1">- bullet</code> · blank line = new paragraph
            </p>
          </div>

          {mode === "edit" ? (
            <textarea
              ref={textareaRef}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              readOnly={readOnly}
              maxLength={20000}
              spellCheck
              placeholder={"Dear {{employee.name}},\n\n# Letter title\n\nWrite the letter here…"}
              className="nice-scrollbar min-h-[460px] w-full resize-y rounded-2xl border border-border bg-card p-4 font-mono text-[13px] leading-relaxed text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          ) : (
            <div className="min-h-[460px] rounded-2xl border border-border/60 bg-background p-6">
              <p className="mb-4 text-xs text-muted-foreground">
                The PDF adds the letterhead, the date and the signature block. Fields show by name.
              </p>
              <LetterPreview body={body} fields={mergeFields} />
            </div>
          )}

          {flagged.length > 0 ? (
            <div className="rounded-2xl border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              <p className="font-semibold">These merge fields don't exist:</p>
              <p className="mt-1 font-mono text-xs">{flagged.map((k) => `{{${k}}}`).join("  ")}</p>
              <p className="mt-1 text-xs">
                Pick a field from the list, or use <code>{"{{input.yourName}}"}</code> for a detail typed
                in for each letter.
              </p>
              {!imported && !readOnly ? (
                <div className="mt-3">
                  <PlaceholderPicker unknown={flagged} mergeFields={mergeFields} onReplace={replaceField} />
                </div>
              ) : null}
            </div>
          ) : null}
        </div>

        <aside className={`${CARD} space-y-4 self-start lg:sticky lg:top-4`}>
          {readOnly ? null : (
            <AskAiPanel
              initialConversation={aiDraft?.conversation ?? []}
              body={body}
              name={name}
              category={category}
              onRevised={(draft) => {
                setBody(draft.body);
                if (!name.trim()) setName(draft.name);
              }}
              onUndo={setBody}
            />
          )}

          <div>
            <h3 className="text-sm font-black text-foreground">Merge fields</h3>
            <p className="mt-0.5 text-xs text-muted-foreground">
              Click to insert at the cursor. Filled from the employee's and company's records when a
              letter is generated.
            </p>
            <div className="mt-2 rounded-2xl border border-border/60 bg-surface-low/50 px-3 py-2 text-[11px] text-muted-foreground">
              <p>
                Tip: in Word, type placeholders like{" "}
                <code className="rounded bg-muted px-1 text-foreground">{"{{employee.name}}"}</code> where
                details should go, then use Upload Word file.
              </p>
              <button
                type="button"
                onClick={() => void copyFieldList()}
                disabled={mergeFields.length === 0}
                className="mt-1.5 inline-flex items-center gap-1 font-semibold text-primary hover:underline disabled:opacity-50"
              >
                {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
                {copied ? "Copied" : "Copy the list of merge fields"}
              </button>
            </div>
          </div>

          {GROUPS.map((group) => {
            const items = mergeFields.filter((f) => f.source === group.source);
            if (items.length === 0) return null;
            return (
              <div key={group.source}>
                <p className={LABEL}>{group.title}</p>
                <div className="mt-1.5 flex flex-wrap gap-1.5">
                  {items.map((f) => (
                    <button
                      key={f.key}
                      type="button"
                      disabled={readOnly}
                      onClick={() => insert(`{{${f.key}}}`)}
                      title={`${f.description}\n{{${f.key}}}`}
                      className={`rounded-full border px-2.5 py-1 text-xs font-semibold transition disabled:pointer-events-none disabled:opacity-60 ${
                        used.includes(f.key)
                          ? "border-primary/40 bg-primary/10 text-primary"
                          : "border-border/60 bg-card text-muted-foreground hover:border-primary/40 hover:text-foreground"
                      }`}
                    >
                      {f.label}
                    </button>
                  ))}
                </div>
              </div>
            );
          })}

          <div>
            <p className={LABEL}>Typed for each letter</p>
            <p className="mt-1 text-xs text-muted-foreground">
              For details not on file — an offer expiry date, a last working day. The generate dialog
              asks for each one.
            </p>
            {inputs.length > 0 ? (
              <div className="mt-2 flex flex-wrap gap-1.5">
                {inputs.map((k) => (
                  <button
                    key={k}
                    type="button"
                    disabled={readOnly}
                    onClick={() => insert(`{{${k}}}`)}
                    className="rounded-full border border-dashed border-warning-foreground/40 bg-warning/20 px-2.5 py-1 text-xs font-semibold text-warning-foreground transition hover:bg-warning/40 disabled:pointer-events-none"
                  >
                    {k.slice("input.".length)}
                  </button>
                ))}
              </div>
            ) : null}
            {readOnly ? null : (
              <div className="mt-2 flex gap-2">
                <input
                  value={inputName}
                  onChange={(e) => setInputName(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      insertInput();
                    }
                  }}
                  placeholder="e.g. Last working day"
                  className="h-9 min-w-0 flex-1 rounded-xl border border-border bg-card px-3 text-xs text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                />
                <button
                  type="button"
                  onClick={insertInput}
                  disabled={inputName.trim() === ""}
                  aria-label="Insert typed field"
                  className="grid h-9 w-9 shrink-0 place-items-center rounded-xl border border-border bg-card text-foreground transition hover:border-primary hover:text-primary disabled:pointer-events-none disabled:opacity-50"
                >
                  <Plus className="h-4 w-4" />
                </button>
              </div>
            )}
            <p className="mt-1.5 text-[11px] text-muted-foreground">
              A name ending in "date" or "day" is asked for as a date.
            </p>
          </div>
        </aside>
      </div>
      {confirmDialog}
    </div>
  );
}

// "Replace with…" for each placeholder that isn't a merge field — typically
// {{Employee Name}} / {{IC}} from a Word file. Rewrites every occurrence in the
// body to a registry field, or to an {{input.xxx}} typed for each letter.
function PlaceholderPicker({
  unknown,
  mergeFields,
  onReplace,
}: {
  unknown: string[];
  mergeFields: MergeField[];
  onReplace: (from: string, to: string) => void;
}) {
  if (unknown.length === 0) return null;
  return (
    <div className="space-y-2">
      <p className="text-xs font-semibold text-foreground">Map these placeholders to merge fields:</p>
      {unknown.map((key) => {
        const asInput = inputKeyFor(key);
        return (
          <div key={key} className="flex flex-wrap items-center gap-2">
            <code className="min-w-0 max-w-full truncate rounded-lg bg-muted px-2 py-1 font-mono text-xs text-foreground">
              {`{{${key}}}`}
            </code>
            <span className="text-xs text-muted-foreground">→</span>
            <Select value="" onValueChange={(to) => onReplace(key, to)}>
              <SelectTrigger className="h-9 w-64 max-w-full bg-card text-xs">
                <SelectValue placeholder="Replace with…" />
              </SelectTrigger>
              <SelectContent>
                {asInput ? (
                  <SelectGroup>
                    <SelectLabel>Typed for each letter</SelectLabel>
                    <SelectItem value={asInput}>{`{{${asInput}}}`}</SelectItem>
                  </SelectGroup>
                ) : null}
                {GROUPS.map((group) => {
                  const items = mergeFields.filter((f) => f.source === group.source);
                  if (items.length === 0) return null;
                  return (
                    <SelectGroup key={group.source}>
                      <SelectLabel>{group.title}</SelectLabel>
                      {items.map((f) => (
                        <SelectItem key={f.key} value={f.key}>
                          {f.label}
                        </SelectItem>
                      ))}
                    </SelectGroup>
                  );
                })}
              </SelectContent>
            </Select>
          </div>
        );
      })}
    </div>
  );
}

// "Ask AI" beside the editor: say what to change and the AI revises the draft
// on screen (one version, not two — the admin is already working on this one).
// Undo puts back the text from before the last revision.
function AskAiPanel({
  initialConversation,
  body,
  name,
  category,
  onRevised,
  onUndo,
}: {
  initialConversation: TemplateAiMessage[];
  body: string;
  name: string;
  category: DocumentCategory;
  onRevised: (draft: TemplateAiDraft) => void;
  onUndo: (previousBody: string) => void;
}) {
  const [conversation, setConversation] = useState<TemplateAiMessage[]>(initialConversation);
  const [request, setRequest] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [previousBody, setPreviousBody] = useState<string | null>(null);

  async function ask() {
    const text = request.trim();
    if (!text || busy) return;
    const messages: TemplateAiMessage[] = [...conversation, { role: "user", text }];
    setBusy(true);
    setError(null);
    try {
      const result = await draftTemplateWithAi({
        messages,
        // A blank editor gets a fresh draft; otherwise this text is revised.
        currentBody: body.trim() ? body : undefined,
        currentName: name.trim() || undefined,
        currentCategory: category,
      });
      const draft = result.drafts[0];
      if (!draft) throw new Error("The AI didn't return a draft.");
      setPreviousBody(body);
      onRevised(draft);
      setConversation([...messages, { role: "assistant", text: result.reply || "Done." }]);
      setRequest("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "The AI couldn't change the template just now.");
    } finally {
      setBusy(false);
    }
  }

  // Only the AI's own replies and the admin's requests — the drafts themselves
  // are in the editor.
  const recent = conversation.slice(-4);

  return (
    <div className="space-y-2 rounded-2xl border border-primary/30 bg-primary/5 p-3">
      <h3 className="flex items-center gap-1.5 text-sm font-black text-foreground">
        <WandSparkles className="h-4 w-4 text-primary" />
        Ask AI
      </h3>
      <p className="text-[11px] text-muted-foreground">
        Say what to change — "make it more formal", "add a clause on annual leave", "translate to
        Bahasa Melayu". It rewrites the text on the left.
      </p>
      {recent.length > 0 ? (
        <ol className="nice-scrollbar max-h-40 space-y-1.5 overflow-y-auto">
          {recent.map((m, i) => (
            <li
              key={i}
              className={`rounded-xl px-2.5 py-1.5 text-[11px] ${
                m.role === "user" ? "bg-card text-foreground" : "bg-primary/10 text-foreground"
              }`}
            >
              {m.text}
            </li>
          ))}
        </ol>
      ) : null}
      <textarea
        value={request}
        onChange={(e) => setRequest(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) void ask();
        }}
        rows={3}
        maxLength={4000}
        disabled={busy}
        placeholder="e.g. Add a paragraph about the 3-month notice period."
        className="w-full resize-y rounded-xl border border-border bg-card p-2.5 text-xs text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
      />
      {error ? <p className="text-[11px] font-medium text-destructive">{error}</p> : null}
      <div className="flex items-center justify-between gap-2">
        {previousBody !== null ? (
          <button
            type="button"
            onClick={() => {
              onUndo(previousBody);
              setPreviousBody(null);
            }}
            disabled={busy}
            className="inline-flex items-center gap-1 text-[11px] font-semibold text-muted-foreground hover:text-foreground"
          >
            <Undo2 className="h-3 w-3" />
            Undo last change
          </button>
        ) : (
          <span />
        )}
        <button
          type="button"
          onClick={() => void ask()}
          disabled={busy || request.trim() === ""}
          className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
        >
          {busy ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <WandSparkles className="h-3.5 w-3.5" />}
          {busy ? "Writing…" : "Ask"}
        </button>
      </div>
    </div>
  );
}
