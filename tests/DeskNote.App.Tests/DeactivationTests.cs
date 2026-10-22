using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeskNote.App;
using DeskNote.App.Services;
using DeskNote.Core.Storage;

[assembly: DoNotParallelize]

namespace DeskNote.App.Tests;

[TestClass]
public sealed class DeactivationTests
{
    [STATestMethod]
    public void Deactivated_FlushesLatestContentAndPreservesPreviousNote()
    {
        using Fixture fixture = new();
        fixture.Editor.Text = "☐ 最新内容";
        Deactivate(fixture.Window);
        PumpUntil(() => File.ReadAllText(fixture.Paths.NoteFilePath) == fixture.Editor.Text);
        Assert.IsTrue(Directory.GetFiles(fixture.Paths.BackupDirectory)
            .Any(path => File.ReadAllText(path) == Fixture.Original));
    }

    [STATestMethod]
    public void Deactivated_DefersWhileSettingsIsOpenAndRechecksAfterwards()
    {
        using Fixture fixture = new();
        fixture.Editor.Text = "☐ 设置期间的内容";
        SetField(fixture.Window, "_modalDialogOpen", true);
        Deactivate(fixture.Window);
        DrainDispatcher();
        Assert.AreEqual(Fixture.Original, File.ReadAllText(fixture.Paths.NoteFilePath));

        SetField(fixture.Window, "_modalDialogOpen", false);
        Invoke(fixture.Window, "ScheduleDeactivationCheck");
        PumpUntil(() => File.ReadAllText(fixture.Paths.NoteFilePath) == fixture.Editor.Text);
    }

    [STATestMethod]
    public void ContextMenuClosed_RechecksExternalFocusLoss()
    {
        using Fixture fixture = new();
        fixture.Editor.Text = "☐ 菜单关闭后的内容";
        fixture.Editor.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        PumpUntil(() => File.ReadAllText(fixture.Paths.NoteFilePath) == fixture.Editor.Text);
    }

    [STATestMethod]
    public void Deactivated_IgnoresDesktopModeAndConcurrentTransitions()
    {
        using Fixture fixture = new();
        fixture.Editor.Text = "☐ 不应写入";
        SetField(fixture.Window, "_editing", false);
        Deactivate(fixture.Window);
        DrainDispatcher();
        Assert.AreEqual(Fixture.Original, File.ReadAllText(fixture.Paths.NoteFilePath));

        SetField(fixture.Window, "_editing", true);
        SetField(fixture.Window, "_transitioning", true);
        Deactivate(fixture.Window);
        DrainDispatcher();
        Assert.AreEqual(Fixture.Original, File.ReadAllText(fixture.Paths.NoteFilePath));
        SetField(fixture.Window, "_transitioning", false);
    }

    [STATestMethod]
    public void Deactivated_SettlesPendingTodoCompositionBeforeSaving()
    {
        using Fixture fixture = new();
        SetField(fixture.Window, "_textCompositionActive", true);
        fixture.Editor.Text = "中文输入";
        Deactivate(fixture.Window);
        PumpUntil(() => File.ReadAllText(fixture.Paths.NoteFilePath) == "☐ 中文输入");
        Assert.AreEqual("☐ 中文输入", fixture.Editor.Text);
    }

    [STATestMethod]
    public void Deactivated_SaveFailureKeepsOriginalFileAndEditableMemory()
    {
        using Fixture fixture = new();
        fixture.Editor.Text = "☐ 必须保留的内存内容";
        using FileStream locked = new(fixture.Paths.NoteFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        Deactivate(fixture.Window);
        TextBlock status = (TextBlock)fixture.Window.FindName("StatusText");
        PumpUntil(() => status.Text.StartsWith("保存失败：", StringComparison.Ordinal));
        Assert.AreEqual("☐ 必须保留的内存内容", fixture.Editor.Text);
        Assert.IsFalse(fixture.Editor.IsReadOnly);
        locked.Dispose();
        Assert.AreEqual(Fixture.Original, File.ReadAllText(fixture.Paths.NoteFilePath));
    }

    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static void Invoke(MainWindow window, string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    private static void Deactivate(MainWindow window) =>
        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);

    private static void DrainDispatcher() => PumpUntil(() => true);

    private static void PumpUntil(Func<bool> completed)
    {
        DispatcherFrame frame = new();
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        DispatcherTimer timer = new(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) =>
        {
            if (completed() || DateTime.UtcNow >= deadline)
            {
                frame.Continue = false;
            }
        };
        timer.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }

        Assert.IsTrue(completed(), "Timed out waiting for the deactivation/save path.");
    }

    // No window is shown, no hotkey is registered, and App.OnStartup is never called.
    // These STA regressions exercise the actual XAML events and persistence path without
    // taking focus or touching either the running app or the default user data directory.
    private sealed class Fixture : IDisposable
    {
        internal const string Original = "☐ 原有笔记";
        public AppDataPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "DeskNote.FocusTests", Guid.NewGuid().ToString("N")));
        public MainWindow Window { get; }
        public TextBox Editor { get; }

        public Fixture()
        {
            Paths.EnsureDirectories();
            File.WriteAllText(Paths.NoteFilePath, Original);
            Window = new MainWindow(Paths, new FileLogger(Paths));
            Editor = (TextBox)Window.FindName("NoteEditor");
            Editor.Text = Original;
            SetField(Window, "_initialized", true);
            SetField(Window, "_editing", true);
        }

        public void Dispose()
        {
            PumpUntil(() => !(bool)typeof(MainWindow)
                .GetField("_transitioning", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!);
            DebouncedTextSaver saver = (DebouncedTextSaver)typeof(MainWindow)
                .GetField("_autoSaver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
            Task disposal = saver.DisposeAsync().AsTask();
            PumpUntil(() => disposal.IsCompleted);
            disposal.GetAwaiter().GetResult();
            SetField(Window, "_allowClose", true);
            Window.Close();
            Directory.Delete(Paths.RootDirectory, recursive: true);
        }
    }
}
