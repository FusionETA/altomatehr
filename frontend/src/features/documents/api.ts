import {
  apiDelete,
  apiGet,
  apiGetFile,
  apiPost,
  apiPostFile,
  apiPostForm,
  apiPut,
  type ApiFile,
} from "@/shared/lib/api-client";
import { invalidateFor } from "@/shared/lib/api-cache";

// HR letters: the company's template library and letters generated from it.
// Admin-only — every endpoint is behind Admin/Owner and the "documents" module.

export type DocumentCategory =
  | "OFFER"
  | "CONFIRMATION"
  | "PROMOTION"
  | "RESIGNATION"
  | "TERMINATION"
  | "WARNING"
  | "OTHER";

export type DocumentTemplate = {
  id: string;
  name: string;
  category: DocumentCategory;
  body: string;
  /** Came from "Add sample templates" — placeholder wording. */
  isSample: boolean;
  /** Merge fields the body uses (registry and input.* alike). */
  fields: string[];
  /** The saved signature this template's letters use; null = the company default. */
  defaultSignatureId: string | null;
  createdAt: string;
  updatedAt: string;
};

export type SaveDocumentTemplate = {
  name: string;
  category: DocumentCategory;
  body: string;
  defaultSignatureId: string | null;
};

/**
 * Which signature a letter prints. null = the default chain (template default →
 * company default → none); NO_SIGNATURE = none, signed by hand; else a
 * signature id.
 */
export type LetterSignatureChoice = string | null;
export const NO_SIGNATURE = "none";

export type MergeFieldSource = "Employee" | "Company" | "Signatory" | "System" | "Input";
export type MergeFieldKind = "Text" | "Multiline" | "Date" | "Number" | "Money";

export type MergeField = {
  key: string;
  label: string;
  source: MergeFieldSource;
  kind: MergeFieldKind;
  description: string;
  writableToEmployee: boolean;
};

export type ResolvedField = {
  key: string;
  label: string;
  source: MergeFieldSource;
  kind: MergeFieldKind;
  /** As it will print. Null when missing. */
  value: string | null;
  /** The same value as an input takes it (yyyy-MM-dd, 4500.00). */
  editValue: string | null;
  /** Nothing on file — must be filled in or explicitly left blank. */
  missing: boolean;
  /** A gap that can be saved back to the employee's record. */
  writableToEmployee: boolean;
  /** Where to fix it at the source, e.g. "Set it in Payroll → Company Info." */
  fixHint: string | null;
};

export type ResolvedLetter = {
  templateId: string;
  templateName: string;
  employeeUserId: string;
  employeeName: string;
  fields: ResolvedField[];
  /** The signature the default chain picks for this letter; null = none (sign by hand). */
  signature: { id: string; name: string; position: string } | null;
};

export type GenerateLetter = {
  employeeUserId: string;
  /** Per-letter values: fills for gaps, input.* answers, overrides. */
  values: Record<string, string>;
  /** Fields the admin chose to leave blank. */
  leaveBlank: string[];
  /** Gaps whose typed value should also be saved to the employee record. */
  saveToEmployee: string[];
  /** Keep a copy on the employee's file (admin-only). */
  save: boolean;
  /** The signature to print. Its name / position fill the signatory unless `values` types them. */
  signatureId: LetterSignatureChoice;
};

export type GeneratedDocument = {
  id: string;
  templateId: string | null;
  templateName: string;
  category: DocumentCategory;
  employeeUserId: string;
  employeeName: string | null;
  generatedByName: string | null;
  fileName: string;
  sizeBytes: number;
  /** The saved signature printed on it (name as it was then); null = none. */
  signatureName: string | null;
  createdAt: string;
};

export type AddSamplesResult = {
  added: number;
  skipped: number;
  templates: DocumentTemplate[];
};

/** A Word / text file converted to template markup — opened in the editor unsaved. */
export type TemplateImport = {
  /** The first heading, else the file name. */
  suggestedName: string;
  body: string;
  /** {{…}} placeholders that aren't merge fields, as written ("Employee Name"). */
  unknownFields: string[];
  /** What didn't come across ("2 images were skipped …"). */
  warnings: string[];
};

/** One turn of a "Write with AI" conversation. */
export type TemplateAiMessage = { role: "user" | "assistant"; text: string };

/** A draft the AI wrote — opened in the editor unsaved, like an import. */
export type TemplateAiDraft = {
  name: string;
  category: DocumentCategory;
  body: string;
  /** {{…}} it made up that aren't merge fields — for the "Replace with…" picker. */
  unknownFields: string[];
  warnings: string[];
};

export type TemplateAiResult = {
  /** What the AI says back, in the admin's language. */
  reply: string;
  /** Two to choose from for a new template; one when revising a draft. */
  drafts: TemplateAiDraft[];
};

/** Upload limit, as the server enforces it. */
export const TEMPLATE_IMPORT_MAX_BYTES = 5 * 1024 * 1024;
export const TEMPLATE_IMPORT_ACCEPT = ".docx,.txt,.md";

export const TEMPLATES_PATH = "/documents/templates";
export const MERGE_FIELDS_PATH = "/documents/merge-fields";
export const generatedPath = (employeeUserId?: string | null) =>
  employeeUserId
    ? `/documents/generated?employeeUserId=${encodeURIComponent(employeeUserId)}`
    : "/documents/generated";

export const getTemplates = () => apiGet<DocumentTemplate[]>(TEMPLATES_PATH);
export const getMergeFields = () => apiGet<MergeField[]>(MERGE_FIELDS_PATH);

export const createTemplate = (body: SaveDocumentTemplate) =>
  apiPost<DocumentTemplate>(TEMPLATES_PATH, body);
export const updateTemplate = (id: string, body: SaveDocumentTemplate) =>
  apiPut<DocumentTemplate>(`${TEMPLATES_PATH}/${id}`, body);
export const deleteTemplate = (id: string) => apiDelete<void>(`${TEMPLATES_PATH}/${id}`);
export const addSampleTemplates = () => apiPost<AddSamplesResult>(`${TEMPLATES_PATH}/samples`);

/** Converts a .docx / .txt / .md to markup. Saves nothing — the editor saves via createTemplate. */
export const importTemplateFile = (file: File) => {
  const form = new FormData();
  form.append("file", file);
  return apiPostForm<TemplateImport>(`${TEMPLATES_PATH}/import`, form);
};

// The server takes at most 20 turns. A long session sends its most recent
// ones, starting on one of the admin's — the current draft travels separately,
// so older turns add little.
const MAX_AI_TURNS = 19;

/**
 * "Write with AI". Without `currentBody`: two drafts of a new template. With it
 * (the editor's Ask AI panel): one revision of that draft. Saves nothing.
 */
export const draftTemplateWithAi = (body: {
  messages: TemplateAiMessage[];
  currentBody?: string;
  currentName?: string;
  currentCategory?: DocumentCategory;
}) => {
  let messages = body.messages.slice(-MAX_AI_TURNS);
  while (messages.length > 1 && messages[0].role !== "user") messages = messages.slice(1);
  return apiPost<TemplateAiResult>(`${TEMPLATES_PATH}/ai`, { ...body, messages });
};

/** A PDF preview. With an id, `body` previews unsaved edits of that template. */
export const previewTemplate = (
  id: string | null,
  body: {
    name?: string;
    body?: string;
    employeeUserId?: string | null;
    signatureId?: LetterSignatureChoice;
  },
): Promise<ApiFile> =>
  apiPostFile(
    id ? `${TEMPLATES_PATH}/${id}/preview` : `${TEMPLATES_PATH}/preview`,
    "letter-preview.pdf",
    body,
  );

export const resolveLetter = (templateId: string, employeeUserId: string) =>
  apiPost<ResolvedLetter>(`${TEMPLATES_PATH}/${templateId}/resolve`, { employeeUserId });

// A file download, so the client's write-invalidation doesn't run on its own:
// a kept letter changes the letters list, and a gap saved back changes the
// employee's record.
export const generateLetter = async (templateId: string, body: GenerateLetter) => {
  const file = await apiPostFile(`${TEMPLATES_PATH}/${templateId}/generate`, "letter.pdf", body);
  invalidateFor(generatedPath());
  if (body.saveToEmployee.length > 0) invalidateFor("/employees");
  return file;
};

export const getGeneratedDocuments = (employeeUserId?: string | null) =>
  apiGet<GeneratedDocument[]>(generatedPath(employeeUserId));

export const downloadGeneratedDocument = (doc: GeneratedDocument) =>
  apiGetFile(`/documents/generated/${doc.id}/file`, doc.fileName);

export const deleteGeneratedDocument = (id: string) =>
  apiDelete<void>(`/documents/generated/${id}`);
