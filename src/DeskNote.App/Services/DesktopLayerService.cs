using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskNote.App.Interop;

namespace DeskNote.App.Services;

/// <summary>
/// Reparents the WPF HWND into Explorer's visible desktop-icon host. Making the note a
/// descendant of SHELLDLL_DefView is the invariant that matters: Explorer keeps that
/// subtree above the wallpaper and Win+D does not minimize its child windows. Normal
/// top-level application windows still cover the entire subtree.
/// </summary>
internal sealed class DesktopLayerService(nint windowHandle)
{
    private readonly long _topLevelStyle = NativeMethods.GetWindowStyle(windowHandle, NativeMethods.GwlStyle);
    private readonly long _topLevelExtendedStyle = NativeMethods.GetWindowStyle(windowHandle, NativeMethods.GwlExStyle);
    private nint _desktopHost;

    public bool IsEmbedded =>
        _desktopHost != nint.Zero &&
        NativeMethods.IsWindow(_desktopHost) &&
        NativeMethods.GetParent(windowHandle) == _desktopHost;

    /// <summary>
    /// Performs a low-cost desktop-layer invariant check. The visible desktop host can
    /// be replaced without restarting Explorer (for example after sleep, virtual desktop
    /// changes, or Shell refresh), and the note can also be reordered behind the icon list.
    /// </summary>
    public bool IsHealthy
    {
        get
        {
            if (!IsEmbedded ||
                !NativeMethods.IsWindowVisible(_desktopHost) ||
                !NativeMethods.IsWindowVisible(windowHandle) ||
                NativeMethods.IsWindowCloaked(windowHandle))
            {
                return false;
            }

            nint root = NativeMethods.GetAncestor(_desktopHost, NativeMethods.GaRoot);
            if (root == nint.Zero || !NativeMethods.IsWindowVisible(root))
            {
                return false;
            }

            // The current visible DefView is authoritative. Explorer can leave an old,
            // still-valid HWND behind while switching the live desktop subtree.
            if (FindVisibleDesktopHost() != _desktopHost)
            {
                return false;
            }

            nint iconList = NativeMethods.FindWindowEx(_desktopHost, nint.Zero, "SysListView32", null);
            return iconList == nint.Zero || !IsSiblingAbove(iconList, windowHandle);
        }
    }

    public bool TryEmbed(out string error)
    {
        if (!NativeMethods.GetWindowRect(windowHandle, out NativeMethods.Rect bounds))
        {
            error = "无法读取笔记窗口的位置。";
            return false;
        }

        nint host = FindVisibleDesktopHost();
        if (host == nint.Zero)
        {
            error = "未找到可见的 Windows 桌面层。请确认 Explorer 正在运行。";
            return false;
        }

        long childStyle = (_topLevelStyle & ~NativeMethods.WsPopup) | NativeMethods.WsChild;
        // Keep the child non-activating while it belongs to Explorer, but do not use
        // WS_EX_TRANSPARENT: the first click must reach WPF so it can detach and edit.
        long childExtendedStyle = (_topLevelExtendedStyle | NativeMethods.WsExToolWindow |
                                   NativeMethods.WsExNoActivate) &
                                  ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowStyle(windowHandle, NativeMethods.GwlStyle, childStyle);
        NativeMethods.SetWindowStyle(windowHandle, NativeMethods.GwlExStyle, childExtendedStyle);

        Marshal.SetLastPInvokeError(0);
        _ = NativeMethods.SetParent(windowHandle, host);
        int parentError = Marshal.GetLastWin32Error();
        if (parentError != 0 || NativeMethods.GetParent(windowHandle) != host)
        {
            RestoreTopLevelStyles();
            error = parentError == 0
                ? "Windows 未接受桌面层父窗口。"
                : $"无法进入桌面层：{new Win32Exception(parentError).Message}";
            return false;
        }

        NativeMethods.Point location = new() { X = bounds.Left, Y = bounds.Top };
        _ = NativeMethods.ScreenToClient(host, ref location);
        bool positioned = NativeMethods.SetWindowPos(
            windowHandle,
            NativeMethods.HwndTop,
            location.X,
            location.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow);
        if (!positioned)
        {
            error = "已进入桌面层，但恢复窗口位置失败。";
            return false;
        }

        if (!NativeMethods.IsWindowVisible(host) ||
            !NativeMethods.IsWindowVisible(windowHandle) ||
            NativeMethods.IsWindowCloaked(windowHandle))
        {
            error = "桌面宿主或笔记窗口当前不可见（可能被 DWM 隐藏）。";
            return false;
        }

        _desktopHost = host;
        error = string.Empty;
        return true;
    }

    public void DetachForEditing()
    {
        if (!NativeMethods.GetWindowRect(windowHandle, out NativeMethods.Rect bounds))
        {
            return;
        }

        _ = NativeMethods.SetParent(windowHandle, nint.Zero);
        RestoreTopLevelStyles();
        _ = NativeMethods.SetWindowPos(
            windowHandle,
            NativeMethods.HwndTop,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow);
        _ = NativeMethods.ShowWindow(windowHandle, NativeMethods.SwShow);
        _ = NativeMethods.SetForegroundWindow(windowHandle);
        _desktopHost = nint.Zero;
    }

    private void RestoreTopLevelStyles()
    {
        NativeMethods.SetWindowStyle(windowHandle, NativeMethods.GwlStyle, _topLevelStyle);
        long extendedStyle = (_topLevelExtendedStyle | NativeMethods.WsExToolWindow) &
                             ~(NativeMethods.WsExAppWindow | NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent);
        NativeMethods.SetWindowStyle(windowHandle, NativeMethods.GwlExStyle, extendedStyle);
    }

    private static nint FindVisibleDesktopHost()
    {
        nint desktopHost = nint.Zero;
        _ = NativeMethods.EnumWindows((topLevel, _) =>
        {
            nint shellView = NativeMethods.FindWindowEx(topLevel, nint.Zero, "SHELLDLL_DefView", null);
            if (shellView == nint.Zero ||
                !NativeMethods.IsWindowVisible(topLevel) ||
                !NativeMethods.IsWindowVisible(shellView))
            {
                return true;
            }

            // WorkerW layout differs across Windows 10/11 builds. SHELLDLL_DefView is
            // the stable visible desktop subtree; an arbitrary WorkerW can be a hidden
            // helper window or even sit behind the wallpaper.
            desktopHost = shellView;
            return false;
        }, nint.Zero);
        return desktopHost;
    }

    private static bool IsSiblingAbove(nint candidate, nint reference)
    {
        nint sibling = NativeMethods.GetWindow(reference, NativeMethods.GwHwndPrev);
        for (int index = 0; sibling != nint.Zero && index < 256; index++)
        {
            if (sibling == candidate)
            {
                return true;
            }

            sibling = NativeMethods.GetWindow(sibling, NativeMethods.GwHwndPrev);
        }

        return false;
    }
}
