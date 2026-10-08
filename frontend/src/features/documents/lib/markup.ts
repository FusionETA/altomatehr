// The template body grammar, for the editor's live preview. Mirrors the
// backend's LetterMarkup (Modules/Documents/LetterMarkup.cs) — keep the two in
// step:
//
//   blank line   ends a paragraph
//   # / ##       a heading
//   - item       a bullet; consecutive bullets form one list
//   **bold**     bold, inside any line
//   {{field}}    a merge field
//
// Values are substituted after parsing, as literal text.

export type Span = { text: string; bold: boolean; field?: string };
export type Line = Span[];
export type Block =
  | { kind: "paragraph"; lines: Line[] }
  | { kind: "h1"; line: Line }
  | { kind: "h2"; line: Line }
  | { kind: "bullets"; items: Line[] };

export const FIELD_PATTERN = /\{\{\s*([A-Za-z0-9_.]+)\s*\}\}/g;

/** Every distinct field the text mentions, in first-use order. */
export function fieldsIn(text: string): string[] {
  const seen = new Set<string>();
  for (const match of text.matchAll(FIELD_PATTERN)) seen.add(match[1]);
  return [...seen];
}

const INPUT_PATTERN = /^input\.[A-Za-z][A-Za-z0-9_]{0,59}$/;

export const isInputField = (key: string) => key.startsWith("input.");
export const isValidInputField = (key: string) => INPUT_PATTERN.test(key);

// Splits one line into bold runs, and each run into text and field spans, so
// the preview can render a field as a highlighted chip.
function inline(line: string): Line {
  const spans: Line = [];
  line.split("**").forEach((part, i) => {
    if (!part) return;
    const bold = i % 2 === 1;
    let last = 0;
    for (const match of part.matchAll(FIELD_PATTERN)) {
      const at = match.index ?? 0;
      if (at > last) spans.push({ text: part.slice(last, at), bold });
      spans.push({ text: match[0], bold, field: match[1] });
      last = at + match[0].length;
    }
    if (last < part.length) spans.push({ text: part.slice(last), bold });
  });
  return spans;
}

export function parseMarkup(body: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: Line[] = [];
  let bullets: Line[] = [];

  const flush = () => {
    if (paragraph.length) blocks.push({ kind: "paragraph", lines: paragraph });
    if (bullets.length) blocks.push({ kind: "bullets", items: bullets });
    paragraph = [];
    bullets = [];
  };

  for (const raw of body.replace(/\r\n?/g, "\n").split("\n")) {
    const line = raw.trimEnd();
    if (line.trim() === "") {
      flush();
      continue;
    }
    if (line.startsWith("## ")) {
      flush();
      blocks.push({ kind: "h2", line: inline(line.slice(3).trim()) });
      continue;
    }
    if (line.startsWith("# ")) {
      flush();
      blocks.push({ kind: "h1", line: inline(line.slice(2).trim()) });
      continue;
    }
    const trimmed = line.trimStart();
    if (trimmed.startsWith("- ")) {
      if (paragraph.length) flush();
      bullets.push(inline(trimmed.slice(2).trim()));
      continue;
    }
    if (bullets.length) flush();
    paragraph.push(inline(line));
  }

  flush();
  return blocks;
}
