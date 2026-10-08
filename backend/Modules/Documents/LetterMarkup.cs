namespace AltomateHR.Api.Modules.Documents;

// The template body grammar — a deliberately small markdown subset, so a
// letter can be written in a textarea and rendered by QuestPDF without a
// rich-text engine:
//
//   blank line      ends a paragraph
//   # Heading       a heading (## for a smaller one)
//   - item          a bullet; consecutive bullets form one list
//   **bold**        bold, inside any line
//   {{field}}       a merge field (see MergeFields)
//
// A single line break inside a paragraph is kept — an address block is a
// paragraph of short lines. The frontend preview (features/documents/lib/
// markup.ts) implements the same rules; keep the two in step.
//
// Merge values are substituted AFTER the markup is parsed, as literal text, so
// a value containing ** or a leading "- " never turns into formatting.
public static class LetterMarkup
{
    public enum BlockKind { Paragraph, Heading1, Heading2, Bullets }

    // One run of text, bold or not.
    public sealed record Span(string Text, bool Bold);

    // A line is a list of spans. A paragraph has many lines; a heading one; a
    // bullet list one line per item.
    public sealed record Block(BlockKind Kind, IReadOnlyList<IReadOnlyList<Span>> Lines);

    public static IReadOnlyList<Block> Parse(string? body, Func<string, string> resolve)
    {
        var blocks = new List<Block>();
        var current = new List<IReadOnlyList<Span>>();
        var currentKind = BlockKind.Paragraph;

        void Flush()
        {
            if (current.Count > 0) blocks.Add(new Block(currentKind, current));
            current = [];
            currentKind = BlockKind.Paragraph;
        }

        var lines = (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("## "))
            {
                Flush();
                blocks.Add(new Block(BlockKind.Heading2, [Inline(line[3..].Trim(), resolve)]));
                continue;
            }

            if (line.StartsWith("# "))
            {
                Flush();
                blocks.Add(new Block(BlockKind.Heading1, [Inline(line[2..].Trim(), resolve)]));
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- "))
            {
                if (currentKind != BlockKind.Bullets) Flush();
                currentKind = BlockKind.Bullets;
                current.Add(Inline(trimmed[2..].Trim(), resolve));
                continue;
            }

            if (currentKind == BlockKind.Bullets) Flush();
            current.Add(Inline(line, resolve));
        }

        Flush();
        return blocks;
    }

    // **bold** toggles, then merge fields replaced inside each run. An
    // unmatched ** leaves the rest of the line bold, as markdown editors do.
    private static IReadOnlyList<Span> Inline(string line, Func<string, string> resolve)
    {
        var spans = new List<Span>();
        var parts = line.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var text = MergeFields.Pattern.Replace(parts[i], m => resolve(m.Groups[1].Value));
            if (text.Length > 0) spans.Add(new Span(text, Bold: i % 2 == 1));
        }
        return spans;
    }
}
