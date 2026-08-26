using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DeskNote.Core.Input;
using DeskNote.Core.Models;

namespace DeskNote.App;

public partial class SettingsWindow : Window
{
    private readonly DeskNoteSettings _original;
    private readonly string _dataFolder;

    public SettingsWindow(DeskNoteSettings settings, string dataFolder)
    {
        InitializeComponent();
        _original = settings.Copy();
        _dataFolder = dataFolder;

        FontFamilyBox.ItemsSource = Fonts.SystemFontFamilies
            .OrderBy(font => font.Source, StringComparer.CurrentCultureIgnoreCase)
            .Select(font => font.Source)
            .ToArray();
        HotkeyBox.Text = settings.Hotkey;
        FontFamilyBox.Text = settings.FontFamily;
        FontSizeBox.Text = settings.FontSize.ToString("0.##", CultureInfo.CurrentCulture);
        LineSpacingBox.Text = settings.LineSpacing.ToString("0.##", CultureInfo.CurrentCulture);
        BackgroundBox.Text = settings.BackgroundColor;
        ForegroundBox.Text = settings.ForegroundColor;
        OpacitySlider.Value = settings.WindowOpacity * 100;
        AutoSaveBox.Text = settings.AutoSaveDelaySeconds.ToString("0.##", CultureInfo.CurrentCulture);
        BackupCountBox.Text = settings.BackupCount.ToString(CultureInfo.CurrentCulture);
    }

    public DeskNoteSettings? ResultSettings { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(HotkeyBox.Text, out HotkeyGesture hotkey, out string hotkeyError))
        {
            ShowValidationError(hotkeyError, HotkeyBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(FontFamilyBox.Text))
        {
            ShowValidationError("字体不能为空。", FontFamilyBox);
            return;
        }

        if (!TryReadNumber(FontSizeBox, 10, 72, "字号", out double fontSize) ||
            !TryReadNumber(LineSpacingBox, 1, 2.5, "行距倍数", out double lineSpacing) ||
            !TryReadNumber(AutoSaveBox, 0.5, 30, "自动保存延迟", out double autoSaveDelay) ||
            !TryReadInteger(BackupCountBox, 1, 20, "备份份数", out int backupCount))
        {
            return;
        }

        if (!IsHexColor(BackgroundBox.Text))
        {
            ShowValidationError("背景色必须是 #RRGGBB 或 #AARRGGBB。", BackgroundBox);
            return;
        }

        if (!IsHexColor(ForegroundBox.Text))
        {
            ShowValidationError("文字颜色必须是 #RRGGBB 或 #AARRGGBB。", ForegroundBox);
            return;
        }

        DeskNoteSettings result = _original.Copy();
        result.Hotkey = hotkey.ToString();
        result.FontFamily = FontFamilyBox.Text.Trim();
        result.FontSize = fontSize;
        result.LineSpacing = lineSpacing;
        result.BackgroundColor = BackgroundBox.Text.Trim().ToUpperInvariant();
        result.ForegroundColor = ForegroundBox.Text.Trim().ToUpperInvariant();
        result.WindowOpacity = OpacitySlider.Value / 100;
        result.AutoSaveDelaySeconds = autoSaveDelay;
        result.BackupCount = backupCount;
        result.Normalize();

        ResultSettings = result;
        DialogResult = true;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_dataFolder);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_dataFolder}\"",
            UseShellExecute = true,
        });
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue is not null)
        {
            OpacityValue.Text = $"{e.NewValue:0}%";
        }
    }

    private static bool TryReadNumber(
        System.Windows.Controls.TextBox textBox,
        double minimum,
        double maximum,
        string fieldName,
        out double value)
    {
        bool parsed = double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                      double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (parsed && double.IsFinite(value) && value >= minimum && value <= maximum)
        {
            return true;
        }

        ShowValidationError($"{fieldName}必须在 {minimum} 到 {maximum} 之间。", textBox);
        return false;
    }

    private static bool TryReadInteger(
        System.Windows.Controls.TextBox textBox,
        int minimum,
        int maximum,
        string fieldName,
        out int value)
    {
        if (int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) &&
            value >= minimum && value <= maximum)
        {
            return true;
        }

        ShowValidationError($"{fieldName}必须是 {minimum} 到 {maximum} 之间的整数。", textBox);
        return false;
    }

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 or 9 } &&
        value[0] == '#' &&
        !value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF");

    private static void ShowValidationError(string message, System.Windows.Controls.Control control)
    {
        MessageBox.Show(message, "设置有误", MessageBoxButton.OK, MessageBoxImage.Warning);
        control.Focus();
    }
}
