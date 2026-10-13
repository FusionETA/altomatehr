import { useEffect, useMemo, useState } from "react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { getSignatures, SIGNATURES_PATH } from "@/features/settings/api";
import { NO_SIGNATURE, type ResolvedLetter } from "../api";
import type { LetterSignature } from "./letter-signature";

// The Generate dialog's "Signature" choice.
//
// Until the admin picks one it follows resolve's answer — the template's
// default, else the company default, else none — and a different template
// starts over from that template's answer. `signatureId` is always sent
// explicitly (the id, or "none"), so the letter prints exactly what the
// dialog showed.
export function useLetterSignature(templateId: string, resolved: ResolvedLetter | null) {
  const query = useCachedQuery(SIGNATURES_PATH, getSignatures);
  const [picked, setPicked] = useState<string | null>(null);

  useEffect(() => {
    setPicked(null);
  }, [templateId]);

  // The resolved default is listed even if the list hasn't loaded (or failed),
  // so the pre-selected option always has a label.
  const options = useMemo<LetterSignature[]>(() => {
    const list: LetterSignature[] = (query.data ?? []).map((s) => ({
      id: s.id,
      name: s.name,
      position: s.position,
    }));
    const fromResolve = resolved?.signature ?? null;
    if (fromResolve && !list.some((s) => s.id === fromResolve.id)) list.unshift(fromResolve);
    return list;
  }, [query.data, resolved?.signature]);

  const selectValue = picked ?? resolved?.signature?.id ?? NO_SIGNATURE;
  const selected = options.find((s) => s.id === selectValue) ?? null;

  return {
    options,
    loading: query.loading && !query.data,
    selectValue,
    onSelect: setPicked,
    /** null when "No signature (sign by hand)" is chosen. */
    selected,
    signatureId: selected ? selected.id : NO_SIGNATURE,
  };
}
