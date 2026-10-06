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

public sealed record MiniAccount(string Name, double? Remaining, bool Fresh, bool Blocked);

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
    public event Action? Click;
    public event Action? RightClick;
    public bool IsDocked { get; private set; }
    public string Status { get; private set; } = "미니 위젯 숨김";
    public Rectangle ScreenBounds => bounds;
    public nint Handle => handle;

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
        var width = (int)Math.Round(248 * scale);
        var height = dock ? Math.Clamp(trayRect.Bottom - trayRect.Top - 4, 32, (int)(56 * scale)) : (int)Math.Round(48 * scale);
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
            handle = CreateWindowEx(Layered | ToolWindow | NoActivate, ClassName, "Codex Account Monitor · Mini",
                (dock ? Child : Popup) | Visible, dock ? x - trayRect.Left : x, dock ? y - trayRect.Top : y, width, height,
                nextParent, 0, GetModuleHandle(null), 0);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Instances[handle] = this; parent = nextParent; paintPending = true;
        }
        if (bounds != next)
        {
            if (!SetWindowPos(handle, 0, dock ? x - trayRect.Left : x, dock ? y - trayRect.Top : y, width, height, 0x10 | 0x200))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            bounds = next; paintPending = true;
        }
        IsDocked = dock;
        Status = dock ? "미니 위젯 · 작업표시줄 안" : "미니 위젯 · 작업표시줄 바로 위";
        if (paintPending) { Paint(); paintPending = false; }
    }

    private static bool IsTaskbarLeftAligned()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return key?.GetValue("TaskbarAl") is int alignment && alignment == 0;
    }

    private static Bitmap Render(IReadOnlyList<MiniAccount> accounts, Rectangle bounds, double scale)
    {
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(255, 18, 25, 34));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var s = (float)scale;
        using var accent = new SolidBrush(Color.FromArgb(82, 220, 196));
        graphics.FillRectangle(accent, 3 * s, 5 * s, 2 * s, image.Height - 10 * s);
        using var nameFont = new Font("Malgun Gothic", 10 * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var valueFont = new Font("Segoe UI", 12 * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.FromArgb(237, 244, 250));
        using var muted = new SolidBrush(Color.FromArgb(147, 164, 184));
        using var warning = new SolidBrush(Color.FromArgb(245, 184, 109));
        using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        var rows = accounts.Take(2).ToArray();
        var rowHeight = (image.Height - 4 * s) / 2f;
        for (var i = 0; i < Math.Max(1, rows.Length); i++)
        {
            var account = i < rows.Length ? rows[i] : new MiniAccount("CODEX · + 연결", null, false, false);
            var name = account.Name + (i == 1 && accounts.Count > 2 ? $"  +{accounts.Count - 2}" : "");
            var value = account.Blocked ? "사용 제한" : account.Remaining is { } remaining ? $"{(account.Fresh ? "" : "이전 ")}{remaining:0}% 남음" : "—";
            var color = account.Blocked || !account.Fresh || account.Remaining <= 10 ? warning : accent;
            var y = 2 * s + i * rowHeight;
            graphics.DrawString(name, nameFont, text, new RectangleF(13 * s, y, image.Width - 125 * s, rowHeight), format);
            graphics.DrawString(value, valueFont, color, new RectangleF(image.Width - 118 * s, y, 100 * s, rowHeight), right);
        }
        if (rows.Length < 2)
            graphics.DrawString(rows.Length == 0 ? "클릭해서 계정 추가" : "잔여 한도 · 클릭해서 상세 보기", nameFont, muted,
                new RectangleF(13 * s, 2 * s + rowHeight, image.Width - 25 * s, rowHeight), format);
        return image;
    }

    private void Paint()
    {
        using var image = Render(accounts, bounds, scale);
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = image.GetHbitmap();
        var previous = SelectObject(memory, bitmap);
        try
        {
            var size = new NativeSize { Width = bounds.Width, Height = bounds.Height };
            var origin = new NativePoint();
            var blend = new Blend { Operation = 0, Alpha = 255, Format = 0 };
            // Use an opaque layered surface so Explorer's composition does not hide a WPF child.
            if (!UpdateLayeredWindow(handle, screen, 0, ref size, memory, ref origin, 0, ref blend, 4))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(0, screen); }
    }

    public object Inspect() => new
    {
        IsDocked, Status, exists = handle != 0 && IsWindow(handle), visible = handle != 0 && IsWindowVisible(handle),
        parentIsTaskbar = handle != 0 && GetParent(handle) == FindWindow("Shell_TrayWnd", null),
        childStyle = handle != 0 && (GetWindowLongPtr(handle, -16).ToInt64() & Child) != 0,
        topmost = handle != 0 && (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0,
        bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height }, scale
    };

    public bool HitTestCenter() => handle != 0 && WindowFromPoint(new NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 }) == handle;

    public static void SavePreview(string path, IReadOnlyList<MiniAccount> accounts)
    {
        using var image = Render(accounts, new Rectangle(0, 0, 248, 48), 1);
        image.Save(path, ImageFormat.Png);
    }

    public void SendTestClick(bool rightButton = false)
    {
        // Exercise only this process's own widget handler. This does not move or click the user's mouse.
        if (handle == 0 || !IsWindow(handle)) throw new InvalidOperationException("Mini widget is unavailable");
        SendMessage(handle, rightButton ? 0x205u : 0x202u, 0, 0);
    }

    public bool CaptureVisible(string path)
    {
        if (handle == 0 || !IsWindowVisible(handle)) return false;
        using var image = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.Save(path, ImageFormat.Png);
        var mint = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < Math.Min(image.Width, (int)(8 * scale)); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.G > 160 && pixel.B > 130 && pixel.R < 120) mint++;
            }
        return mint > 10;
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
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
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
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
