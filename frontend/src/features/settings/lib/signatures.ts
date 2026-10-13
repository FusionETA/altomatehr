// What a saved signature accepts. Mirrors the server's own checks (which look
// at the file's real bytes, not its name) so the common mistakes — a PDF, an
// SVG, a phone photo of several MB — are caught before the upload. The server
// still decides; this only saves a round trip.

import type { Signature } from "../api";

export const MAX_SIGNATURES = 10;

export const SIGNATURE_TYPES = ["image/png", "image/jpeg", "image/webp"] as const;
export const SIGNATURE_MAX_BYTES = 1024 * 1024;

/** For the file picker's `accept`, so the dialog offers the right files first. */
export const SIGNATURE_ACCEPT = [...SIGNATURE_TYPES, ".png", ".jpg", ".jpeg", ".webp"].join(",");

/** Text limits for the name and position under the signature. */
export const SIGNATURE_NAME_MAX = 120;
export const SIGNATURE_POSITION_MAX = 120;

/** A friendly reason the file can't be used, or null when it looks fine. */
export function signatureFileProblem(file: File): string | null {
  if (!(SIGNATURE_TYPES as readonly string[]).includes(file.type)) {
    return "That file isn't a PNG, JPG or WebP image. Save the signature in one of those formats and try again.";
  }
  if (file.size > SIGNATURE_MAX_BYTES) {
    const mb = (file.size / (1024 * 1024)).toFixed(1);
    return `That image is ${mb} MB — the limit is 1 MB. Crop it to just the signature, or export a smaller version.`;
  }
  return null;
}

/** "Tan Mei Ling — HR Manager", for selects and confirm messages. */
export function signatureLabel(signature: Pick<Signature, "name" | "position">): string {
  return signature.position ? `${signature.name} — ${signature.position}` : signature.name;
}
