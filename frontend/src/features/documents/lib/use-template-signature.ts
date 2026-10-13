import { useState } from "react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { getSignatures, SIGNATURES_PATH } from "@/features/settings/api";
import { NO_SIGNATURE, type DocumentTemplate } from "../api";
import { COMPANY_DEFAULT_OPTION } from "./letter-signature";

// The template editor's "Default signature" choice.
//
// `chosenId` is what gets saved: a signature id, or null for "use the company
// default". A template whose saved signature has since been removed already
// prints the company default, so it shows (and saves) as that — and doesn't
// count as an unsaved change.
export function useTemplateSignature(template: DocumentTemplate | null) {
  const query = useCachedQuery(SIGNATURES_PATH, getSignatures);
  const signatures = query.data ?? [];
  const [choice, setChoice] = useState<string | null>(template?.defaultSignatureId ?? null);

  // Until the list loads, trust the id; after, an id not in it is a removed one.
  const isActive = (id: string | null) => id !== null && (!query.data || signatures.some((s) => s.id === id));
  const chosenId = isActive(choice) ? choice : null;
  const savedId = template?.defaultSignatureId ?? null;
  const savedGone = savedId !== null && !isActive(savedId);
  const dirty = chosenId !== (savedGone ? null : savedId);

  // What Preview PDF should print: the choice on screen even before it's
  // saved. "Company default" is sent as the company default's own id (or as
  // none), because null would mean the SAVED template default.
  const companyDefault = signatures.find((s) => s.isDefault) ?? null;
  const previewId = chosenId ?? companyDefault?.id ?? (query.data ? NO_SIGNATURE : null);

  return {
    signatures,
    loading: query.loading && !query.data,
    selectValue: chosenId ?? COMPANY_DEFAULT_OPTION,
    onSelect: (value: string) => setChoice(value === COMPANY_DEFAULT_OPTION ? null : value),
    chosenId,
    savedGone,
    dirty,
    previewId,
    companyDefault,
  };
}
