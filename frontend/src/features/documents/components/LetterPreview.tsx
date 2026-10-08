import { Fragment } from "react";
import type { MergeField } from "../api";
import { isInputField, isValidInputField, parseMarkup, type Line } from "../lib/markup";

// The template body as the letter will read, in the browser — quick feedback
// while typing. The PDF (Preview PDF) is the real layout; this shows the
// structure and where each field lands. Fields render as chips: known ones by
// label, typed-per-letter ones dashed, unknown ones in red.
export function LetterPreview({ body, fields }: { body: string; fields: MergeField[] }) {
  const byKey = new Map(fields.map((f) => [f.key, f]));
  const blocks = parseMarkup(body);

  if (blocks.length === 0) {
    return <p className="text-sm text-muted-foreground">Nothing to preview yet.</p>;
  }

  const renderLine = (line: Line) =>
    line.map((span, i) => {
      if (span.field) {
        const known = byKey.get(span.field);
        const input = isInputField(span.field) && isValidInputField(span.field);
        const label = known?.label ?? (input ? span.field.slice("input.".length) : span.field);
        return (
          <span
            key={i}
            title={`{{${span.field}}}`}
            className={`mx-0.5 inline-flex items-center rounded-md px-1.5 py-px align-baseline text-[0.85em] font-semibold ${
              known
                ? "bg-primary/10 text-primary"
                : input
                  ? "border border-dashed border-warning-foreground/50 bg-warning/30 text-warning-foreground"
                  : "bg-destructive/10 text-destructive line-through"
            }`}
          >
            {label}
          </span>
        );
      }
      return span.bold ? (
        <strong key={i} className="font-bold">
          {span.text}
        </strong>
      ) : (
        <Fragment key={i}>{span.text}</Fragment>
      );
    });

  return (
    <div className="space-y-3 text-sm leading-relaxed text-foreground">
      {blocks.map((block, i) => {
        switch (block.kind) {
          case "h1":
            return (
              <h3 key={i} className="pt-1 text-base font-black">
                {renderLine(block.line)}
              </h3>
            );
          case "h2":
            return (
              <h4 key={i} className="text-sm font-bold">
                {renderLine(block.line)}
              </h4>
            );
          case "bullets":
            return (
              <ul key={i} className="list-disc space-y-1 pl-5">
                {block.items.map((item, j) => (
                  <li key={j}>{renderLine(item)}</li>
                ))}
              </ul>
            );
          default:
            return (
              <p key={i}>
                {block.lines.map((line, j) => (
                  <Fragment key={j}>
                    {j > 0 ? <br /> : null}
                    {renderLine(line)}
                  </Fragment>
                ))}
              </p>
            );
        }
      })}
    </div>
  );
}
