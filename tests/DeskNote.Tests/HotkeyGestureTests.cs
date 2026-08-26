using DeskNote.Core.Input;

namespace DeskNote.Tests;

[TestClass]
public sealed class HotkeyGestureTests
{
    [TestMethod]
    [DataRow("control + alt + n", "Ctrl+Alt+N", 0x4E)]
    [DataRow("Win+Shift+F11", "Shift+Win+F11", 0x7A)]
    [DataRow("Ctrl+PageDown", "Ctrl+PageDown", 0x22)]
    public void TryParse_ValidGesture_ReturnsCanonicalForm(string source, string canonical, int virtualKey)
    {
        bool parsed = HotkeyGesture.TryParse(source, out HotkeyGesture gesture, out string error);

        Assert.IsTrue(parsed, error);
        Assert.AreEqual(canonical, gesture.ToString());
        Assert.AreEqual((uint)virtualKey, gesture.VirtualKey);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("N")]
    [DataRow("Shift+N")]
    [DataRow("Ctrl+F12")]
    [DataRow("Ctrl+Alt+N+M")]
    [DataRow("Ctrl+Ctrl+N")]
    public void TryParse_InvalidGesture_ReturnsExplanation(string source)
    {
        bool parsed = HotkeyGesture.TryParse(source, out _, out string error);

        Assert.IsFalse(parsed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }
}
