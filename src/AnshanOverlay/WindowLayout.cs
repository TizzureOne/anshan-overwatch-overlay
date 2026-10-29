using System;
using System.Windows;
using System.Windows.Interop;

namespace AnshanOverlay;

public static class WindowLayout
{
    public static WindowPosition? Capture(Window window)
    {
        var bounds = window.RestoreBounds;
        bool useCurrent = window.WindowState == WindowState.Normal || bounds.IsEmpty;
        double left = useCurrent ? window.Left : bounds.Left, top = useCurrent ? window.Top : bounds.Top;
        return double.IsFinite(left) && double.IsFinite(top) ? new(left, top) : null;
    }
    public static void Restore(Window window, WindowPosition? position)
    {
        if (position is not null && double.IsFinite(position.Left) && double.IsFinite(position.Top))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = position.Left; window.Top = position.Top;
        }
        window.Loaded += (_, _) => KeepVisible(window);
    }
    public static Point FitPosition(Rect bounds, Rect workArea) => new(
        Math.Clamp(bounds.Left, workArea.Left, workArea.Left + Math.Max(0, workArea.Width - bounds.Width)),
        Math.Clamp(bounds.Top, workArea.Top, workArea.Top + Math.Max(0, workArea.Height - bounds.Height)));

    public static void KeepVisible(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!Native.GetWindowRect(handle, out var frame)) return;
        // Keep Win32 screen coordinates in physical pixels when choosing a monitor.
        var area = System.Windows.Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom)).WorkingArea;
        var position = FitPosition(new Rect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top), new Rect(area.X, area.Y, area.Width, area.Height));
        if (position.X != frame.Left || position.Y != frame.Top)
            Native.SetWindowPos(handle, IntPtr.Zero, (int)position.X, (int)position.Y, 0, 0, 0x1 | 0x4 | 0x10);
    }
}
