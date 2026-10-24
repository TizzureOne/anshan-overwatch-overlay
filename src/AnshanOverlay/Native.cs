using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AnshanOverlay;

public sealed record DisplayInfo(string Name, int X, int Y, int Width, int Height, bool Primary)
{
    public override string ToString() => $"{(Primary ? "主屏" : "副屏")} · {Width} × {Height} · {Name.Replace("\\\\.\\", "")}";
}

public static class Native
{
    public const int ExTransparent = 0x20, ExNoActivate = 0x08000000, ExToolWindow = 0x80;
    [StructLayout(LayoutKind.Sequential)] public struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr hwnd, string name);
    [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr hwnd, string name, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "RemovePropW", CharSet = CharSet.Unicode)] public static extern IntPtr RemoveProp(IntPtr hwnd, string name);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void DarkTitle(Window window)
    {
        int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, 4);
    }
    public static List<DisplayInfo> Displays()
    {
        var list = new List<DisplayInfo>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var r = screen.Bounds; list.Add(new(screen.DeviceName, r.X, r.Y, r.Width, r.Height, screen.Primary));
        }
        return list;
    }
    public static void SetPassThrough(IntPtr hwnd, bool enabled)
    {
        int style = GetWindowLong(hwnd, -20);
        style = enabled ? style | ExTransparent | ExNoActivate : style & ~(ExTransparent | ExNoActivate);
        SetWindowLong(hwnd, -20, style | ExToolWindow);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20);
    }
}
