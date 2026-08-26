using DeskNote.Core.Text;

namespace DeskNote.Tests;

[TestClass]
public sealed class TodoTextServiceTests
{
    [TestMethod]
    public void Normalize_AddsUncheckedMarkerOnlyToNonEmptyLines()
    {
        string input = "第一项\r\n\r\n第二项\n   \n第三项";

        string result = TodoTextService.Normalize(input);

        Assert.AreEqual("☐ 第一项\r\n\r\n☐ 第二项\n\n☐ 第三项", result);
    }

    [TestMethod]
    public void Normalize_PreservesExistingStatesAndConvertsMarkdownTasks()
    {
        string input = "☐ 未完成\n☑ 已完成\n- [ ] 旧任务\n- [x] 旧完成";

        string result = TodoTextService.Normalize(input);

        Assert.AreEqual("☐ 未完成\n☑ 已完成\n☐ 旧任务\n☑ 旧完成", result);
    }

    [TestMethod]
    public void Normalize_RemovesMarkerFromContentlessLinesAndRepairsMissingSpace()
    {
        string input = "☐ \n☑   \n- [ ] \n- [x]   \n☐内容\n☑完成";

        string result = TodoTextService.Normalize(input);

        Assert.AreEqual("\n\n\n\n☐ 内容\n☑ 完成", result);
    }

    [TestMethod]
    public void Normalize_IsIdempotent()
    {
        const string input = "普通任务\r\n\r\n- [x] 完成任务\n☐ 已规范";

        string once = TodoTextService.Normalize(input);
        string twice = TodoTextService.Normalize(once);

        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void MapIndex_TracksCaretAndSelectionAcrossInsertedMarkers()
    {
        const string input = "abc\r\ndef";
        IReadOnlyList<TextEdit> edits = TodoTextService.GetNormalizationEdits(input);

        Assert.AreEqual(2, TodoTextService.MapIndex(0, edits));
        Assert.AreEqual(5, TodoTextService.MapIndex(3, edits));
        Assert.AreEqual(11, TodoTextService.MapIndex(7, edits));
    }

    [TestMethod]
    public void ToggleMarker_SwitchesBothWays()
    {
        Assert.AreEqual('☑', TodoTextService.ToggleMarker('☐'));
        Assert.AreEqual('☐', TodoTextService.ToggleMarker('☑'));
    }

    [TestMethod]
    public void FindLineStart_HandlesMixedNewlines()
    {
        const string text = "☐ one\r\n☑ two\n☐ three";

        Assert.AreEqual(0, TodoTextService.FindLineStart(text, 2));
        Assert.AreEqual(7, TodoTextService.FindLineStart(text, 10));
        Assert.AreEqual(13, TodoTextService.FindLineStart(text, text.Length));
    }
}
