using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskNote.App.Interop;
using DeskNote.App.Services;
using DeskNote.Core.Input;
using DeskNote.Core.Models;
using DeskNote.Core.Search;
using DeskNote.Core.Storage;
using DeskNote.Core.Text;

namespace DeskNote.App;

public partial class MainWindow : Window
{
    private readonly AppDataPaths _paths;
    private readonly FileLogger _logger;
    private readonly SettingsStore _settingsStore;
    private readonly NoteRepository _noteRepository;
    private readonly DebouncedTextSaver _autoSaver;
    private readonly DispatcherTimer _desktopHealthTimer;
    private readonly DispatcherTimer _desktopRecoveryTimer;
    private DeskNoteSettings _settings = new();
    private HwndSource? _messageSource;
    private DesktopLayerService? _desktopLayer;
    private GlobalHotkeyService? _globalHotkey;
    private uint _taskbarCreatedMessage;
    private bool _initialized;
    private bool _editing;
    private bool _transitioning;
    private bool _suppressTextChanged;
    private bool _modalDialogOpen;
    private bool _allowClose;
    private bool _showForEditingPending;
    private bool _textCompositionActive;
    private bool _todoNormalizationPending;
    private bool _forceDesktopRecoveryPending;
    private int _desktopRecoveryFailures;

    internal MainWindow(AppDataPaths paths, FileLogger logger)
    {
        InitializeComponent();
        _paths = paths;
        _logger = logger;
        _settingsStore = new SettingsStore(paths);
        _noteRepository = new NoteRepository(paths);
        _autoSaver = new DebouncedTextSaver(SaveSnapshotAsync);
        _autoSaver.SaveFailed += AutoSaver_SaveFailed;
        TextCompositionManager.AddPreviewTextInputStartHandler(NoteEditor, NoteEditor_PreviewTextInputStart);

        _desktopHealthTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _desktopHealthTimer.Tick += DesktopHealthTimer_Tick;
        _desktopRecoveryTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _desktopRecoveryTimer.Tick += DesktopRecoveryTimer_Tick;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        _desktopLayer = new DesktopLayerService(handle);

        // Keep a hidden top-level HWND for broadcasts and WM_HOTKEY. The visible note
        // becomes an Explorer child while resting on the desktop and would otherwise
        // stop receiving top-level broadcasts such as TaskbarCreated.
        HwndSourceParameters parameters = new("DeskNote.MessageSink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)NativeMethods.WsPopup),
            ExtendedWindowStyle = unchecked((int)NativeMethods.WsExToolWindow),
        };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WindowMessageHook);
        _globalHotkey = new GlobalHotkeyService(_messageSource.Handle);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            string note = await _noteRepository.LoadAsync();
            string normalizedNote = TodoTextService.Normalize(note);
            _suppressTextChanged = true;
            NoteEditor.Text = normalizedNote;
            _suppressTextChanged = false;
            if (!string.Equals(note, normalizedNote, StringComparison.Ordinal))
            {
                // Migrating an existing plain note is a normal save: the original file
                // is retained in backups and the new file stays readable UTF-8 text.
                await _noteRepository.SaveAsync(normalizedNote, _settings.BackupCount);
            }

            ApplySettings();
            RestoreWindowBounds();
            _initialized = true;

            bool hotkeyReady = RegisterConfiguredHotkey(showError: true);
            string desktopError = "桌面层服务尚未初始化。";
            if (hotkeyReady && _desktopLayer is not null && _desktopLayer.TryEmbed(out desktopError))
            {
                ApplyDesktopModeUi();
                _logger.Info("应用启动并进入桌面层。");
            }
            else
            {
                EnterEditingMode();
                SetStatus(hotkeyReady ? desktopError : "全局快捷键不可用，请先在设置中更换。", isError: true);
            }

            if (_showForEditingPending)
            {
                ShowForEditing();
            }
        }
        catch (Exception exception)
        {
            _initialized = true;
            _logger.Error("初始化失败", exception);
            EnterEditingMode();
            SetStatus($"初始化失败：{exception.Message}", isError: true);
        }
    }

    private async Task SaveSnapshotAsync(string content, CancellationToken cancellationToken)
    {
        bool changed = await _noteRepository.SaveAsync(content, _settings.BackupCount, cancellationToken);
        if (changed && !Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(() => SetStatus($"已自动保存 {DateTime.Now:HH:mm:ss}"), DispatcherPriority.Background);
        }
    }

    private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _suppressTextChanged)
        {
            return;
        }

        if (_textCompositionActive)
        {
            // Pinyin letters are transient composition text. Mutating the TextBox here
            // would make the IME commit the first letter as ordinary Latin input.
            _todoNormalizationPending = true;
            return;
        }

        NormalizeAndScheduleAutoSave();
    }

    private async Task ToggleEditingModeAsync()
    {
        if (!_initialized || _transitioning || _modalDialogOpen)
        {
            return;
        }

        if (_editing)
        {
            await ExitEditingModeAsync();
        }
        else
        {
            EnterEditingMode();
        }
    }

    private void EnterEditingMode()
    {
        _desktopHealthTimer.Stop();
        _desktopRecoveryTimer.Stop();
        _forceDesktopRecoveryPending = false;
        _desktopRecoveryFailures = 0;
        _desktopLayer?.DetachForEditing();
        _editing = true;
        HeaderPanel.Visibility = Visibility.Visible;
        FooterPanel.Visibility = Visibility.Visible;
        WindowBorder.BorderThickness = new Thickness(2);
        ResizeMode = ResizeMode.CanResizeWithGrip;
        NoteEditor.IsReadOnly = false;
        NoteEditor.IsHitTestVisible = true;
        NoteEditor.IsTabStop = true;
        SetStatus("正在编辑");

        _ = Dispatcher.BeginInvoke(() =>
        {
            Activate();
            NoteEditor.Focus();
            ScrollToDocumentEnd(moveCaret: true);
        }, DispatcherPriority.Input);
    }

    private async Task ExitEditingModeAsync()
    {
        _transitioning = true;
        try
        {
            CloseSearch();
            NoteEditor.IsReadOnly = true;
            CaptureWindowState();
            _settings.LastCaretIndex = NoteEditor.CaretIndex;
            await _autoSaver.FlushAsync(NoteEditor.Text);
            await _settingsStore.SaveAsync(_settings);

            string error = "桌面层服务尚未初始化。";
            if (_desktopLayer is not null && _desktopLayer.TryEmbed(out error))
            {
                ApplyDesktopModeUi();
            }
            else
            {
                EnterEditingMode();
                SetStatus(error, isError: true);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("退出编辑状态时保存失败", exception);
            EnterEditingMode();
            SetStatus($"保存失败：{exception.Message}", isError: true);
        }
        finally
        {
            _transitioning = false;
        }
    }

    private void ApplyDesktopModeUi()
    {
        _editing = false;
        HeaderPanel.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Collapsed;
        FooterPanel.Visibility = Visibility.Collapsed;
        WindowBorder.BorderThickness = new Thickness(1);
        ResizeMode = ResizeMode.NoResize;
        NoteEditor.IsReadOnly = true;
        NoteEditor.IsHitTestVisible = false;
        NoteEditor.IsTabStop = false;
        Keyboard.ClearFocus();
        ScheduleScrollToDocumentEnd(moveCaret: false);
        _desktopRecoveryFailures = 0;
        _desktopHealthTimer.Start();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_initialized || _editing || _transitioning || _modalDialogOpen)
        {
            return;
        }

        // The desktop child uses WS_EX_NOACTIVATE, so this click does not steal focus
        // until we explicitly detach it and switch to the normal editor window.
        e.Handled = true;
        EnterEditingMode();
    }

    private void ApplySettings()
    {
        _settings.Normalize();
        FontFamily family = new(_settings.FontFamily);
        Brush background = ParseBrush(_settings.BackgroundColor, Brushes.Cornsilk);
        Brush foreground = ParseBrush(_settings.ForegroundColor, Brushes.Black);
        FontFamily = family;
        FontSize = _settings.FontSize;
        Background = background;
        WindowBorder.Background = background;
        NoteEditor.Background = background;
        NoteEditor.Foreground = foreground;
        NoteEditor.FontFamily = family;
        NoteEditor.FontSize = _settings.FontSize;
        NoteEditor.SetValue(TextBlock.LineHeightProperty, _settings.FontSize * _settings.LineSpacing);
        NoteEditor.SetValue(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
        Opacity = _settings.WindowOpacity;
        MatchCaseCheckBox.IsChecked = _settings.SearchMatchCase;
    }

    private void RestoreWindowBounds()
    {
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        Left = _settings.WindowLeft;
        Top = _settings.WindowTop;

        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        const double visibleMargin = 48;
        if (Left + visibleMargin > virtualRight || Left + Width - visibleMargin < virtualLeft ||
            Top + visibleMargin > virtualBottom || Top + Height - visibleMargin < virtualTop)
        {
            Left = virtualLeft + 80;
            Top = virtualTop + 80;
        }
    }

    private void CaptureWindowState()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top) ||
            !double.IsFinite(ActualWidth) || !double.IsFinite(ActualHeight))
        {
            return;
        }

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = Math.Max(MinWidth, ActualWidth);
        _settings.WindowHeight = Math.Max(MinHeight, ActualHeight);
    }

    private bool RegisterConfiguredHotkey(bool showError)
    {
        string parseError = "全局快捷键服务尚未初始化。";
        if (_globalHotkey is null || !HotkeyGesture.TryParse(_settings.Hotkey, out HotkeyGesture gesture, out parseError))
        {
            if (showError)
            {
                MessageBox.Show(parseError, "DeskNote 快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        if (_globalHotkey.TryRegister(gesture, out string registrationError))
        {
            return true;
        }

        _logger.Error("注册全局快捷键失败", new InvalidOperationException(registrationError));
        if (showError)
        {
            MessageBox.Show(registrationError, "DeskNote 快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return false;
    }

    private nint WindowMessageHook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == NativeMethods.WmHotkey && wParam == NativeMethods.HotkeyId)
        {
            handled = true;
            _ = Dispatcher.BeginInvoke(() => _ = ToggleEditingModeAsync(), DispatcherPriority.Input);
        }
        else if ((uint)message == NativeMethods.WmDisplayChange && _initialized && !_editing)
        {
            ScheduleDesktopRecovery(TimeSpan.FromSeconds(1), force: true);
        }
        else if (_taskbarCreatedMessage != 0 && (uint)message == _taskbarCreatedMessage)
        {
            ScheduleDesktopRecovery(TimeSpan.FromSeconds(1), force: true);
        }
        else if (_initialized && !_editing && IsDesktopRefreshMessage((uint)message, wParam))
        {
            ScheduleDesktopRecovery(TimeSpan.FromSeconds(1), force: true);
        }

        return nint.Zero;
    }

    internal void ShowForEditingFromActivation()
    {
        if (!_initialized)
        {
            _showForEditingPending = true;
            return;
        }

        ShowForEditing();
    }

    private void ShowForEditing()
    {
        if (!_initialized || _transitioning || _modalDialogOpen)
        {
            return;
        }

        if (!_editing)
        {
            EnterEditingMode();
            _showForEditingPending = false;
            return;
        }

        Activate();
        NoteEditor.Focus();
        ScrollToDocumentEnd(moveCaret: true);
        _showForEditingPending = false;
    }

    private static bool IsDesktopRefreshMessage(uint message, nint wParam)
    {
        if (message is NativeMethods.WmSettingChange or
            NativeMethods.WmThemeChanged or
            NativeMethods.WmDwmCompositionChanged)
        {
            return true;
        }

        return message == NativeMethods.WmPowerBroadcast &&
               (uint)wParam is NativeMethods.PbtApmResumeSuspend or NativeMethods.PbtApmResumeAutomatic;
    }

    private void DesktopHealthTimer_Tick(object? sender, EventArgs e)
    {
        if (!_initialized || _editing || _transitioning || _desktopLayer is null)
        {
            return;
        }

        if (!_desktopLayer.IsHealthy)
        {
            ScheduleDesktopRecovery(TimeSpan.Zero, force: false);
        }
    }

    private void ScheduleDesktopRecovery(TimeSpan delay, bool force)
    {
        if (!_initialized || _editing || _transitioning)
        {
            return;
        }

        _forceDesktopRecoveryPending |= force;
        _desktopRecoveryTimer.Stop();
        _desktopRecoveryTimer.Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay;
        _desktopRecoveryTimer.Start();
    }

    private void DesktopRecoveryTimer_Tick(object? sender, EventArgs e)
    {
        _desktopRecoveryTimer.Stop();
        if (!_initialized || _editing || _transitioning || _desktopLayer is null)
        {
            return;
        }

        bool force = _forceDesktopRecoveryPending;
        _forceDesktopRecoveryPending = false;
        if (!force && _desktopLayer.IsHealthy)
        {
            _desktopRecoveryFailures = 0;
            return;
        }

        if (_desktopLayer.TryEmbed(out string error))
        {
            _desktopRecoveryFailures = 0;
            ScheduleScrollToDocumentEnd(moveCaret: false);
            _logger.Info("桌面层健康检查已恢复笔记显示。");
            return;
        }

        _desktopRecoveryFailures++;
        _logger.Error(
            $"桌面层恢复失败（{_desktopRecoveryFailures}/3）",
            new InvalidOperationException(error));
        if (_desktopRecoveryFailures < 3)
        {
            ScheduleDesktopRecovery(TimeSpan.FromSeconds(2), force: true);
            return;
        }

        _desktopRecoveryFailures = 0;
        EnterEditingMode();
        SetStatus($"桌面层恢复失败：{error}", isError: true);
    }

    private void NormalizeEditorTodoLines()
    {
        string originalText = NoteEditor.Text;
        IReadOnlyList<TextEdit> edits = TodoTextService.GetNormalizationEdits(originalText);
        if (edits.Count == 0)
        {
            return;
        }

        int originalSelectionStart = NoteEditor.SelectionStart;
        int originalSelectionEnd = originalSelectionStart + NoteEditor.SelectionLength;
        int mappedSelectionStart = TodoTextService.MapIndex(originalSelectionStart, edits);
        int mappedSelectionEnd = TodoTextService.MapIndex(originalSelectionEnd, edits);

        _suppressTextChanged = true;
        NoteEditor.BeginChange();
        try
        {
            for (int index = edits.Count - 1; index >= 0; index--)
            {
                TextEdit edit = edits[index];
                NoteEditor.Select(edit.Start, edit.Length);
                NoteEditor.SelectedText = edit.Replacement;
            }
        }
        finally
        {
            NoteEditor.EndChange();
            _suppressTextChanged = false;
        }

        int safeStart = Math.Clamp(mappedSelectionStart, 0, NoteEditor.Text.Length);
        int safeEnd = Math.Clamp(mappedSelectionEnd, safeStart, NoteEditor.Text.Length);
        NoteEditor.Select(safeStart, safeEnd - safeStart);
    }

    private void NoteEditor_PreviewTextInputStart(object sender, TextCompositionEventArgs e)
    {
        if (_editing)
        {
            _textCompositionActive = true;
        }
    }

    private void NoteEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_textCompositionActive)
        {
            return;
        }

        _todoNormalizationPending = true;
        _ = Dispatcher.BeginInvoke(CompleteTextComposition, DispatcherPriority.Background);
    }

    private void NoteEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_textCompositionActive)
        {
            _ = Dispatcher.BeginInvoke(CompleteTextComposition, DispatcherPriority.Background);
        }
    }

    private void CompleteTextComposition()
    {
        _textCompositionActive = false;
        if (!_initialized || !_todoNormalizationPending)
        {
            return;
        }

        _todoNormalizationPending = false;
        NormalizeAndScheduleAutoSave();
    }

    private void NormalizeAndScheduleAutoSave()
    {
        NormalizeEditorTodoLines();
        SetStatus("有未保存的更改…");
        _autoSaver.Schedule(NoteEditor.Text, TimeSpan.FromSeconds(_settings.AutoSaveDelaySeconds));
    }

    private void NoteEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || NoteEditor.IsReadOnly)
        {
            return;
        }

        Point point = e.GetPosition(NoteEditor);
        int characterIndex = NoteEditor.GetCharacterIndexFromPoint(point, snapToText: true);
        if (characterIndex < 0)
        {
            return;
        }

        int lineStart = TodoTextService.FindLineStart(NoteEditor.Text, characterIndex);
        if (!TodoTextService.IsTodoLineAt(NoteEditor.Text, lineStart))
        {
            return;
        }

        Rect markerBounds = NoteEditor.GetRectFromCharacterIndex(lineStart, trailingEdge: false);
        double clickableRight = markerBounds.X + Math.Max(22, NoteEditor.FontSize * 1.65);
        if (point.X < markerBounds.X - 4 || point.X > clickableRight)
        {
            return;
        }

        int selectionStart = NoteEditor.SelectionStart;
        int selectionLength = NoteEditor.SelectionLength;
        char replacement = TodoTextService.ToggleMarker(NoteEditor.Text[lineStart]);
        _suppressTextChanged = true;
        NoteEditor.BeginChange();
        try
        {
            NoteEditor.Select(lineStart, 1);
            NoteEditor.SelectedText = replacement.ToString();
        }
        finally
        {
            NoteEditor.EndChange();
            _suppressTextChanged = false;
        }

        NoteEditor.Select(
            Math.Clamp(selectionStart, 0, NoteEditor.Text.Length),
            Math.Clamp(selectionLength, 0, NoteEditor.Text.Length - Math.Clamp(selectionStart, 0, NoteEditor.Text.Length)));
        SetStatus(replacement == TodoTextService.CheckedMarker ? "已完成" : "已取消完成");
        _autoSaver.Schedule(NoteEditor.Text, TimeSpan.FromSeconds(_settings.AutoSaveDelaySeconds));
        e.Handled = true;
    }

    private void ScheduleScrollToDocumentEnd(bool moveCaret)
    {
        _ = Dispatcher.BeginInvoke(
            () => ScrollToDocumentEnd(moveCaret),
            DispatcherPriority.Loaded);
    }

    private void ScrollToDocumentEnd(bool moveCaret)
    {
        if (moveCaret)
        {
            NoteEditor.Select(NoteEditor.Text.Length, 0);
            _settings.LastCaretIndex = NoteEditor.Text.Length;
        }

        NoteEditor.ScrollToEnd();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SearchPanel.Visibility == Visibility.Visible)
            {
                CloseSearch();
            }
            else
            {
                _ = ExitEditingModeAsync();
            }

            e.Handled = true;
        }
    }

    private void OpenSearch()
    {
        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CloseSearch()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        if (_editing)
        {
            NoteEditor.Focus();
        }
    }

    private void Find(bool previous)
    {
        string query = SearchBox.Text;
        TextMatch? match = previous
            ? TextSearchService.FindPrevious(NoteEditor.Text, query, NoteEditor.SelectionStart, MatchCaseCheckBox.IsChecked == true)
            : TextSearchService.FindNext(NoteEditor.Text, query, NoteEditor.SelectionStart, NoteEditor.SelectionLength, MatchCaseCheckBox.IsChecked == true);
        if (match is not TextMatch found)
        {
            SetStatus(query.Length == 0 ? "请输入搜索内容" : $"未找到“{query}”", isError: query.Length > 0);
            return;
        }

        NoteEditor.Focus();
        NoteEditor.Select(found.Index, found.Length);
        int line = NoteEditor.GetLineIndexFromCharacterIndex(found.Index);
        NoteEditor.ScrollToLine(Math.Max(0, line));
        SetStatus(found.Wrapped ? (previous ? "已从文档末尾继续查找" : "已从文档开头继续查找") : "已找到");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized && SearchBox.Text.Length > 0)
        {
            Find(previous: false);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Find(previous: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSearch();
            e.Handled = true;
        }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => Find(previous: true);
    private void FindNext_Click(object sender, RoutedEventArgs e) => Find(previous: false);
    private void CloseSearch_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void MatchCase_Changed(object sender, RoutedEventArgs e)
    {
        _settings.SearchMatchCase = MatchCaseCheckBox.IsChecked == true;
        if (_initialized && SearchPanel.Visibility == Visibility.Visible && SearchBox.Text.Length > 0)
        {
            Find(previous: false);
        }
    }

    private void HeaderPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!_editing || _modalDialogOpen)
        {
            return;
        }

        CaptureWindowState();
        _settings.LastCaretIndex = NoteEditor.CaretIndex;
        SettingsWindow dialog = new(_settings, _paths.RootDirectory) { Owner = this };
        _modalDialogOpen = true;
        bool accepted = dialog.ShowDialog() == true;
        _modalDialogOpen = false;
        if (!accepted || dialog.ResultSettings is not DeskNoteSettings updated)
        {
            NoteEditor.Focus();
            return;
        }

        string parseError = string.Empty;
        string registrationError = "全局快捷键服务尚未初始化。";
        if (!HotkeyGesture.TryParse(updated.Hotkey, out HotkeyGesture gesture, out parseError) ||
            _globalHotkey is null || !_globalHotkey.TryRegister(gesture, out registrationError))
        {
            string error = string.IsNullOrEmpty(parseError) ? registrationError : parseError;
            MessageBox.Show(error, "无法应用快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
            NoteEditor.Focus();
            return;
        }

        _settings = updated;
        ApplySettings();
        try
        {
            await _settingsStore.SaveAsync(_settings);
            SetStatus("设置已保存");
        }
        catch (Exception exception)
        {
            _logger.Error("保存设置失败", exception);
            SetStatus($"设置保存失败：{exception.Message}", isError: true);
        }

        NoteEditor.Focus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_transitioning)
        {
            return;
        }

        _transitioning = true;
        try
        {
            if (_editing)
            {
                NoteEditor.IsReadOnly = true;
                CaptureWindowState();
                _settings.LastCaretIndex = NoteEditor.CaretIndex;
            }

            await _autoSaver.FlushAsync(NoteEditor.Text);
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            _logger.Error("退出前保存失败", exception);
            MessageBox.Show($"退出前保存失败：{exception.Message}\n\n程序将保持打开，以免丢失内容。", "DeskNote", MessageBoxButton.OK, MessageBoxImage.Error);
            if (_editing)
            {
                NoteEditor.IsReadOnly = false;
                NoteEditor.Focus();
            }

            _transitioning = false;
            return;
        }

        _globalHotkey?.Dispose();
        _desktopHealthTimer.Stop();
        _desktopRecoveryTimer.Stop();
        _desktopHealthTimer.Tick -= DesktopHealthTimer_Tick;
        _desktopRecoveryTimer.Tick -= DesktopRecoveryTimer_Tick;
        if (_messageSource is not null)
        {
            _messageSource.RemoveHook(WindowMessageHook);
            _messageSource.Dispose();
        }

        _autoSaver.SaveFailed -= AutoSaver_SaveFailed;
        await _autoSaver.DisposeAsync();
        _allowClose = true;
        Close();
    }

    private void AutoSaver_SaveFailed(object? sender, Exception exception)
    {
        _logger.Error("自动保存失败", exception);
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(() => SetStatus($"自动保存失败：{exception.Message}", isError: true));
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : NoteEditor.Foreground;
    }

    private static Brush ParseBrush(string value, Brush fallback)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(value)!;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
