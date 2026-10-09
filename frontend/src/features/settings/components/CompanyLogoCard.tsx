import { useEffect, useRef, useState } from "react";
import { ImageIcon, ImageUp, LoaderCircle, Trash2 } from "lucide-react";
import {
  getOrganization,
  getOrganizationLogo,
  removeOrganizationLogo,
  uploadOrganizationLogo,
} from "../api";
import { LOGO_ACCEPT, logoFileProblem } from "../lib/company-logo";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useConfirm } from "@/shared/components/ConfirmDialog";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const PRIMARY_BUTTON =
  "inline-flex h-11 items-center justify-center gap-2 rounded-2xl bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50";
const SECONDARY_BUTTON =
  "inline-flex h-11 items-center justify-center gap-2 rounded-2xl border border-border bg-card px-4 text-sm font-semibold text-foreground shadow-sm transition hover:border-destructive/40 hover:text-destructive disabled:opacity-50 disabled:hover:border-border disabled:hover:text-foreground";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

// The company logo printed on letters from Documents.
//
// Reads the same cached org as the rest of the page. The image itself sits
// behind the login token, so it is fetched as a blob and shown through an
// object URL; the org's logoUpdatedAt keys that fetch, so a replaced logo is
// re-read rather than served from anywhere stale. Read-only admins see the
// logo with the buttons disabled by the page's <fieldset>.
export function CompanyLogoCard() {
  const orgQuery = useCachedQuery("/organizations/current", getOrganization);
  const hasLogo = orgQuery.data?.hasLogo === true;
  const version = orgQuery.data?.logoUpdatedAt ?? null;

  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewFailed, setPreviewFailed] = useState(false);
  const [busy, setBusy] = useState<"upload" | "remove" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const [confirm, confirmDialog] = useConfirm();

  useEffect(() => {
    setPreviewFailed(false);
    if (!hasLogo) {
      setPreviewUrl(null);
      setPreviewLoading(false);
      return;
    }

    // `cancelled` stops a slow answer for the previous logo landing after the
    // next one; `url` is this run's own object URL, revoked when it's replaced
    // or the card unmounts.
    let cancelled = false;
    let url: string | null = null;
    setPreviewLoading(true);
    getOrganizationLogo(version)
      .then((blob) => {
        if (cancelled) return;
        url = URL.createObjectURL(blob);
        setPreviewUrl(url);
      })
      .catch(() => {
        if (cancelled) return;
        setPreviewUrl(null);
        setPreviewFailed(true);
      })
      .finally(() => {
        if (!cancelled) setPreviewLoading(false);
      });

    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [hasLogo, version]);

  async function handleFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    // Cleared so picking the same file again (after fixing it) still fires.
    e.target.value = "";
    if (!file) return;

    const problem = logoFileProblem(file);
    if (problem) {
      setError(problem);
      return;
    }

    setBusy("upload");
    setError(null);
    try {
      await uploadOrganizationLogo(file);
      // The upload invalidated /organizations*; re-read so every screen holding
      // the org (and this card's preview, via logoUpdatedAt) sees the new logo.
      await orgQuery.refresh();
    } catch (err) {
      setError(message(err, "Could not upload the logo."));
    } finally {
      setBusy(null);
    }
  }

  async function handleRemove() {
    const ok = await confirm({
      title: "Remove the company logo?",
      message:
        "New letters will print without a logo. Letters already generated keep the logo they were made with.",
      confirmLabel: "Remove logo",
      destructive: true,
    });
    if (!ok) return;

    setBusy("remove");
    setError(null);
    try {
      await removeOrganizationLogo();
      await orgQuery.refresh();
    } catch (err) {
      setError(message(err, "Could not remove the logo."));
    } finally {
      setBusy(null);
    }
  }

  const loadingOrg = orgQuery.loading && !orgQuery.data;

  return (
    <section className={CARD}>
      <div>
        <h2 className="flex items-center gap-2 text-lg font-black text-foreground">
          <ImageIcon className="h-5 w-5 text-primary" />
          Company logo
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Shown at the top of letters from Documents. PNG, JPG or WebP, up to 2 MB.
        </p>
      </div>

      <div className="mt-4 flex flex-col gap-4 sm:flex-row sm:items-center">
        <div
          className="flex h-24 w-full items-center justify-center rounded-[20px] border border-border/70 bg-card p-2 sm:w-64"
          aria-live="polite"
        >
          {loadingOrg || previewLoading ? (
            <LoaderCircle className="h-5 w-5 animate-spin text-muted-foreground" aria-label="Loading logo" />
          ) : previewUrl ? (
            <img src={previewUrl} alt="Company logo" className="h-20 w-full object-contain" />
          ) : (
            <div className="flex flex-col items-center gap-1 text-center text-xs text-muted-foreground">
              <ImageIcon className="h-5 w-5" />
              {previewFailed ? "Couldn't load the logo preview." : "No logo yet"}
            </div>
          )}
        </div>

        <div className="flex flex-wrap gap-2">
          <input
            ref={fileInput}
            type="file"
            accept={LOGO_ACCEPT}
            className="hidden"
            onChange={handleFile}
          />
          <button
            type="button"
            disabled={busy !== null || loadingOrg}
            onClick={() => fileInput.current?.click()}
            className={PRIMARY_BUTTON}
          >
            {busy === "upload" ? (
              <LoaderCircle className="h-4 w-4 animate-spin" />
            ) : (
              <ImageUp className="h-4 w-4" />
            )}
            {busy === "upload" ? "Uploading…" : hasLogo ? "Replace" : "Upload logo"}
          </button>
          {hasLogo ? (
            <button
              type="button"
              disabled={busy !== null}
              onClick={() => void handleRemove()}
              className={SECONDARY_BUTTON}
            >
              {busy === "remove" ? (
                <LoaderCircle className="h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="h-4 w-4" />
              )}
              Remove
            </button>
          ) : null}
        </div>
      </div>

      {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}
      {confirmDialog}
    </section>
  );
}
