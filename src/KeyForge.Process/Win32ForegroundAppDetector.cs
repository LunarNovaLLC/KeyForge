using System.Runtime.InteropServices;
using System.Text;
using KeyForge.Core.Models;

namespace KeyForge.Process;

public sealed class Win32ForegroundAppDetector : IForegroundAppDetector
{
    public ActiveWindowInfo GetActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return ActiveWindowInfo.Empty;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        return processId == 0 ? ActiveWindowInfo.Empty : CreateWindowInfo(hwnd, processId);
    }

    public IReadOnlyList<ActiveWindowInfo> GetOpenWindows()
    {
        var windows = new List<ActiveWindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0 || processId == Environment.ProcessId)
            {
                return true;
            }

            var info = CreateWindowInfo(hwnd, processId, title);
            if (!string.IsNullOrWhiteSpace(info.ExecutableName))
            {
                windows.Add(info);
            }

            return true;
        }, 0);

        return windows
            .OrderBy(window => window.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ActiveWindowInfo CreateWindowInfo(nint hwnd, uint processId, string? title = null)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            var executablePath = TryGetExecutablePath(process);
            var executableName = !string.IsNullOrWhiteSpace(executablePath)
                ? Path.GetFileName(executablePath)
                : $"{process.ProcessName}.exe";

            return new ActiveWindowInfo
            {
                ProcessId = (int)processId,
                ProcessName = process.ProcessName,
                ExecutableName = executableName,
                ExecutablePath = executablePath,
                WindowTitle = title ?? GetWindowTitle(hwnd),
                IsElevated = TryIsProcessElevated(process)
            };
        }
        catch
        {
            return new ActiveWindowInfo
            {
                ProcessId = (int)processId,
                WindowTitle = title ?? GetWindowTitle(hwnd)
            };
        }
    }

    private static string? TryGetExecutablePath(System.Diagnostics.Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool? TryIsProcessElevated(System.Diagnostics.Process process)
    {
        try
        {
            if (!OpenProcessToken(process.Handle, TokenQuery, out var tokenHandle))
            {
                return null;
            }

            try
            {
                var elevation = new TokenElevation();
                var size = Marshal.SizeOf<TokenElevation>();
                return GetTokenInformation(
                    tokenHandle,
                    TokenInformationClass.TokenElevation,
                    ref elevation,
                    size,
                    out _) && elevation.TokenIsElevated != 0;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string GetWindowTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private const uint TokenQuery = 0x0008;

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        TokenInformationClass tokenInformationClass,
        ref TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
