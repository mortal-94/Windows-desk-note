namespace DeskNote.Core.Search;

public readonly record struct TextMatch(int Index, int Length, bool Wrapped);

/// <summary>Deterministic, UI-independent full-text search used by the editor.</summary>
public static class TextSearchService
{
    public static TextMatch? FindNext(
        string text,
        string query,
        int selectionStart,
        int selectionLength,
        bool matchCase)
    {
        if (text.Length == 0 || query.Length == 0)
        {
            return null;
        }

        StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int start = Math.Clamp(selectionStart + selectionLength, 0, text.Length);
        int index = text.IndexOf(query, start, comparison);
        if (index >= 0)
        {
            return new TextMatch(index, query.Length, false);
        }

        index = text.IndexOf(query, 0, comparison);
        return index >= 0 ? new TextMatch(index, query.Length, true) : null;
    }

    public static TextMatch? FindPrevious(string text, string query, int selectionStart, bool matchCase)
    {
        if (text.Length == 0 || query.Length == 0)
        {
            return null;
        }

        StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (selectionStart > 0)
        {
            int index = text.LastIndexOf(query, Math.Min(selectionStart - 1, text.Length - 1), comparison);
            if (index >= 0)
            {
                return new TextMatch(index, query.Length, false);
            }
        }

        int wrappedIndex = text.LastIndexOf(query, comparison);
        return wrappedIndex >= 0 ? new TextMatch(wrappedIndex, query.Length, true) : null;
    }
}
