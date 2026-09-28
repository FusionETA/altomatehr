import { useState } from "react";
import { FileImage, FileText, LoaderCircle, X } from "lucide-react";

import { openOvertimePhoto, type OvertimeAttachment } from "../api";

const isPdf = (name: string) => name.toLowerCase().endsWith(".pdf");

// The before- or after-work files on an overtime request, as a row of chips —
// click one to open it (a photo in a new tab, a PDF in the browser's viewer).
//
// Shared by the employee's card, the approver's details and the admin table,
// so the three can't disagree about how a file is shown or opened. `onRemove`
// is only passed where a file may be taken off (the owner, while pending).
export function OvertimeFileList({
  files,
  onRemove,
  removingId,
}: {
  files: OvertimeAttachment[];
  onRemove?: (file: OvertimeAttachment) => void;
  removingId?: string | null;
}) {
  // A file the storage no longer has 404s on open; say so on that chip
  // rather than letting the rejection vanish.
  const [failed, setFailed] = useState<Set<string>>(new Set());

  return (
    <div className="flex flex-wrap gap-2">
      {files.map((file) => {
        const Icon = isPdf(file.fileName) ? FileText : FileImage;
        const broken = failed.has(file.id);
        return (
          <span
            key={file.id}
            className={`inline-flex max-w-full items-center rounded-full text-xs font-semibold ${
              broken ? "bg-destructive/10 text-destructive" : "bg-muted text-primary"
            }`}
          >
            <button
              type="button"
              title={broken ? `${file.fileName} — the file is no longer in storage` : `Open ${file.fileName}`}
              onClick={() => {
                setFailed((cur) => {
                  const next = new Set(cur);
                  next.delete(file.id);
                  return next;
                });
                openOvertimePhoto(file.url).catch(() =>
                  setFailed((cur) => new Set(cur).add(file.id)),
                );
              }}
              className={`inline-flex min-w-0 items-center gap-1.5 py-1.5 transition hover:underline ${
                onRemove ? "pl-3 pr-1" : "px-3"
              }`}
            >
              <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden />
              <span className="max-w-[12rem] truncate">{file.fileName}</span>
            </button>
            {onRemove ? (
              <button
                type="button"
                aria-label={`Remove ${file.fileName}`}
                disabled={removingId === file.id}
                onClick={() => onRemove(file)}
                className="mr-1 flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-background hover:text-destructive disabled:opacity-50"
              >
                {removingId === file.id ? (
                  <LoaderCircle className="h-3 w-3 animate-spin" aria-hidden />
                ) : (
                  <X className="h-3 w-3" aria-hidden />
                )}
              </button>
            ) : null}
          </span>
        );
      })}
    </div>
  );
}
