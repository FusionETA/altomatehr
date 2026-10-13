import { useState } from "react";
import { createPortal } from "react-dom";
import { AlertTriangle, LoaderCircle, RefreshCw, Sparkles, X } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import {
  draftTemplateWithAi,
  type MergeField,
  type TemplateAiDraft,
  type TemplateAiMessage,
} from "../api";
import { categoryLabel } from "../lib/categories";
import { LetterPreview } from "./LetterPreview";

const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";
const TEXTAREA =
  "w-full resize-y rounded-2xl border border-border bg-card p-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";

// Things an admin often asks for — a click fills the box, they edit from there.
const EXAMPLES = [
  "An offer letter for a site engineer: 3-month probation, salary, start date and who they report to.",
  "A confirmation of employment letter after probation.",
  "A first written warning for repeated lateness.",
  "Surat penerimaan peletakan jawatan dalam Bahasa Melayu, dengan tarikh hari terakhir bekerja.",
];

// "Write with AI": describe a letter, get two drafts to choose from, ask for
// changes until one fits, then open it in the editor — unsaved, like an upload.
// The AI only ever sees this conversation and the field list, never an
// employee's details.
export function AiTemplateDialog({
  mergeFields,
  onClose,
  onChoose,
}: {
  mergeFields: MergeField[];
  onClose: () => void;
  onChoose: (draft: TemplateAiDraft, conversation: TemplateAiMessage[]) => void;
}) {
  useBodyScrollLock();
  const [request, setRequest] = useState("");
  const [conversation, setConversation] = useState<TemplateAiMessage[]>([]);
  const [drafts, setDrafts] = useState<TemplateAiDraft[]>([]);
  const [writing, setWriting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function write() {
    const text = request.trim();
    if (!text || writing) return;
    const messages: TemplateAiMessage[] = [...conversation, { role: "user", text }];
    setWriting(true);
    setError(null);
    try {
      const result = await draftTemplateWithAi({ messages });
      setConversation([...messages, { role: "assistant", text: result.reply || "Here are two drafts." }]);
      setDrafts(result.drafts);
      setRequest("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "The AI couldn't write the template just now.");
    } finally {
      setWriting(false);
    }
  }

  const started = conversation.length > 0;

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="ai-template-title"
    >
      <section className="nice-scrollbar flex max-h-[92vh] w-full max-w-[1040px] flex-col overflow-hidden rounded-[26px] border border-border/60 bg-card shadow-2xl">
        <header className="flex items-start justify-between gap-4 border-b border-border/60 px-6 py-5">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              New template
            </p>
            <h3 id="ai-template-title" className="mt-1 flex items-center gap-2 text-xl font-black text-foreground">
              <Sparkles className="h-5 w-5 text-primary" />
              Write with AI
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={writing}
            aria-label="Close"
            className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="nice-scrollbar flex-1 space-y-5 overflow-y-auto px-6 py-5">
          {!started ? (
            <p className="text-sm text-muted-foreground">
              Describe the letter — who it's for, what it should say, and in English or Bahasa Melayu.
              You'll get two drafts to choose from, using your merge fields for the employee's and
              company's details. Nothing is saved until you save the template.
            </p>
          ) : (
            <ol className="space-y-2">
              {conversation.map((m, i) => (
                <li
                  key={i}
                  className={`max-w-[85%] rounded-2xl px-4 py-2.5 text-sm ${
                    m.role === "user"
                      ? "ml-auto bg-primary/10 text-foreground"
                      : "bg-surface-low/80 text-foreground"
                  }`}
                >
                  {m.text}
                </li>
              ))}
            </ol>
          )}

          {drafts.length > 0 ? (
            <div className={`grid gap-4 ${drafts.length > 1 ? "lg:grid-cols-2" : ""}`}>
              {drafts.map((d, i) => (
                <article key={i} className="flex min-w-0 flex-col rounded-3xl border border-border/70 bg-background">
                  <div className="flex items-start justify-between gap-3 border-b border-border/60 px-4 py-3">
                    <div className="min-w-0">
                      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                        Draft {i + 1} · {categoryLabel(d.category)}
                      </p>
                      <h4 className="truncate text-sm font-black text-foreground">{d.name}</h4>
                    </div>
                    <button
                      type="button"
                      onClick={() => onChoose(d, conversation)}
                      disabled={writing}
                      className="shrink-0 rounded-2xl bg-primary px-3.5 py-2 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
                    >
                      Use this draft
                    </button>
                  </div>
                  <div className="nice-scrollbar max-h-[420px] overflow-y-auto px-5 py-4">
                    <LetterPreview body={d.body} fields={mergeFields} />
                  </div>
                  {d.unknownFields.length > 0 || d.warnings.length > 0 ? (
                    <div className="flex items-start gap-2 border-t border-border/60 px-4 py-2.5 text-xs text-warning-foreground">
                      <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                      <span>
                        {[
                          ...d.warnings,
                          ...(d.unknownFields.length > 0
                            ? [`${d.unknownFields.length} field${d.unknownFields.length === 1 ? "" : "s"} to fix in the editor: ${d.unknownFields.map((k) => `{{${k}}}`).join(", ")}`]
                            : []),
                        ].join(" ")}
                      </span>
                    </div>
                  ) : null}
                </article>
              ))}
            </div>
          ) : null}

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
        </div>

        <footer className="space-y-3 border-t border-border/60 px-6 py-4">
          <label className="block">
            <span className={LABEL}>{started ? "Ask for changes" : "Describe the letter"}</span>
            <textarea
              value={request}
              onChange={(e) => setRequest(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) void write();
              }}
              maxLength={4000}
              rows={started ? 2 : 3}
              disabled={writing}
              placeholder={
                started
                  ? "e.g. Make draft 2 shorter, and mention the annual leave entitlement."
                  : "e.g. An offer letter for a site engineer with a 3-month probation…"
              }
              className={`${TEXTAREA} mt-1.5`}
            />
          </label>
          <div className="flex flex-wrap items-center justify-between gap-3">
            {!started ? (
              <div className="flex flex-wrap gap-1.5">
                {EXAMPLES.map((ex) => (
                  <button
                    key={ex}
                    type="button"
                    onClick={() => setRequest(ex)}
                    disabled={writing}
                    title={ex}
                    className="max-w-[230px] truncate rounded-full border border-border/60 bg-card px-3 py-1 text-xs font-semibold text-muted-foreground transition hover:border-primary/40 hover:text-foreground"
                  >
                    {ex}
                  </button>
                ))}
              </div>
            ) : (
              <p className="text-xs text-muted-foreground">
                Pick a draft to open it in the editor — you can keep asking the AI for changes there.
              </p>
            )}
            <button
              type="button"
              onClick={() => void write()}
              disabled={writing || request.trim() === ""}
              className="ml-auto inline-flex items-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
            >
              {writing ? (
                <LoaderCircle className="h-4 w-4 animate-spin" />
              ) : started ? (
                <RefreshCw className="h-4 w-4" />
              ) : (
                <Sparkles className="h-4 w-4" />
              )}
              {writing ? "Writing…" : started ? "Write two new drafts" : "Write two drafts"}
            </button>
          </div>
        </footer>
      </section>
    </div>,
    document.body,
  );
}
