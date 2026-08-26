using System.Text;
using DeskNote.Core.Storage;
using DeskNote.Tests.TestSupport;

namespace DeskNote.Tests;

[TestClass]
public sealed class NoteRepositoryTests
{
    [TestMethod]
    public async Task SaveAndLoad_UsesUtf8WithoutBom()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        NoteRepository repository = new(paths);
        const string content = "第一行\r\nEnglish and 中文 😀";

        bool changed = await repository.SaveAsync(content, backupCount: 5);
        string loaded = await new NoteRepository(paths).LoadAsync();
        byte[] bytes = await File.ReadAllBytesAsync(paths.NoteFilePath);

        Assert.IsTrue(changed);
        Assert.AreEqual(content, loaded);
        Assert.IsFalse(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [TestMethod]
    public async Task Save_CreatesOldContentBackup_AndSkipsUnchangedContent()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        NoteRepository repository = new(paths, () => new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync("version 1", backupCount: 5);
        bool unchanged = await repository.SaveAsync("version 1", backupCount: 5);
        await repository.SaveAsync("version 2", backupCount: 5);

        string[] backups = Directory.GetFiles(paths.BackupDirectory, "note-*.txt");
        Assert.IsFalse(unchanged);
        Assert.HasCount(1, backups);
        Assert.AreEqual("version 1", await File.ReadAllTextAsync(backups[0]));
        Assert.AreEqual("version 2", await File.ReadAllTextAsync(paths.NoteFilePath));
    }

    [TestMethod]
    public async Task Save_PrunesBackupHistoryToConfiguredLimit()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        NoteRepository repository = new(paths, () => new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));

        for (int version = 1; version <= 6; version++)
        {
            await repository.SaveAsync($"version {version}", backupCount: 2);
        }

        string[] backups = Directory.GetFiles(paths.BackupDirectory, "note-*.txt");
        Assert.HasCount(2, backups);
        string[] retainedContents = await Task.WhenAll(backups.Select(path => File.ReadAllTextAsync(path)));
        CollectionAssert.AreEquivalent(new[] { "version 4", "version 5" }, retainedContents);
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task MultiMegabyteDocument_RoundTripsWithoutDataLoss()
    {
        using TempDirectory directory = new();
        AppDataPaths paths = new(directory.Path);
        string content = string.Concat(Enumerable.Repeat("长期笔记内容 0123456789 abcdefghijklmnopqrstuvwxyz\r\n", 80_000));
        NoteRepository repository = new(paths);

        await repository.SaveAsync(content, backupCount: 3);
        string loaded = await new NoteRepository(paths).LoadAsync();

        Assert.IsGreaterThan(3_000_000, content.Length);
        Assert.AreEqual(content.Length, loaded.Length);
        Assert.AreEqual(content, loaded);
    }
}
