import { Fragment } from "react";

// FAH's answers use a little Markdown: paragraphs, "- " lists, **bold** and
// "| a | b |" tables. Rendered as React elements, never as HTML, so nothing in
// an answer — which can quote the company's own records — can inject markup.
export function FahText({ text }: { text: string }) {
  const blocks = text.split(/\n{2,}/).flatMap(splitTables);
  return (
    <div className="space-y-2">
      {blocks.map((block, i) => {
        if (block.kind === "table") return <Table key={i} rows={block.rows} />;

        const lines = block.text.split("\n").filter((l) => l.trim().length > 0);
        const isList = lines.length > 0 && lines.every((l) => /^\s*([-*•]|\d+\.)\s+/.test(l));
        if (isList) {
          const ordered = /^\s*\d+\./.test(lines[0]);
          const items = lines.map((l) => l.replace(/^\s*([-*•]|\d+\.)\s+/, ""));
          const List = ordered ? "ol" : "ul";
          return (
            <List key={i} className={`space-y-1 pl-5 ${ordered ? "list-decimal" : "list-disc"}`}>
              {items.map((item, j) => (
                <li key={j}>
                  <Inline text={item} />
                </li>
              ))}
            </List>
          );
        }
        return (
          <p key={i} className="whitespace-pre-wrap">
            <Inline text={block.text} />
          </p>
        );
      })}
    </div>
  );
}

type Block = { kind: "text"; text: string } | { kind: "table"; rows: string[][] };

const isTableLine = (line: string) => /^\s*\|.*\|\s*$/.test(line);
const isDivider = (line: string) => /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(line);

// A block's table (a "|" header row, a "|---|" divider, then "|" rows) apart
// from any text around it in the same block.
function splitTables(block: string): Block[] {
  const lines = block.split("\n");
  const out: Block[] = [];
  let text: string[] = [];
  let i = 0;
  while (i < lines.length) {
    if (isTableLine(lines[i]) && i + 1 < lines.length && isDivider(lines[i + 1])) {
      if (text.length) out.push({ kind: "text", text: text.join("\n") });
      text = [];
      const rows = [cells(lines[i])];
      i += 2;
      while (i < lines.length && isTableLine(lines[i])) rows.push(cells(lines[i++]));
      out.push({ kind: "table", rows });
      continue;
    }
    text.push(lines[i++]);
  }
  if (text.some((l) => l.trim().length > 0)) out.push({ kind: "text", text: text.join("\n") });
  return out;
}

const cells = (line: string) =>
  line
    .trim()
    .replace(/^\|/, "")
    .replace(/\|$/, "")
    .split("|")
    .map((c) => c.trim());

function Table({ rows }: { rows: string[][] }) {
  const [head, ...body] = rows;
  return (
    <div className="overflow-x-auto rounded-xl border border-border/70">
      <table className="w-full text-left text-xs">
        <thead className="bg-muted/60 text-muted-foreground">
          <tr>
            {head.map((c, i) => (
              <th key={i} className="whitespace-nowrap px-2.5 py-1.5 font-semibold">
                <Inline text={c} />
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {body.map((row, r) => (
            <tr key={r} className="border-t border-border/50">
              {head.map((_, c) => (
                <td key={c} className="px-2.5 py-1.5 text-foreground">
                  <Inline text={row[c] ?? ""} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Inline({ text }: { text: string }) {
  const parts = text.split(/(\*\*[^*]+\*\*)/g);
  return (
    <>
      {parts.map((part, i) =>
        part.startsWith("**") && part.endsWith("**") && part.length > 4 ? (
          <strong key={i} className="font-semibold text-foreground">
            {part.slice(2, -2)}
          </strong>
        ) : (
          <Fragment key={i}>{part}</Fragment>
        ),
      )}
    </>
  );
}
