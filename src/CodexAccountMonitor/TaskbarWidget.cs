using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace CodexAccountMonitor;

public sealed record MiniAccount(string Name, double? Remaining, bool Fresh, bool Blocked, string? ShortName = null, bool Primary = false);

/// <summary>A small native child of Explorer. The WPF detail panel remains a separate window.</summary>
public sealed class TaskbarWidget : IDisposable
{
    private const string ClassName = "CodexAccountMonitor.MiniWidget";
    private const uint Child = 0x40000000, Popup = 0x80000000, Visible = 0x10000000;
    private const uint Layered = 0x80000, ToolWindow = 0x80, NoActivate = 0x8000000;
    private static readonly WindowProcedure Procedure = WindowProc;
    private static readonly Dictionary<nint, TaskbarWidget> Instances = [];
    private static bool registered;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IReadOnlyList<MiniAccount> accounts = [];
    private nint handle, parent;
    private Rectangle bounds;
    private double scale = 1;
    private bool enabled, requestedDock, disposed, paintPending = true;
    private bool lightTheme;
    public event Action? Click;
    public event Action? RightClick;
    public bool IsDocked { get; private set; }
    public string Status { get; private set; } = "미니 위젯 숨김";
    public Rectangle ScreenBounds => bounds;
    public nint Handle => handle;
    public int VisibleAccounts => Math.Min(3, accounts.Count);
    public int OverflowAccounts => Math.Max(0, accounts.Count - 3);

    public TaskbarWidget()
    {
        EnsureClass();
        timer.Tick += (_, _) => Maintain();
        timer.Start();
    }

    public void Configure(bool show, bool dock)
    {
        if (enabled != show || requestedDock != dock) DestroyOwnWindow();
        enabled = show; requestedDock = dock; paintPending = true;
        Maintain();
    }

    public void Update(IReadOnlyList<MiniAccount> values)
    {
        accounts = values; paintPending = true; Maintain();
    }

    private void Maintain()
    {
        if (disposed || !enabled) { Status = "미니 위젯 숨김"; return; }
        try { PlaceAndPaint(allowDock: true); }
        catch (Win32Exception)
        {
            DestroyOwnWindow();
            try { PlaceAndPaint(allowDock: false); }
            catch (Win32Exception) { DestroyOwnWindow(); Status = "미니 위젯 표시 실패 · 알림 영역에서 열기"; }
        }
    }

    private void PlaceAndPaint(bool allowDock)
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var trayRect = new NativeRect();
        var taskbarFound = taskbar != 0 && GetWindowRect(taskbar, out trayRect);
        scale = taskbar != 0 ? Math.Max(96, GetDpiForWindow(taskbar)) / 96d : 1;
        // A left-aligned taskbar has Start/search buttons in this space. Keep them accessible.
        var leftAligned = Environment.OSVersion.Version.Build < 22000 || IsTaskbarLeftAligned();
        var dock = allowDock && requestedDock && taskbarFound && trayRect.Right - trayRect.Left > trayRect.Bottom - trayRect.Top && !leftAligned;
        var width = (int)Math.Round(MiniWidgetRenderer.Width(accounts.Count) * scale);
        var height = dock ? Math.Clamp(trayRect.Bottom - trayRect.Top - 4, 30, (int)(44 * scale)) : (int)Math.Round(36 * scale);
        using var personalization = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var nextLightTheme = personalization?.GetValue("SystemUsesLightTheme") is int theme && theme == 1;
        if (nextLightTheme != lightTheme) { lightTheme = nextLightTheme; paintPending = true; }
        int x, y;
        if (dock)
        {
            x = trayRect.Left + (int)Math.Round(8 * scale);
            y = trayRect.Top + Math.Max(0, (trayRect.Bottom - trayRect.Top - height) / 2);
        }
        else
        {
            if (!SystemParametersInfo(0x30, 0, out var work, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            x = work.Left + (int)Math.Round(8 * scale);
            y = work.Bottom - height - (int)Math.Round(6 * scale);
        }
        var next = new Rectangle(x, y, width, height);
        var nextParent = dock ? taskbar : 0;
        if (handle != 0 && (!IsWindow(handle) || parent != nextParent)) DestroyOwnWindow();
        if (handle == 0)
        {
            handle = CreateWindowEx(Layered | ToolWindow | NoActivate | (dock ? 0u : 8u), ClassName, "Codex Account Monitor · Mini",
                (dock ? Child : Popup) | Visible, dock ? x - trayRect.Left : x, dock ? y - trayRect.Top : y, width, height,
                nextParent, 0, GetModuleHandle(null), 0);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Instances[handle] = this; parent = nextParent; paintPending = true;
        }
        if (bounds != next)
        {
            if (!SetWindowPos(handle, dock ? 0 : -1, dock ? x - trayRect.Left : x, dock ? y - trayRect.Top : y, width, height, 0x10 | 0x200))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            bounds = next; paintPending = true;
        }
        IsDocked = dock;
        Status = dock ? "미니 위젯 · 작업표시줄 안" : "미니 위젯 · 작업표시줄 바로 위";
        if (paintPending) { Paint(); paintPending = false; }
        if (!dock) ShowWindow(handle, OtherWindowCoversMonitor(taskbar) ? 0 : 4);
    }

    private static bool OtherWindowCoversMonitor(nint taskbar)
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == GetShellWindow()) return false;
        GetWindowThreadProcessId(foreground, out var process);
        GetWindowThreadProcessId(taskbar, out var explorer);
        if (process == Environment.ProcessId || process == explorer) return false;
        var monitor = new MonitorInformation { Size = (uint)Marshal.SizeOf<MonitorInformation>() };
        return GetMonitorInfo(MonitorFromWindow(taskbar, 1), ref monitor) && GetWindowRect(foreground, out var window)
            && window.Left <= monitor.Monitor.Left && window.Top <= monitor.Monitor.Top
            && window.Right >= monitor.Monitor.Right && window.Bottom >= monitor.Monitor.Bottom;
    }

    private static bool IsTaskbarLeftAligned()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return key?.GetValue("TaskbarAl") is int alignment && alignment == 0;
    }

    private void Paint()
    {
        using var image = MiniWidgetRenderer.Render(accounts, bounds.Size, scale, lightTheme);
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var information = new BitmapInformation { Size = 40, Width = image.Width, Height = -image.Height, Planes = 1, Bits = 32 };
        var bitmap = CreateDIBSection(memory, ref information, 0, out var pixels, 0, 0);
        if (bitmap == 0) { DeleteDC(memory); ReleaseDC(0, screen); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        var previous = SelectObject(memory, bitmap);
        try
        {
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[image.Width * 4];
                for (var y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    Marshal.Copy(row, 0, pixels + y * row.Length, row.Length);
                }
            }
            finally { image.UnlockBits(data); }
            var size = new NativeSize { Width = bounds.Width, Height = bounds.Height };
            var origin = new NativePoint();
            var blend = new Blend { Operation = 0, Alpha = 255, Format = 1 };
            // Copy premultiplied BGRA directly. Per-pixel alpha leaves Explorer's background visible.
            if (!UpdateLayeredWindow(handle, screen, 0, ref size, memory, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(0, screen); }
    }

    public object Inspect() => new
    {
        IsDocked, Status, VisibleAccounts, OverflowAccounts, lightTheme, exists = handle != 0 && IsWindow(handle), visible = handle != 0 && IsWindowVisible(handle),
        parentIsTaskbar = handle != 0 && GetParent(handle) == FindWindow("Shell_TrayWnd", null),
        childStyle = handle != 0 && (GetWindowLongPtr(handle, -16).ToInt64() & Child) != 0,
        topmost = handle != 0 && (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0,
        bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height }, scale
    };

    public bool HitTestCenter() => handle != 0 && WindowFromPoint(new NativePoint { X = bounds.Left + (int)(40 * scale), Y = bounds.Top + bounds.Height / 2 }) == handle;
    public bool HitTestOverflow() => OverflowAccounts > 0 && WindowFromPoint(new NativePoint { X = bounds.Left + (int)(320 * scale), Y = bounds.Top + bounds.Height / 2 }) == handle;

    public static void SavePreview(string path, IReadOnlyList<MiniAccount> accounts, bool lightTheme = true)
    {
        var size = new Size(MiniWidgetRenderer.Width(accounts.Count), 44);
        using var image = MiniWidgetRenderer.Render(accounts, size, 1, lightTheme);
        using var preview = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(preview)) { graphics.Clear(lightTheme ? Color.FromArgb(241, 242, 246) : Color.FromArgb(30, 31, 34)); graphics.DrawImageUnscaled(image, Point.Empty); }
        preview.Save(path, ImageFormat.Png);
    }

    public void SendTestClick(bool rightButton = false, bool overflowBadge = false)
    {
        // Exercise only this process's own widget handler. This does not move or click the user's mouse.
        if (handle == 0 || !IsWindow(handle)) throw new InvalidOperationException("Mini widget is unavailable");
        var x = (int)((overflowBadge ? 320 : 40) * scale);
        var coordinates = (x & 0xffff) | (bounds.Height / 2 << 16);
        SendMessage(handle, rightButton ? 0x205u : 0x202u, 0, coordinates);
    }

    public bool CaptureVisible(string path)
    {
        if (handle == 0 || !IsWindowVisible(handle)) return false;
        using var image = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.Save(path, ImageFormat.Png);
        using var expected = MiniWidgetRenderer.Render(accounts, bounds.Size, scale, lightTheme);
        var matches = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                var target = expected.GetPixel(x, y);
                if (target.A >= 235 && Math.Abs(pixel.R - target.R) < 25 && Math.Abs(pixel.G - target.G) < 25 && Math.Abs(pixel.B - target.B) < 25) matches++;
            }
        return matches > 12;
    }

    private void DestroyOwnWindow()
    {
        var old = handle;
        handle = 0; parent = 0; bounds = Rectangle.Empty; IsDocked = false;
        if (old != 0) { Instances.Remove(old); if (IsWindow(old)) DestroyWindow(old); }
    }

    public void Dispose() { disposed = true; timer.Stop(); DestroyOwnWindow(); }

    private static void EnsureClass()
    {
        if (registered) return;
        var windowClass = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Procedure, Instance = GetModuleHandle(null), Name = ClassName, Cursor = LoadCursor(0, 32649) };
        if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        registered = true;
    }

    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (Instances.TryGetValue(window, out var instance))
        {
            switch (message)
            {
                case 0x202: instance.dispatcher.BeginInvoke(() => instance.Click?.Invoke()); return 0;
                case 0x205: instance.dispatcher.BeginInvoke(() => instance.RightClick?.Invoke()); return 0;
                case 0x21: return 3; // MA_NOACTIVATE: expanding details activates the WPF window explicitly.
                case 0x14: return 1;
                case 0x82: Instances.Remove(window); instance.handle = 0; break;
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Size, Style;
        public WindowProcedure Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? Menu;
        public string Name;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInformation { public uint Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInformation
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? name);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint value, out NativeRect rect, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern nint LoadCursor(nint instance, nint cursor);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint window, nint destDC, nint destination, ref NativeSize size, nint sourceDC, ref NativePoint source, uint colorKey, ref Blend blend, uint flags);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation information);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInformation information, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
