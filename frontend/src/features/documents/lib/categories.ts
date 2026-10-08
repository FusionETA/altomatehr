import type { DocumentCategory } from "../api";

export const DOCUMENT_CATEGORIES: { value: DocumentCategory; label: string }[] = [
  { value: "OFFER", label: "Offer" },
  { value: "CONFIRMATION", label: "Confirmation" },
  { value: "PROMOTION", label: "Promotion" },
  { value: "RESIGNATION", label: "Resignation" },
  { value: "TERMINATION", label: "Termination" },
  { value: "WARNING", label: "Warning" },
  { value: "OTHER", label: "Other" },
];

export const categoryLabel = (category: DocumentCategory) =>
  DOCUMENT_CATEGORIES.find((c) => c.value === category)?.label ?? category;

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}
