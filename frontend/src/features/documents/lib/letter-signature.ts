import type { ResolvedField } from "../api";

// How a chosen signature changes what the Generate dialog shows.
//
// A saved signature carries a name and position, and the server prints those
// as the signatory unless the letter types its own. So when one is chosen the
// dialog shows its name / position under "From the records" (no longer a gap,
// even if Payroll has no declarant); with "No signature" it shows what resolve
// read from Payroll's declarant, exactly as before signatures existed.

export const SIGNATORY_NAME = "signatory.name";
export const SIGNATORY_POSITION = "signatory.position";
/** The fields a chosen signature fills. */
export const SIGNATORY_KEYS = [SIGNATORY_NAME, SIGNATORY_POSITION] as const;

/** The template editor's "Company default" option — a Select item can't be null. */
export const COMPANY_DEFAULT_OPTION = "__company-default";

export type LetterSignature = { id: string; name: string; position: string };

export function applySignature(
  fields: ResolvedField[],
  signature: LetterSignature | null,
): ResolvedField[] {
  if (!signature) return fields;
  return fields.map((f) => {
    const value =
      f.key === SIGNATORY_NAME ? signature.name : f.key === SIGNATORY_POSITION ? signature.position : null;
    if (value === null) return f;
    return { ...f, value, editValue: value, missing: false, fixHint: null };
  });
}
