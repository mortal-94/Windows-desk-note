using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DeskNote.Core.Search;

namespace DeskNote.PerformanceProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int requestedMiCharacters = args.Length > 0 && int.TryParse(args[0], out int parsed) ? Math.Clamp(parsed, 1, 100) : 5;
        int targetCharacters = requestedMiCharacters * 1024 * 1024;
        const string line = "长期单页笔记 0123456789 abcdefghijklmnopqrstuvwxyz 搜索目标\r\n";
        StringBuilder builder = new(targetCharacters + line.Length);
        while (builder.Length < targetCharacters)
        {
            builder.Append(line);
        }

        string content = builder.ToString();
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        TextBox editor = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Width = 460,
            Height = 680,
        };
        editor.SetValue(TextBlock.LineHeightProperty, 24.3);
        editor.SetValue(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);

        Stopwatch loadTimer = Stopwatch.StartNew();
        editor.Text = content;
        editor.Measure(new Size(460, 680));
        editor.Arrange(new Rect(0, 0, 460, 680));
        editor.UpdateLayout();
        loadTimer.Stop();

        Stopwatch searchTimer = Stopwatch.StartNew();
        TextMatch? match = TextSearchService.FindPrevious(editor.Text, "搜索目标", editor.Text.Length, matchCase: true);
        searchTimer.Stop();
        long memoryAfter = GC.GetTotalMemory(forceFullCollection: true);

        object result = new
        {
            requestedMiCharacters,
            characters = content.Length,
            editorLoadAndLayoutMilliseconds = loadTimer.ElapsedMilliseconds,
            fullTextSearchMilliseconds = searchTimer.ElapsedMilliseconds,
            managedMemoryDeltaMiB = Math.Round((memoryAfter - memoryBefore) / 1024d / 1024d, 2),
            integrityPassed = editor.Text.Length == content.Length && match is not null,
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return editor.Text.Length == content.Length && match is not null ? 0 : 1;
    }
}
