using DeskNote.Core.Models;
using DeskNote.Core.Storage;
using DeskNote.Tests.TestSupport;

namespace DeskNote.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsNormalizedSettings()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        SettingsStore store = new(paths);
        DeskNoteSettings settings = new()
        {
            FontFamily = "Consolas",
            FontSize = 21,
            Hotkey = "control+alt+d",
            BackupCount = 7,
            LastCaretIndex = 42,
        };

        await store.SaveAsync(settings);
        DeskNoteSettings loaded = await store.LoadAsync();

        Assert.AreEqual("Consolas", loaded.FontFamily);
        Assert.AreEqual(21, loaded.FontSize);
        Assert.AreEqual("Ctrl+Alt+D", loaded.Hotkey);
        Assert.AreEqual(7, loaded.BackupCount);
        Assert.AreEqual(42, loaded.LastCaretIndex);
    }

    [TestMethod]
    public async Task Load_CorruptJson_PreservesFileAndReturnsDefaults()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{ not valid json");

        DeskNoteSettings loaded = await new SettingsStore(paths).LoadAsync();

        Assert.AreEqual("Ctrl+Alt+N", loaded.Hotkey);
        Assert.HasCount(1, Directory.GetFiles(paths.RootDirectory, "settings.invalid-*.json"));
    }

    [TestMethod]
    public async Task Load_OutOfRangeValues_NormalizesThem()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFilePath,
            """
            {
              "windowWidth": 10,
              "fontSize": 500,
              "windowOpacity": 0.1,
              "backgroundColor": "invalid",
              "hotkey": "N",
              "backupCount": 999
            }
            """);

        DeskNoteSettings loaded = await new SettingsStore(paths).LoadAsync();

        Assert.AreEqual(280, loaded.WindowWidth);
        Assert.AreEqual(72, loaded.FontSize);
        Assert.AreEqual(0.45, loaded.WindowOpacity);
        Assert.AreEqual("#FFF8DC", loaded.BackgroundColor);
        Assert.AreEqual("Ctrl+Alt+N", loaded.Hotkey);
        Assert.AreEqual(20, loaded.BackupCount);
    }
}
