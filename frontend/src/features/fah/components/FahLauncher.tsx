import { Fragment, useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, CircleAlert, Download, FileText, History, LoaderCircle, Paperclip, Plus, SendHorizontal, Sparkles, X } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  FAH_STATUS_PATH,
  answerFahChange,
  downloadFahImportFile,
  fahMessageNotSent,
  getFahConversation,
  getFahStatus,
  listFahConversations,
  removeFahAttachment,
  sendFahMessage,
  uploadFahAttachment,
  type FahCompanyRow,
  type FahConversationSummary,
  type FahImportCard,
  type FahPendingAction,
  type FahProgress,
  type FahReply,
} from "../api";
import { ACCEPT, MAX_FILES_PER_MESSAGE, droppedFolders, shrinkImage } from "../lib/attach";
import { toolLabel } from "../lib/tool-labels";
import { FahText } from "./FahText";

type ChangeState =
  | "waiting"
  | "confirming"
  | "cancelling"
  | "made"
  | "cancelled"
  | "dropped"
  | "failed"
  | "unknown";

// A file shown on a sent message. Preview: an image's local copy, this
// session only — a reopened chat has the names.
type Attached = { name: string; preview?: string };

type Entry =
  | { kind: "user"; text: string; attachments: Attached[] }
  | { kind: "assistant"; text: string }
  | { kind: "change"; action: FahPendingAction; state: ChangeState };

type Step = { tool: string; done: boolean; ok: boolean };

// A file in the composer: uploading as soon as it's added, so it's ready by
// the time the message is.
type Pending = {
  key: number;
  name: string;
  status: "uploading" | "ready" | "error";
  id?: string;
  error?: string;
  note?: string | null;
  preview?: string;
};

const SUGGESTIONS = [
  "What's waiting for me?",
  "Who's on leave this week?",
  "Show this month's payroll totals",
];

// FAH ("Friendly AltomateHR"), the assistant: a button in the corner of every
// admin screen that opens a chat. Hidden when FAH isn't set up on the server.
//
// Portaled to the body for the same reason as FloatingActionButton: the
// shell's enter animation makes its wrapper the containing block for anything
// position:fixed inside it. z-30 keeps it under the unsaved-changes bar.
export function FahLauncher() {
  const status = useCachedQuery(FAH_STATUS_PATH, getFahStatus);
  const [open, setOpen] = useState(false);
  const close = useCallback(() => setOpen(false), []);

  if (!status.data?.available) return null;

  return createPortal(
    <>
      {!open ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          aria-label="Ask FAH, the AltomateHR assistant"
          className="fixed bottom-5 right-5 z-30 flex h-12 items-center gap-2 rounded-full bg-primary px-4 text-sm font-semibold text-primary-foreground shadow-panel transition-transform hover:scale-105 active:scale-95 lg:bottom-8 lg:right-8"
        >
          <Sparkles className="h-4 w-4" aria-hidden />
          Ask FAH
        </button>
      ) : null}
      {/* Kept mounted while closed so a chat survives closing the panel. */}
      <FahPanel open={open} onClose={close} />
    </>,
    document.body,
  );
}

function FahPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [entries, setEntries] = useState<Entry[]>([]);
  const [steps, setSteps] = useState<Step[]>([]);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [input, setInput] = useState("");
  const [history, setHistory] = useState<FahConversationSummary[] | null>(null);
  const [showHistory, setShowHistory] = useState(false);
  const [files, setFiles] = useState<Pending[]>([]);
  const [dragging, setDragging] = useState(false);
  const [expired, setExpired] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const nextKey = useRef(0);
  // Files removed while still uploading: taken back once their upload lands.
  const dropped = useRef(new Set<number>());

  const uploading = files.some((f) => f.status === "uploading");
  const ready = files.filter((f) => f.status === "ready");

  useEffect(() => {
    if (!open) return;
    inputRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [entries, steps, running]);

  function onProgress(e: FahProgress) {
    setSteps((current) =>
      e.type === "started"
        ? [...current, { tool: e.tool, done: false, ok: true }]
        : current.map((s) => (s.tool === e.tool && !s.done ? { ...s, done: true, ok: e.ok !== false } : s)),
    );
  }

  function apply(reply: FahReply) {
    setConversationId(reply.conversationId);
    setEntries((current) => [
      ...current,
      ...(reply.reply ? [{ kind: "assistant" as const, text: reply.reply }] : []),
      ...(reply.pendingAction ? [{ kind: "change" as const, action: reply.pendingAction, state: "waiting" as const }] : []),
    ]);
  }

  async function run(work: () => Promise<FahReply>, onFail?: (err: unknown) => void, onDone?: (reply: FahReply) => void) {
    setRunning(true);
    setSteps([]);
    setError(null);
    try {
      const reply = await work();
      onDone?.(reply);
      apply(reply);
    } catch (err) {
      onFail?.(err);
      setError(err instanceof Error ? err.message : "FAH couldn't answer that. Try again.");
    } finally {
      setRunning(false);
      setSteps([]);
    }
  }

  function setChange(actionId: string, state: ChangeState) {
    setEntries((current) =>
      current.map((e) => (e.kind === "change" && e.action.id === actionId ? { ...e, state } : e)),
    );
  }

  function addFiles(list: File[]) {
    if (list.length === 0) return;
    const room = MAX_FILES_PER_MESSAGE - files.length;
    if (list.length > room) {
      setError(`Attach up to ${MAX_FILES_PER_MESSAGE} files to one message.`);
      list = list.slice(0, Math.max(room, 0));
    }
    for (const file of list) {
      const key = nextKey.current++;
      const preview = file.type.startsWith("image/") ? URL.createObjectURL(file) : undefined;
      setFiles((current) => [...current, { key, name: file.name, status: "uploading", preview }]);
      void (async () => {
        try {
          const attachment = await uploadFahAttachment(await shrinkImage(file));
          if (dropped.current.delete(key)) {
            void removeFahAttachment(attachment.id).catch(() => undefined);
            return;
          }
          setFiles((current) =>
            current.map((f) =>
              f.key === key ? { ...f, status: "ready", id: attachment.id, name: attachment.fileName, note: attachment.note } : f,
            ),
          );
        } catch (err) {
          dropped.current.delete(key);
          const message = err instanceof Error ? err.message : `${file.name} couldn't be attached.`;
          setFiles((current) => current.map((f) => (f.key === key ? { ...f, status: "error", error: message } : f)));
        }
      })();
    }
  }

  function removeFile(file: Pending) {
    if (file.status === "uploading") dropped.current.add(file.key);
    if (file.id) void removeFahAttachment(file.id).catch(() => undefined);
    if (file.preview) URL.revokeObjectURL(file.preview);
    setFiles((current) => current.filter((f) => f.key !== file.key));
  }

  function onDrop(e: React.DragEvent) {
    e.preventDefault();
    setDragging(false);
    if (running || expired) return;
    const items = Array.from(e.dataTransfer.items).filter((item) => item.kind === "file");
    if (droppedFolders(e.dataTransfer) > 0) setError("Folders can't be attached. Open the folder and attach the files inside.");
    addFiles(
      items
        .filter((item) => !item.webkitGetAsEntry?.()?.isDirectory)
        .map((item) => item.getAsFile())
        .filter((file): file is File => file !== null),
    );
  }

  function send(text: string) {
    const message = text.trim();
    if ((!message && ready.length === 0) || running || uploading || expired) return;
    const sending = ready;
    const entry: Entry = {
      kind: "user",
      text: message,
      attachments: sending.map((f) => ({ name: f.name, preview: f.preview })),
    };
    setInput("");
    // Files that couldn't be attached go with nothing; clear them too.
    setFiles([]);
    setEntries((current) => [...current, entry]);
    void run(
      () => sendFahMessage({ conversationId, message, attachmentIds: sending.map((f) => f.id!) }, onProgress),
      // Not in the chat at all (a file FAH couldn't read, a limit): put it
      // back to fix and send again.
      (err) => {
        if (!fahMessageNotSent(err)) return;
        setEntries((current) => current.filter((e) => e !== entry));
        setInput(message);
        setFiles(sending);
      },
      // Moving on drops a change still waiting — but only once the server
      // took the message; a refused send leaves the change waiting there too.
      () =>
        setEntries((current) =>
          current.map((e) => (e.kind === "change" && e.state === "waiting" ? { ...e, state: "dropped" as const } : e)),
        ),
    );
  }

  function answer(action: FahPendingAction, approve: boolean) {
    if (!conversationId || running) return;
    setChange(action.id, approve ? "confirming" : "cancelling");
    void run(
      () => answerFahChange({ conversationId, actionId: action.id, approve }, onProgress),
      // No answer at all (the connection dropped): a Confirm may still have
      // gone through on the server, so don't claim it didn't.
      () => setChange(action.id, approve ? "unknown" : "failed"),
      // The server says what happened to the change, even when FAH's reply
      // after it failed.
      (reply) => {
        const outcome = reply.changeOutcome;
        setChange(
          action.id,
          outcome === "made" || outcome === "cancelled" || outcome === "failed" || outcome === "unknown"
            ? outcome
            : approve
              ? "made"
              : "cancelled",
        );
      },
    );
  }

  function newChat() {
    if (running) return;
    setConversationId(null);
    setEntries([]);
    setError(null);
    setExpired(false);
    setShowHistory(false);
    inputRef.current?.focus();
  }

  async function openHistory() {
    setShowHistory(true);
    setHistory(null);
    try {
      setHistory(await listFahConversations());
    } catch {
      setHistory([]);
    }
  }

  async function openConversation(id: string) {
    // A turn still running would put its answer into the chat opened instead.
    if (running) return;
    setShowHistory(false);
    setError(null);
    try {
      const chat = await getFahConversation(id);
      setConversationId(chat.id);
      setExpired(chat.filesExpired);
      setEntries([
        ...chat.messages.map(
          (m): Entry =>
            m.role === "user"
              ? { kind: "user", text: m.text, attachments: (m.attachments ?? []).map((name) => ({ name })) }
              : { kind: "assistant", text: m.text },
        ),
        ...(chat.pendingAction ? [{ kind: "change" as const, action: chat.pendingAction, state: "waiting" as const }] : []),
      ]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Couldn't open that chat.");
    }
  }

  return (
    <div
      role="dialog"
      aria-label="FAH, the AltomateHR assistant"
      aria-hidden={!open}
      onDragOver={(e) => {
        if (!e.dataTransfer.types.includes("Files")) return;
        e.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDragging(false);
      }}
      onDrop={onDrop}
      className={`${open ? "flex" : "hidden"} fixed inset-0 z-50 flex-col bg-background lg:inset-auto lg:bottom-8 lg:right-8 lg:h-[min(640px,calc(100vh-4rem))] lg:w-[400px] lg:overflow-hidden lg:rounded-[28px] lg:border lg:border-border/70 lg:shadow-2xl`}
    >
      {dragging ? (
        <div className="pointer-events-none absolute inset-2 z-10 flex items-center justify-center rounded-[24px] border-2 border-dashed border-primary bg-background/90 text-sm font-semibold text-primary">
          Drop files for FAH
        </div>
      ) : null}
      <header className="flex items-center gap-2 border-b border-border/60 px-4 py-3">
        <span className="flex size-9 items-center justify-center rounded-full bg-primary/10 text-primary">
          <Sparkles className="size-4" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-black text-foreground">FAH</p>
          <p className="text-xs text-muted-foreground">Friendly AltomateHR</p>
        </div>
        <IconButton
          label="Past chats"
          onClick={() => (showHistory ? setShowHistory(false) : void openHistory())}
          disabled={running}
        >
          <History className="size-4" />
        </IconButton>
        <IconButton label="New chat" onClick={newChat} disabled={running}>
          <Plus className="size-4" />
        </IconButton>
        <IconButton label="Close" onClick={onClose}>
          <X className="size-4" />
        </IconButton>
      </header>

      {showHistory ? (
        <div className="min-h-0 flex-1 overflow-y-auto p-3">
          <p className="px-2 pb-2 text-xs font-semibold uppercase tracking-wider text-muted-foreground">Past chats</p>
          {history === null ? (
            <div className="flex items-center gap-2 px-2 py-6 text-sm text-muted-foreground">
              <LoaderCircle className="size-4 animate-spin" /> Loading…
            </div>
          ) : history.length === 0 ? (
            <p className="px-2 py-6 text-sm text-muted-foreground">No chats yet.</p>
          ) : (
            <ul className="space-y-1">
              {history.map((c) => (
                <li key={c.id}>
                  <button
                    type="button"
                    onClick={() => void openConversation(c.id)}
                    className="w-full rounded-2xl px-3 py-2.5 text-left transition hover:bg-muted"
                  >
                    <span className="block truncate text-sm font-medium text-foreground">{c.title}</span>
                    <span className="block text-xs text-muted-foreground">
                      {new Date(c.updatedAt).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : (
        <div ref={scrollRef} className="min-h-0 flex-1 space-y-3 overflow-y-auto px-4 py-4">
          {entries.length === 0 && !running ? (
            <div className="space-y-3 pt-4">
              <p className="text-sm text-muted-foreground">
                Hi! Ask me about your people, leave, claims, attendance or payroll. I can also add or update employees
                and prepare payroll changes — you confirm every change before it&apos;s made. Attach screenshots, PDFs,
                spreadsheets or Word files and I&apos;ll read them too.
              </p>
              <div className="flex flex-wrap gap-2">
                {SUGGESTIONS.map((s) => (
                  <button
                    key={s}
                    type="button"
                    onClick={() => send(s)}
                    className="rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted"
                  >
                    {s}
                  </button>
                ))}
              </div>
            </div>
          ) : null}

          {entries.map((entry, i) =>
            entry.kind === "user" ? (
              <div key={i} className="flex flex-col items-end gap-1.5">
                {entry.attachments.length > 0 ? (
                  <div className="flex max-w-[85%] flex-wrap justify-end gap-1.5">
                    {entry.attachments.map((a, j) =>
                      a.preview ? (
                        <img
                          key={j}
                          src={a.preview}
                          alt={a.name}
                          className="h-20 max-w-[160px] rounded-xl border border-border/70 object-cover"
                        />
                      ) : (
                        <span
                          key={j}
                          className="inline-flex max-w-full items-center gap-1.5 rounded-xl border border-border/70 bg-card px-2.5 py-1.5 text-xs text-foreground"
                        >
                          <FileText className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
                          <span className="truncate">{a.name}</span>
                        </span>
                      ),
                    )}
                  </div>
                ) : null}
                {entry.text ? (
                  <p className="max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-br-md bg-primary px-3.5 py-2 text-sm text-primary-foreground">
                    {entry.text}
                  </p>
                ) : null}
              </div>
            ) : entry.kind === "assistant" ? (
              <div key={i} className="max-w-[92%] text-sm leading-relaxed text-foreground">
                <FahText text={entry.text} />
              </div>
            ) : (
              <ChangeCard
                key={entry.action.id}
                action={entry.action}
                state={entry.state}
                disabled={running}
                onAnswer={(approve) => answer(entry.action, approve)}
                onDownload={
                  conversationId
                    ? () =>
                        downloadFahImportFile(conversationId, entry.action.id).catch((err) =>
                          setError(err instanceof Error ? err.message : "Couldn't download the import file."),
                        )
                    : undefined
                }
              />
            ),
          )}

          {running ? (
            <div className="space-y-1.5 text-xs text-muted-foreground">
              {steps.map((s, i) => (
                <p key={i} className="flex items-center gap-2">
                  {!s.done ? (
                    <LoaderCircle className="size-3.5 animate-spin" />
                  ) : s.ok ? (
                    <Check className="size-3.5 text-success" />
                  ) : (
                    <CircleAlert className="size-3.5 text-destructive" />
                  )}
                  {toolLabel(s.tool)}
                  {s.done ? "" : "…"}
                </p>
              ))}
              {steps.every((s) => s.done) ? (
                <p className="flex items-center gap-2">
                  <LoaderCircle className="size-3.5 animate-spin" /> Thinking…
                </p>
              ) : null}
            </div>
          ) : null}

          {error ? (
            <p className="rounded-2xl border border-destructive/20 bg-destructive/5 px-3 py-2 text-sm text-destructive">{error}</p>
          ) : null}
        </div>
      )}

      <form
        onSubmit={(e) => {
          e.preventDefault();
          send(input);
        }}
        className="border-t border-border/60 p-3"
      >
        {expired ? (
          <p className="mb-2 rounded-2xl bg-muted px-3 py-2 text-xs text-muted-foreground">
            The files in this chat were deleted after 30 days, so it can&apos;t be continued. Start a new chat.
          </p>
        ) : null}
        {files.length > 0 ? (
          <div className="mb-2 space-y-1.5">
            <div className="flex flex-wrap gap-1.5">
              {files.map((f) => (
                <FileChip key={f.key} file={f} onRemove={() => removeFile(f)} />
              ))}
            </div>
            {files
              .filter((f) => f.error || f.note)
              .map((f) => (
                <p key={f.key} className={`text-xs ${f.error ? "text-destructive" : "text-muted-foreground"}`}>
                  {f.error ?? `${f.name}: ${f.note}`}
                </p>
              ))}
          </div>
        ) : null}
        <div className="flex items-end gap-2">
          <input
            ref={fileRef}
            type="file"
            multiple
            accept={ACCEPT}
            className="hidden"
            onChange={(e) => {
              addFiles(Array.from(e.target.files ?? []));
              e.target.value = "";
            }}
          />
          <IconButton
            label="Attach images, PDF, Excel, CSV or Word files"
            onClick={() => fileRef.current?.click()}
            disabled={running || expired || files.length >= MAX_FILES_PER_MESSAGE}
          >
            <Paperclip className="size-4" />
          </IconButton>
          <textarea
            ref={inputRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                send(input);
              }
            }}
            onPaste={(e) => {
              // A pasted screenshot attaches, as in other chat apps.
              const pasted = Array.from(e.clipboardData.files);
              if (pasted.length === 0 || running || expired) return;
              e.preventDefault();
              addFiles(pasted);
            }}
            rows={1}
            maxLength={4000}
            disabled={expired}
            placeholder={expired ? "This chat can't be continued" : "Ask FAH…"}
            aria-label="Message FAH"
            className="max-h-32 min-h-10 flex-1 resize-none rounded-2xl border border-border bg-card px-3.5 py-2.5 text-base text-foreground lg:text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
          />
          <button
            type="submit"
            aria-label="Send"
            disabled={running || uploading || expired || (input.trim().length === 0 && ready.length === 0)}
            className="flex size-10 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
          >
            {running || uploading ? <LoaderCircle className="size-4 animate-spin" /> : <SendHorizontal className="size-4" />}
          </button>
        </div>
      </form>
    </div>
  );
}

// AltomateHR's own preview of a change, with the only way to make it.
function ChangeCard({
  action,
  state,
  disabled,
  onAnswer,
  onDownload,
}: {
  action: FahPendingAction;
  state: ChangeState;
  disabled: boolean;
  onAnswer: (approve: boolean) => void;
  onDownload?: () => Promise<void>;
}) {
  const waiting = state === "waiting" || state === "confirming" || state === "cancelling";
  const importing = action.import ? action.import.adds + action.import.updates : null;
  const companies = action.companies ? action.companies.filter((c) => !c.skip).length : null;
  const outcome: Partial<Record<ChangeState, string>> = {
    made: "Confirmed",
    cancelled: "Cancelled — nothing was changed",
    dropped: "Not made — you moved on",
    failed: "Not made",
    unknown: "May have been made — check before trying again",
  };

  return (
    <div className="rounded-2xl border border-primary/30 bg-primary/5 p-3.5">
      <p className="text-xs font-semibold uppercase tracking-wider text-primary">{action.title}</p>
      <p className="mt-1.5 whitespace-pre-wrap text-sm text-foreground">{action.preview}</p>
      {action.import ? <ImportRows card={action.import} /> : null}
      {action.companies ? <CompanyRows rows={action.companies} /> : null}
      {waiting ? (
        <div className="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => onAnswer(true)}
            disabled={disabled || importing === 0 || companies === 0}
            className="inline-flex items-center gap-1.5 rounded-full bg-primary px-4 py-1.5 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-60"
          >
            {state === "confirming" ? <LoaderCircle className="size-3.5 animate-spin" /> : <Check className="size-3.5" />}
            {importing !== null
              ? `Import ${importing} ${importing === 1 ? "person" : "people"}`
              : companies !== null
                ? `Confirm for ${companies} ${companies === 1 ? "company" : "companies"}`
                : "Confirm"}
          </button>
          <button
            type="button"
            onClick={() => onAnswer(false)}
            disabled={disabled}
            className="inline-flex items-center gap-1.5 rounded-full border border-border/70 bg-card px-4 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-60"
          >
            {state === "cancelling" ? <LoaderCircle className="size-3.5 animate-spin" /> : null}
            Cancel
          </button>
          {action.import && onDownload ? <DownloadButton onDownload={onDownload} /> : null}
        </div>
      ) : (
        <p className="mt-2 text-xs font-semibold text-muted-foreground">{outcome[state]}</p>
      )}
    </div>
  );
}

// A change in several companies: one line each — made there, or skipped and why.
function CompanyRows({ rows }: { rows: FahCompanyRow[] }) {
  return (
    <ul className="mt-2.5 max-h-64 divide-y divide-border/50 overflow-auto rounded-xl border border-border/70 bg-card">
      {rows.map((r, i) => (
        <li key={i} className="flex gap-2 px-2.5 py-2 text-xs">
          <span
            className={`h-fit shrink-0 rounded-full px-2 py-0.5 font-semibold ${
              r.skip ? "bg-muted text-muted-foreground" : "bg-success/10 text-success"
            }`}
          >
            {r.skip ? "Skip" : "Do"}
          </span>
          <span className="min-w-0">
            <span className="font-semibold text-foreground">{r.company}</span>
            <span className="text-muted-foreground"> — {r.detail}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}

const ROW_STATUS: Record<FahImportCard["rows"][number]["status"], { label: string; className: string }> = {
  add: { label: "Add", className: "bg-success/10 text-success" },
  update: { label: "Update", className: "bg-primary/10 text-primary" },
  problem: { label: "Problem", className: "bg-destructive/10 text-destructive" },
  leftout: { label: "Left out", className: "bg-muted text-muted-foreground" },
};

// The rows of an import card: what happens to each, and why a row won't go in.
function ImportRows({ card }: { card: FahImportCard }) {
  return (
    <div className="mt-2.5 space-y-2">
      <p className="text-xs text-muted-foreground">
        Rows with problems aren&apos;t imported — fix them in the download, or tell FAH. People already here are
        matched by email; blank cells don&apos;t change them. No emails are sent.
      </p>
      <div className="max-h-64 overflow-auto rounded-xl border border-border/70 bg-card">
        <table className="w-full text-left text-xs">
          <thead className="sticky top-0 bg-card text-muted-foreground">
            <tr>
              <th className="px-2 py-1.5 font-semibold">Row</th>
              <th className="px-2 py-1.5 font-semibold" />
              {card.columns.map((c) => (
                <th key={c} className="whitespace-nowrap px-2 py-1.5 font-semibold">
                  {c}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {card.rows.map((r) => (
              <Fragment key={r.row}>
                <tr className={r.message ? "" : "border-b border-border/50 last:border-0"}>
                  <td className="px-2 py-1.5 tabular-nums text-muted-foreground">{r.row}</td>
                  <td className="px-2 py-1.5">
                    <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-semibold ${ROW_STATUS[r.status].className}`}>
                      {ROW_STATUS[r.status].label}
                    </span>
                  </td>
                  {r.cells.map((cell, i) => (
                    <td key={i} className="whitespace-nowrap px-2 py-1.5 text-foreground">
                      {cell}
                    </td>
                  ))}
                </tr>
                {r.message ? (
                  <tr className="border-b border-border/50 last:border-0">
                    <td />
                    <td
                      colSpan={card.columns.length + 1}
                      className={`px-2 pb-1.5 ${r.status === "add" || r.status === "update" ? "text-muted-foreground" : "text-destructive"}`}
                    >
                      {/* Pinned and narrow, so it wraps where it can be read rather than off to the right. */}
                      <div className="sticky left-2 max-w-[17rem] whitespace-normal">{r.message}</div>
                    </td>
                  </tr>
                ) : null}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
      {card.moreRows > 0 ? (
        <p className="text-xs text-muted-foreground">And {card.moreRows} more rows — the download has them all.</p>
      ) : null}
    </div>
  );
}

function DownloadButton({ onDownload }: { onDownload: () => Promise<void> }) {
  const [busy, setBusy] = useState(false);
  return (
    <button
      type="button"
      onClick={() => {
        setBusy(true);
        void onDownload().finally(() => setBusy(false));
      }}
      disabled={busy}
      title="AltomateHR's import file: check it in Excel, then import it from Manage Employee"
      className="inline-flex items-center gap-1.5 rounded-full border border-border/70 bg-card px-4 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-60"
    >
      {busy ? <LoaderCircle className="size-3.5 animate-spin" /> : <Download className="size-3.5" />}
      Download file
    </button>
  );
}

function FileChip({ file, onRemove }: { file: Pending; onRemove: () => void }) {
  return (
    <span
      title={file.error ?? file.note ?? file.name}
      className={`inline-flex max-w-[220px] items-center gap-1.5 rounded-xl border py-1 pl-1.5 pr-1 text-xs ${
        file.status === "error"
          ? "border-destructive/30 bg-destructive/5 text-destructive"
          : "border-border/70 bg-card text-foreground"
      }`}
    >
      {file.preview ? (
        <img src={file.preview} alt="" className="size-6 shrink-0 rounded-md object-cover" />
      ) : (
        <FileText className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
      )}
      <span className="truncate">{file.name}</span>
      {file.status === "uploading" ? (
        <LoaderCircle className="size-3.5 shrink-0 animate-spin text-muted-foreground" aria-label="Uploading" />
      ) : file.status === "error" ? (
        <CircleAlert className="size-3.5 shrink-0" aria-hidden />
      ) : null}
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Remove ${file.name}`}
        className="flex size-5 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
      >
        <X className="size-3" />
      </button>
    </span>
  );
}

function IconButton({
  label,
  onClick,
  disabled,
  children,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      disabled={disabled}
      className="flex size-9 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:opacity-50"
    >
      {children}
    </button>
  );
}
