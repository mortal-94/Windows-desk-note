using System.Text;

namespace DeskNote.Core.Storage;

/// <summary>
/// Writes a complete sibling temporary file before replacing the destination. A crash
/// can therefore leave either the old or new file, but never a deliberately truncated one.
/// </summary>
internal static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("目标文件必须包含目录。", nameof(path));
        Directory.CreateDirectory(directory);
        string temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (StreamWriter writer = new(stream, Utf8WithoutBom, 64 * 1024, leaveOpen: true))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
