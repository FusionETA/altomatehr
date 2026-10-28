import { ApiError, apiGet, apiGetFresh, apiPostStream } from "@/shared/lib/api-client";

// FAH, the in-app assistant. A turn streams: progress while FAH checks
// things, then the answer — see streamTurn.

export type FahStatus = { available: boolean };

export type FahStep = { tool: string; ok: boolean };

/** A change FAH is holding for Confirm or Cancel: AltomateHR's own preview of it. */
export type FahPendingAction = { id: string; tool: string; title: string; preview: string };

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

export type FahMessage = { role: "user" | "assistant"; text: string };

export type FahConversation = {
  id: string;
  title: string;
  messages: FahMessage[];
  pendingAction: FahPendingAction | null;
};

export const FAH_STATUS_PATH = "/fah/status";

export const getFahStatus = () => apiGet<FahStatus>(FAH_STATUS_PATH);

// Fresh, not cached: a chat changes with every message.
export const listFahConversations = () => apiGetFresh<FahConversationSummary[]>("/fah/conversations");

export const getFahConversation = (id: string) =>
  apiGetFresh<FahConversation>(`/fah/conversations/${encodeURIComponent(id)}`);

export const sendFahMessage = (
  body: { conversationId: string | null; message: string },
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
