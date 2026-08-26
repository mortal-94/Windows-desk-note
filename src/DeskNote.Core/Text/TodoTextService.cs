namespace DeskNote.Core.Text;

public readonly record struct TextEdit(int Start, int Length, string Replacement);

/// <summary>
/// Defines the on-disk TODO convention used by the plain-text editor. Non-empty lines
/// start with either "☐ " or "☑ "; empty lines remain truly empty and have no marker.
/// </summary>
public static class TodoTextService
{
    public const char UncheckedMarker = '☐';
    public const char CheckedMarker = '☑';
    public const string UncheckedPrefix = "☐ ";
    public const string CheckedPrefix = "☑ ";

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<TextEdit> edits = GetNormalizationEdits(text);
        if (edits.Count == 0)
        {
            return text;
        }

        System.Text.StringBuilder result = new(text);
        for (int index = edits.Count - 1; index >= 0; index--)
        {
            TextEdit edit = edits[index];
            result.Remove(edit.Start, edit.Length);
            result.Insert(edit.Start, edit.Replacement);
        }

        return result.ToString();
    }

    public static IReadOnlyList<TextEdit> GetNormalizationEdits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<TextEdit> edits = [];
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int lineEnd = text.IndexOfAny(['\r', '\n'], lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            AddLineEdit(text, lineStart, lineEnd - lineStart, edits);
            if (lineEnd == text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
            if (text[lineEnd] == '\r' && lineStart < text.Length && text[lineStart] == '\n')
            {
                lineStart++;
            }
        }

        return edits;
    }

    public static int MapIndex(int originalIndex, IReadOnlyList<TextEdit> edits)
    {
        int accumulatedDelta = 0;
        foreach (TextEdit edit in edits)
        {
            int originalEnd = edit.Start + edit.Length;
            if (originalIndex < edit.Start)
            {
                break;
            }

            if (originalIndex >= originalEnd)
            {
                accumulatedDelta += edit.Replacement.Length - edit.Length;
            }
            else
            {
                return Math.Max(0, edit.Start + accumulatedDelta + edit.Replacement.Length);
            }
        }

        return Math.Max(0, originalIndex + accumulatedDelta);
    }

    public static int FindLineStart(string text, int characterIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        int index = Math.Clamp(characterIndex, 0, text.Length);
        if (index == 0)
        {
            return 0;
        }

        int lineFeed = text.LastIndexOf('\n', index - 1);
        int carriageReturn = text.LastIndexOf('\r', index - 1);
        return Math.Max(lineFeed, carriageReturn) + 1;
    }

    public static bool IsTodoLineAt(string text, int lineStart) =>
        lineStart >= 0 &&
        lineStart + 1 < text.Length &&
        text[lineStart] is UncheckedMarker or CheckedMarker &&
        text[lineStart + 1] == ' ';

    public static char ToggleMarker(char marker) => marker switch
    {
        UncheckedMarker => CheckedMarker,
        CheckedMarker => UncheckedMarker,
        _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, "字符不是 TODO 标记。"),
    };

    private static void AddLineEdit(string text, int start, int length, List<TextEdit> edits)
    {
        if (length == 0)
        {
            return;
        }

        ReadOnlySpan<char> line = text.AsSpan(start, length);
        if (line.IsWhiteSpace())
        {
            edits.Add(new TextEdit(start, length, string.Empty));
            return;
        }

        if (line[0] is UncheckedMarker or CheckedMarker)
        {
            int contentStart = line.Length > 1 && line[1] == ' ' ? 2 : 1;
            if (line[contentStart..].IsWhiteSpace())
            {
                edits.Add(new TextEdit(start, length, string.Empty));
            }
            else if (contentStart == 1)
            {
                edits.Add(new TextEdit(start + 1, 0, " "));
            }

            return;
        }

        if (line.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
        {
            if (line[6..].IsWhiteSpace())
            {
                edits.Add(new TextEdit(start, length, string.Empty));
                return;
            }

            string prefix = line[3] is 'x' or 'X' ? CheckedPrefix : UncheckedPrefix;
            edits.Add(new TextEdit(start, 6, prefix));
            return;
        }

        edits.Add(new TextEdit(start, 0, UncheckedPrefix));
    }
}
