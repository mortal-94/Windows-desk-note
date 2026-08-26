using DeskNote.Core.Search;

namespace DeskNote.Tests;

[TestClass]
public sealed class TextSearchServiceTests
{
    [TestMethod]
    public void FindNext_ContinuesAfterSelection_AndWraps()
    {
        const string text = "Alpha beta ALPHA";

        TextMatch first = TextSearchService.FindNext(text, "alpha", 0, 0, matchCase: false)!.Value;
        TextMatch second = TextSearchService.FindNext(text, "alpha", first.Index, first.Length, matchCase: false)!.Value;
        TextMatch wrapped = TextSearchService.FindNext(text, "alpha", second.Index, second.Length, matchCase: false)!.Value;

        Assert.AreEqual(0, first.Index);
        Assert.AreEqual(11, second.Index);
        Assert.AreEqual(0, wrapped.Index);
        Assert.IsTrue(wrapped.Wrapped);
    }

    [TestMethod]
    public void FindPrevious_SearchesBackward_AndWraps()
    {
        const string text = "one two one";

        TextMatch previous = TextSearchService.FindPrevious(text, "one", text.Length, matchCase: true)!.Value;
        TextMatch first = TextSearchService.FindPrevious(text, "one", previous.Index, matchCase: true)!.Value;
        TextMatch wrapped = TextSearchService.FindPrevious(text, "one", first.Index, matchCase: true)!.Value;

        Assert.AreEqual(8, previous.Index);
        Assert.AreEqual(0, first.Index);
        Assert.AreEqual(8, wrapped.Index);
        Assert.IsTrue(wrapped.Wrapped);
    }

    [TestMethod]
    public void FindNext_RespectsCaseAndMissingOrEmptyQueries()
    {
        Assert.IsNull(TextSearchService.FindNext("Alpha", "alpha", 0, 0, matchCase: true));
        Assert.IsNull(TextSearchService.FindNext("Alpha", string.Empty, 0, 0, matchCase: false));
        Assert.IsNull(TextSearchService.FindNext(string.Empty, "a", 0, 0, matchCase: false));
    }
}
