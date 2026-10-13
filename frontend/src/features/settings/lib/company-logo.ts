// What the company-logo upload accepts. Mirrors the server's own check (which
// looks at the file's real bytes, not its name) so the common mistakes — an
// SVG, a PDF, a 6 MB photo — are caught before the upload instead of after.
// The server still decides; this only saves a round trip.

export const LOGO_TYPES = ["image/png", "image/jpeg", "image/webp"] as const;
export const LOGO_MAX_BYTES = 2 * 1024 * 1024;

/** For the file picker's `accept`, so the dialog offers the right files first. */
export const LOGO_ACCEPT = [...LOGO_TYPES, ".png", ".jpg", ".jpeg", ".webp"].join(",");

/** A friendly reason the file can't be used, or null when it looks fine. */
export function logoFileProblem(file: File): string | null {
  if (!(LOGO_TYPES as readonly string[]).includes(file.type)) {
    return "That file isn't a PNG, JPG or WebP image. Save the logo in one of those formats and try again.";
  }
  if (file.size > LOGO_MAX_BYTES) {
    const mb = (file.size / (1024 * 1024)).toFixed(1);
    return `That image is ${mb} MB — the limit is 2 MB. Export a smaller version and try again.`;
  }
  return null;
}
