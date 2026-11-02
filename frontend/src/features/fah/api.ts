import { ApiError, apiDelete, apiGet, apiGetFile, apiGetFresh, apiPostForm, apiPostStream, saveFile } from "@/shared/lib/api-client";

// FAH, the in-app assistant. A turn streams: progress while FAH checks
// things, then the answer — see streamTurn.

export type FahStatus = { available: boolean };

export type FahStep = { tool: string; ok: boolean };

/** A change FAH is holding for Confirm or Cancel: AltomateHR's own preview of it. */
export type FahPendingAction = {
  id: string;
  tool: string;
  title: string;
  preview: string;
  /** Set for an employee import: one card, one Confirm, for the whole file. */
  import: FahImportCard | null;
  /** Set for a change in several companies: what happens in each, or why it's skipped. */
  companies: FahCompanyRow[] | null;
};

export type FahCompanyRow = { company: string; skip: boolean; detail: string };

/** One row of the admin's file: its row number there, what the import would do, and why not. */
export type FahImportRow = {
  row: number;
  status: "add" | "update" | "problem" | "leftout";
  cells: string[];
  message: string | null;
};

export type FahImportCard = {
  sourceFile: string;
  adds: number;
  updates: number;
  /** Rows the import would refuse: not imported, kept in the download to fix. */
  problems: number;
  /** Repeats, or rows skipped as asked: in neither. */
  leftOut: number;
  columns: string[];
  rows: FahImportRow[];
  /** Rows beyond those listed; the download has them all. */
  moreRows: number;
};

export type FahReply = {
  conversationId: string;
  reply: string;
  pendingAction: FahPendingAction | null;
  /** On Confirm or Cancel: what happened to the change, even if FAH's reply after it failed. */
  changeOutcome: "made" | "failed" | "cancelled" | "unknown" | null;
  steps: FahStep[];
};

export type FahProgress = { type: "started" | "done"; tool: string; ok?: boolean | null };

export type FahConversationSummary = { id: string; title: string; updatedAt: string };

/** The names of files attached to an admin's message. */
export type FahMessage = { role: "user" | "assistant"; text: string; attachments: string[] };

export type FahConversation = {
  id: string;
  title: string;
  messages: FahMessage[];
  pendingAction: FahPendingAction | null;
  /** Its images or PDFs were deleted after 30 days: it can be read, not continued. */
  filesExpired: boolean;
};

/** An uploaded file waiting to go with the next message. */
export type FahAttachment = {
  id: string;
  fileName: string;
  /** "text" is a spreadsheet or Word file, read into text on the server. */
  kind: "image" | "pdf" | "text";
  /** e.g. that only the first rows were read. */
  note: string | null;
};

export const FAH_STATUS_PATH = "/fah/status";

export const getFahStatus = () => apiGet<FahStatus>(FAH_STATUS_PATH);

// Fresh, not cached: a chat changes with every message.
export const listFahConversations = () => apiGetFresh<FahConversationSummary[]>("/fah/conversations");

export const getFahConversation = (id: string) =>
  apiGetFresh<FahConversation>(`/fah/conversations/${encodeURIComponent(id)}`);

export const uploadFahAttachment = (file: File) => {
  const form = new FormData();
  form.append("file", file);
  return apiPostForm<FahAttachment>("/fah/attachments", form);
};

export const removeFahAttachment = (id: string) => apiDelete<void>(`/fah/attachments/${encodeURIComponent(id)}`);

/** The import file behind an import card still waiting, in AltomateHR's own import layout. */
export const downloadFahImportFile = (conversationId: string, actionId: string) =>
  apiGetFile(
    `/fah/conversations/${encodeURIComponent(conversationId)}/import/${encodeURIComponent(actionId)}`,
    "employees-import.xlsx",
  ).then(saveFile);

/** The send failed before the message reached the chat: it can be put back and sent again. */
export const fahMessageNotSent = (err: unknown) =>
  err instanceof ApiError && (err.body as { notSent?: boolean } | undefined)?.notSent === true;

export const sendFahMessage = (
  body: { conversationId: string | null; message: string; attachmentIds: string[] },
  onProgress: (e: FahProgress) => void,
) => streamTurn("/fah/chat", body, onProgress);

export const answerFahChange = (
  body: { conversationId: string; actionId: string; approve: boolean },
  onProgress: (e: FahProgress) => void,
) => streamTurn("/fah/confirm", body, onProgress);

// Reads the server-sent events of one turn: "progress" frames as they come,
// then a single "result" (resolved) or "error" (thrown as an ApiError).
async function streamTurn(path: string, body: unknown, onProgress: (e: FahProgress) => void): Promise<FahReply> {
  const res = await apiPostStream(path, body);
  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const frames = buffer.split("\n\n");
    buffer = frames.pop() ?? "";

    for (const frame of frames) {
      let event = "message";
      let data = "";
      for (const line of frame.split("\n")) {
        if (line.startsWith("event:")) event = line.slice(6).trim();
        else if (line.startsWith("data:")) data += line.slice(5).trim();
      }
      if (!data) continue; // a comment frame (connected / ping)

      const payload: unknown = JSON.parse(data);
      if (event === "progress") onProgress(payload as FahProgress);
      else if (event === "result") return payload as FahReply;
      else if (event === "error") {
        const e = payload as { status?: number; error?: string };
        throw new ApiError(e.error ?? "FAH couldn't answer that.", e.status ?? 500, payload);
      }
    }
  }

  // The stream ended without an answer: the connection dropped mid-turn.
  throw new ApiError("The connection to FAH dropped. Your chat is saved — open it again to see where it got to.", 0);
}
