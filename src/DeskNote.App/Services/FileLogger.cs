using System.Text;
using System.IO;
using DeskNote.Core.Storage;

namespace DeskNote.App.Services;

internal sealed class FileLogger
{
    private readonly object _sync = new();
    private readonly AppDataPaths _paths;

    public FileLogger(AppDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureDirectories();
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception exception) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            string line = $"{DateTimeOffset.Now:O} [{level}] {message}" +
                          (exception is null ? string.Empty : $"{Environment.NewLine}{exception}") +
                          Environment.NewLine;
            lock (_sync)
            {
                File.AppendAllText(_paths.LogFilePath, line, new UTF8Encoding(false));
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            // Logging must never become a second application failure.
        }
    }
}
