using System.Text.Json;
using DeskNote.Core.Models;

namespace DeskNote.Core.Storage;

public sealed class SettingsStore(AppDataPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<DeskNoteSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.SettingsFilePath))
        {
            return new DeskNoteSettings();
        }

        try
        {
            await using FileStream stream = File.OpenRead(paths.SettingsFilePath);
            DeskNoteSettings settings = await JsonSerializer.DeserializeAsync<DeskNoteSettings>(stream, JsonOptions, cancellationToken)
                ?? new DeskNoteSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            PreserveInvalidConfiguration();
            return new DeskNoteSettings();
        }
    }

    public Task SaveAsync(DeskNoteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        paths.EnsureDirectories();
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        return AtomicFileWriter.WriteTextAsync(paths.SettingsFilePath, json + Environment.NewLine, cancellationToken);
    }

    private void PreserveInvalidConfiguration()
    {
        try
        {
            string invalidPath = Path.Combine(
                paths.RootDirectory,
                $"settings.invalid-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.json");
            File.Copy(paths.SettingsFilePath, invalidPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Loading defaults is more important than failing because the diagnostic copy failed.
        }
    }
}
