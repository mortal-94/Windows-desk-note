using System.Text;

namespace DeskNote.Core.Storage;

/// <summary>Owns the human-readable note file and its bounded history.</summary>
public sealed class NoteRepository(AppDataPaths paths, Func<DateTimeOffset>? clock = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private string? _lastSavedContent;
    private bool _contentKnown;

    public async Task<string> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        string content = File.Exists(paths.NoteFilePath)
            ? await File.ReadAllTextAsync(paths.NoteFilePath, Encoding.UTF8, cancellationToken)
            : string.Empty;
        _lastSavedContent = content;
        _contentKnown = true;
        return content;
    }

    /// <returns><see langword="true"/> only when disk content changed.</returns>
    public async Task<bool> SaveAsync(string content, int backupCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        backupCount = Math.Clamp(backupCount, 1, 20);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_contentKnown && string.Equals(content, _lastSavedContent, StringComparison.Ordinal))
            {
                return false;
            }

            paths.EnsureDirectories();
            if (File.Exists(paths.NoteFilePath))
            {
                string backupPath = CreateUniqueBackupPath();
                File.Copy(paths.NoteFilePath, backupPath);
            }

            await AtomicFileWriter.WriteTextAsync(paths.NoteFilePath, content, cancellationToken);
            _lastSavedContent = content;
            _contentKnown = true;
            PruneBackups(backupCount);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string CreateUniqueBackupPath()
    {
        string stem = $"note-{_clock():yyyyMMdd-HHmmssfff}";
        int nextSuffix = 0;
        foreach (string existingPath in Directory.EnumerateFiles(paths.BackupDirectory, $"{stem}-*.txt"))
        {
            string existingStem = Path.GetFileNameWithoutExtension(existingPath);
            int separator = existingStem.LastIndexOf('-');
            if (separator >= 0 && int.TryParse(existingStem.AsSpan(separator + 1), out int suffix))
            {
                nextSuffix = Math.Max(nextSuffix, suffix + 1);
            }
        }

        return Path.Combine(paths.BackupDirectory, $"{stem}-{nextSuffix:D4}.txt");
    }

    private void PruneBackups(int backupCount)
    {
        foreach (FileInfo obsolete in new DirectoryInfo(paths.BackupDirectory)
                     .EnumerateFiles("note-*.txt")
                     // The sortable filename is authoritative; File.Copy preserves the
                     // source LastWriteTime, so filesystem timestamps can be misleading.
                     .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(backupCount))
        {
            obsolete.Delete();
        }
    }
}
