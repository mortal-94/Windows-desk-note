using System.Windows;
using System.Windows.Threading;
using DeskNote.App.Services;
using DeskNote.Core.Storage;

namespace DeskNote.App;

public partial class App : Application
{
    private const string ActivationEventName = "Local\\WindowsDeskNote.ShowForEditing.v1";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsSingleInstanceMutex;
    private FileLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\WindowsDeskNote.SingleInstance", out bool isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            try
            {
                using EventWaitHandle activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                _ = activationEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                MessageBox.Show(
                    "DeskNote 已经在运行，但未能唤出窗口。请使用全局快捷键。",
                    "DeskNote",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);

        AppDataPaths paths = AppDataPaths.CreateDefault();
        _logger = new FileLogger(paths);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        MainWindow window = new(paths, _logger);
        MainWindow = window;
        window.Show();
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !Dispatcher.HasShutdownStarted)
                {
                    _ = Dispatcher.BeginInvoke(window.ShowForEditingFromActivation);
                }
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        _activationWait?.Unregister(waitObject: null);
        _activationEvent?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("未处理的界面异常", e.Exception);
        MessageBox.Show($"DeskNote 遇到错误：\n{e.Exception.Message}\n\n详细信息已写入日志。", "DeskNote", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
