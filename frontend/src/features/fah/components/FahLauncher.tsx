import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, CircleAlert, History, LoaderCircle, Plus, SendHorizontal, Sparkles, X } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  FAH_STATUS_PATH,
  answerFahChange,
  getFahConversation,
  getFahStatus,
  listFahConversations,
  sendFahMessage,
  type FahConversationSummary,
  type FahPendingAction,
  type FahProgress,
  type FahReply,
} from "../api";
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

type Entry =
  | { kind: "user"; text: string }
  | { kind: "assistant"; text: string }
  | { kind: "change"; action: FahPendingAction; state: ChangeState };

type Step = { tool: string; done: boolean; ok: boolean };

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
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

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

  async function run(work: () => Promise<FahReply>, onFail?: () => void, onDone?: (reply: FahReply) => void) {
    setRunning(true);
    setSteps([]);
    setError(null);
    try {
      const reply = await work();
      onDone?.(reply);
      apply(reply);
    } catch (err) {
      onFail?.();
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

  function send(text: string) {
    const message = text.trim();
    if (!message || running) return;
    setInput("");
    setEntries((current) => [...current, { kind: "user", text: message }]);
    void run(
      () => sendFahMessage({ conversationId, message }, onProgress),
      undefined,
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
      setEntries([
        ...chat.messages.map((m) => ({ kind: m.role, text: m.text }) as Entry),
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
      className={`${open ? "flex" : "hidden"} fixed inset-0 z-50 flex-col bg-background lg:inset-auto lg:bottom-8 lg:right-8 lg:h-[min(640px,calc(100vh-4rem))] lg:w-[400px] lg:overflow-hidden lg:rounded-[28px] lg:border lg:border-border/70 lg:shadow-2xl`}
    >
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
                and prepare payroll changes — you confirm every change before it&apos;s made.
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
              <div key={i} className="flex justify-end">
                <p className="max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-br-md bg-primary px-3.5 py-2 text-sm text-primary-foreground">
                  {entry.text}
                </p>
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
        className="flex items-end gap-2 border-t border-border/60 p-3"
      >
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
          rows={1}
          maxLength={4000}
          placeholder="Ask FAH…"
          aria-label="Message FAH"
          className="max-h-32 min-h-10 flex-1 resize-none rounded-2xl border border-border bg-card px-3.5 py-2.5 text-base text-foreground lg:text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        />
        <button
          type="submit"
          aria-label="Send"
          disabled={running || input.trim().length === 0}
          className="flex size-10 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
        >
          {running ? <LoaderCircle className="size-4 animate-spin" /> : <SendHorizontal className="size-4" />}
        </button>
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
}: {
  action: FahPendingAction;
  state: ChangeState;
  disabled: boolean;
  onAnswer: (approve: boolean) => void;
}) {
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
      {state === "waiting" || state === "confirming" || state === "cancelling" ? (
        <div className="mt-3 flex gap-2">
          <button
            type="button"
            onClick={() => onAnswer(true)}
            disabled={disabled}
            className="inline-flex items-center gap-1.5 rounded-full bg-primary px-4 py-1.5 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-60"
          >
            {state === "confirming" ? <LoaderCircle className="size-3.5 animate-spin" /> : <Check className="size-3.5" />}
            Confirm
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
        </div>
      ) : (
        <p className="mt-2 text-xs font-semibold text-muted-foreground">{outcome[state]}</p>
      )}
    </div>
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
