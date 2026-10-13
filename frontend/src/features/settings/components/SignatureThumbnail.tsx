import { ImageOff, LoaderCircle } from "lucide-react";
import { useSignatureImage } from "../lib/use-signature-image";
import { cn } from "@/shared/lib/utils";

// A saved signature's image in a white box — the paper it prints on — so a
// dark signature on a transparent background reads the same in dark mode.
// Used by the Signatures card and the Documents dialogs.
export function SignatureThumbnail({
  id,
  name,
  className,
}: {
  id: string | null;
  name: string;
  /** Sizes the box; the image is contain-fit inside it. */
  className?: string;
}) {
  const { url, loading, failed } = useSignatureImage(id);

  return (
    <div
      className={cn(
        "flex shrink-0 items-center justify-center overflow-hidden rounded-xl border border-border/70 bg-white p-1",
        className ?? "h-12 w-28",
      )}
    >
      {loading ? (
        <LoaderCircle className="h-4 w-4 animate-spin text-black/40" aria-label="Loading signature" />
      ) : url ? (
        <img src={url} alt={`Signature of ${name}`} className="h-full w-full object-contain" />
      ) : failed ? (
        <ImageOff className="h-4 w-4 text-black/40" aria-label="Couldn't load the signature" />
      ) : null}
    </div>
  );
}
