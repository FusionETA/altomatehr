import { useEffect, useState } from "react";
import { getSignatureImage } from "../api";

// A saved signature's image as an object URL, for an <img src>.
//
// The image sits behind the login token, so it can't be a plain URL: it is
// fetched as a blob and wrapped in an object URL, which is revoked when the id
// changes or the component unmounts. `cancelled` stops a slow answer for the
// previous id landing after the next one. A null id shows nothing.
export function useSignatureImage(id: string | null) {
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
    setUrl(null);
    if (!id) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    setLoading(true);
    getSignatureImage(id)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [id]);

  return { url, loading, failed };
}
