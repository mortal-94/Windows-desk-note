namespace DeskNote.Core.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public string NoteFilePath => Path.Combine(RootDirectory, "note.txt");
    public string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");
    public string BackupDirectory => Path.Combine(RootDirectory, "backups");
    public string LogFilePath => Path.Combine(RootDirectory, "desknote.log");

    public static AppDataPaths CreateDefault()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppDataPaths(Path.Combine(localData, "WindowsDeskNote"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }
}
