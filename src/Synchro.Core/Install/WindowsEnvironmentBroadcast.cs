using System.Runtime.InteropServices;

namespace Synchro.Core.Install;

internal static class WindowsEnvironmentBroadcast
{
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;
    private const int TimeoutMilliseconds = 2000;

    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int message, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    internal static void NotifyEnvironmentChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, TimeoutMilliseconds, out _);
    }
}
