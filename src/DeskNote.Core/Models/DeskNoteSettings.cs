using DeskNote.Core.Input;

namespace DeskNote.Core.Models;

/// <summary>
/// Persisted user preferences. Values are normalized when loaded so a manually edited
/// or older configuration cannot create an unusable window.
/// </summary>
public sealed class DeskNoteSettings
{
    public int SchemaVersion { get; set; } = 1;
    public double WindowLeft { get; set; } = 80;
    public double WindowTop { get; set; } = 80;
    public double WindowWidth { get; set; } = 460;
    public double WindowHeight { get; set; } = 680;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 18;
    public double LineSpacing { get; set; } = 1.35;
    public string BackgroundColor { get; set; } = "#FFF8DC";
    public string ForegroundColor { get; set; } = "#202020";
    public double WindowOpacity { get; set; } = 0.94;
    public string Hotkey { get; set; } = "Ctrl+Alt+N";
    public double AutoSaveDelaySeconds { get; set; } = 2;
    public int BackupCount { get; set; } = 5;
    public int LastCaretIndex { get; set; } = -1;
    public bool SearchMatchCase { get; set; }

    public DeskNoteSettings Copy() => (DeskNoteSettings)MemberwiseClone();

    public void Normalize()
    {
        SchemaVersion = 1;
        WindowLeft = ClampFinite(WindowLeft, -100_000, 100_000, 80);
        WindowTop = ClampFinite(WindowTop, -100_000, 100_000, 80);
        WindowWidth = ClampFinite(WindowWidth, 280, 2_400, 460);
        WindowHeight = ClampFinite(WindowHeight, 240, 2_400, 680);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Microsoft YaHei UI" : FontFamily.Trim();
        FontSize = ClampFinite(FontSize, 10, 72, 18);
        LineSpacing = ClampFinite(LineSpacing, 1, 2.5, 1.35);
        WindowOpacity = ClampFinite(WindowOpacity, 0.45, 1, 0.94);
        AutoSaveDelaySeconds = ClampFinite(AutoSaveDelaySeconds, 0.5, 30, 2);
        BackupCount = Math.Clamp(BackupCount, 1, 20);
        LastCaretIndex = Math.Max(-1, LastCaretIndex);

        BackgroundColor = NormalizeColor(BackgroundColor, "#FFF8DC");
        ForegroundColor = NormalizeColor(ForegroundColor, "#202020");
        if (!HotkeyGesture.TryParse(Hotkey, out HotkeyGesture gesture, out _))
        {
            gesture = HotkeyGesture.Default;
        }

        Hotkey = gesture.ToString();
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static string NormalizeColor(string? value, string fallback)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#')
        {
            return fallback;
        }

        return value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF")
            ? fallback
            : value.ToUpperInvariant();
    }
}
