using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskNote.DesktopProbe;

internal static class Program
{
    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static readonly List<nint> TopLevelWindows = [];

    private static int Main(string[] args)
    {
        uint targetProcessId = args.Length > 0 && uint.TryParse(args[0], out uint parsed) ? parsed : 0;
        _ = EnumWindows((window, _) =>
        {
            TopLevelWindows.Add(window);
            return true;
        }, nint.Zero);

        Console.WriteLine("Top-level desktop-related windows (front to back):");
        for (int index = 0; index < TopLevelWindows.Count; index++)
        {
            nint window = TopLevelWindows[index];
            string className = GetClassName(window);
            if (className is "Progman" or "WorkerW" || FindWindowEx(window, nint.Zero, "SHELLDLL_DefView", null) != nint.Zero)
            {
                PrintWindow(window, $"z={index}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Desktop host descendants and target process windows:");
        foreach (nint topLevel in TopLevelWindows)
        {
            string className = GetClassName(topLevel);
            if (className is not ("Progman" or "WorkerW"))
            {
                continue;
            }

            _ = EnumChildWindows(topLevel, (child, parameter) =>
            {
                _ = GetWindowThreadProcessId(child, out uint processId);
                string childClass = GetClassName(child);
                if (GetParent(child) == topLevel || processId == targetProcessId || childClass is "SHELLDLL_DefView" or "SysListView32")
                {
                    PrintWindow(child, $"under=0x{topLevel:X}");
                }

                return true;
            }, nint.Zero);
        }

        if (targetProcessId != 0 &&
            (args.Contains("--show-desktop-check", StringComparer.OrdinalIgnoreCase) ||
             args.Contains("--toggle-desktop", StringComparer.OrdinalIgnoreCase)))
        {
            return VerifyDesktopToggle(targetProcessId);
        }

        return 0;
    }

    private static int VerifyDesktopToggle(uint targetProcessId)
    {
        nint noteWindow = FindProcessWindow(targetProcessId);
        if (noteWindow == nint.Zero)
        {
            Console.Error.WriteLine($"FAIL: no HWND found for process {targetProcessId}.");
            return 2;
        }

        nint ordinaryForeground = GetForegroundWindow();
        string ordinaryForegroundClass = GetClassName(ordinaryForeground);
        if (ordinaryForeground == nint.Zero ||
            ordinaryForegroundClass is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd")
        {
            Console.Error.WriteLine("FAIL: an ordinary foreground application is required before the desktop test.");
            return 4;
        }

        object? shell = null;
        bool restoredInitialState = false;
        try
        {
            Type shellType = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException("Shell.Application COM type is unavailable.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Shell.Application COM object could not be created.");

            InvokeShellMethod(shellType, shell, "MinimizeAll");
            Thread.Sleep(900);
            bool ordinaryWindowMinimized = IsIconic(ordinaryForeground);
            bool stateValid = PrintAndValidateTargetState(noteWindow, "after Minimize All");

            InvokeShellMethod(shellType, shell, "UndoMinimizeALL");
            Thread.Sleep(700);

            restoredInitialState = true;
            if (!ordinaryWindowMinimized)
            {
                Console.Error.WriteLine($"FAIL: ordinary foreground window {ordinaryForegroundClass} was not minimized.");
            }

            Console.WriteLine(stateValid && ordinaryWindowMinimized
                ? "PASS: desktop-layer invariants survived Minimize All."
                : "FAIL");
            return stateValid && ordinaryWindowMinimized ? 0 : 3;
        }
        finally
        {
            if (!restoredInitialState && shell is not null)
            {
                // Best-effort recovery prevents a failed probe from leaving the user's
                // applications minimized.
                try
                {
                    InvokeShellMethod(shell.GetType(), shell, "UndoMinimizeALL");
                }
                catch (Exception)
                {
                }
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static bool PrintAndValidateTargetState(nint noteWindow, string label)
    {
        nint parent = GetParent(noteWindow);
        nint root = GetAncestor(noteWindow, 2);
        nint siblingBelow = GetWindow(noteWindow, 2);
        string parentClass = GetClassName(parent);
        string rootClass = GetClassName(root);
        string siblingBelowClass = GetClassName(siblingBelow);
        long extendedStyle = GetWindowStyle(noteWindow, -20);
        bool visible = IsWindowVisible(noteWindow);
        bool parentVisible = IsWindowVisible(parent);
        bool topmost = (extendedStyle & 0x00000008L) != 0;
        bool cloaked = IsCloaked(noteWindow);
        bool rootTopmost = (GetWindowStyle(root, -20) & 0x00000008L) != 0;
        bool valid = parentClass == "SHELLDLL_DefView" &&
                     rootClass is "Progman" or "WorkerW" &&
                     siblingBelowClass == "SysListView32" &&
                     visible && parentVisible && !topmost && !rootTopmost && !cloaked;

        Console.WriteLine();
        Console.WriteLine(
            $"{label}: hwnd=0x{noteWindow:X}, parent={parentClass}(0x{parent:X}), " +
            $"root={rootClass}, siblingBelow={siblingBelowClass}, visible={visible}, " +
            $"parentVisible={parentVisible}, topmost={topmost}, rootTopmost={rootTopmost}, cloaked={cloaked}");
        return valid;
    }

    private static nint FindProcessWindow(uint processId)
    {
        nint result = nint.Zero;
        foreach (nint topLevel in TopLevelWindows)
        {
            _ = GetWindowThreadProcessId(topLevel, out uint topLevelProcessId);
            if (topLevelProcessId == processId && IsVisibleContentWindow(topLevel))
            {
                return topLevel;
            }

            _ = EnumChildWindows(topLevel, (child, parameter) =>
            {
                _ = GetWindowThreadProcessId(child, out uint childProcessId);
                if (childProcessId != processId || !IsVisibleContentWindow(child))
                {
                    return true;
                }

                result = child;
                return false;
            }, nint.Zero);
            if (result != nint.Zero)
            {
                return result;
            }
        }

        return result;
    }

    private static bool IsVisibleContentWindow(nint window)
    {
        _ = GetWindowRect(window, out Rect bounds);
        return IsWindowVisible(window) &&
               bounds.Right > bounds.Left &&
               bounds.Bottom > bounds.Top &&
               GetClassName(window).StartsWith("HwndWrapper[DeskNote;", StringComparison.Ordinal);
    }

    private static bool IsCloaked(nint window)
    {
        int cloaked = 0;
        int result = DwmGetWindowAttribute(window, 14, ref cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    private static void InvokeShellMethod(Type shellType, object shell, string method)
    {
        _ = shellType.InvokeMember(
            method,
            BindingFlags.InvokeMethod,
            binder: null,
            target: shell,
            args: null);
        Thread.Sleep(500);
    }

    private static void PrintWindow(nint window, string context)
    {
        _ = GetWindowThreadProcessId(window, out uint processId);
        _ = GetWindowRect(window, out Rect bounds);
        string processName;
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            processName = "?";
        }

        Console.WriteLine(
            $"{context,-18} hwnd=0x{window:X} parent=0x{GetParent(window):X} " +
            $"class={GetClassName(window),-20} visible={IsWindowVisible(window),-5} " +
            $"rect=({bounds.Left},{bounds.Top})-({bounds.Right},{bounds.Bottom}) " +
            $"pid={processId}:{processName} style=0x{GetWindowStyle(window, -16):X} ex=0x{GetWindowStyle(window, -20):X} " +
            $"first=0x{GetWindow(window, 0):X} last=0x{GetWindow(window, 1):X} next=0x{GetWindow(window, 2):X} prev=0x{GetWindow(window, 3):X}");
    }

    private static string GetClassName(nint window)
    {
        StringBuilder name = new(256);
        _ = GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static long GetWindowStyle(nint window, int index) =>
        nint.Size == 8 ? GetWindowLongPtr64(window, index).ToInt64() : GetWindowLong32(window, index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, ref int value, int size);
}
