using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace CodexAccountMonitor;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("작업을 완료하지 못했습니다. 연결 설정을 확인한 뒤 다시 시도하세요.", "Codex Account Monitor");
            args.Handled = true;
        };
        var demo = e.Args.Contains("--demo");
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        var screenshot = screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[screenshotIndex + 1]) : null;
        var liveScreenshotIndex = Array.IndexOf(e.Args, "--live-screenshot");
        if (liveScreenshotIndex >= 0 && liveScreenshotIndex + 1 < e.Args.Length) screenshot = Path.GetFullPath(e.Args[liveScreenshotIndex + 1]);
        if (screenshot is not null) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var settingsIndex = Array.IndexOf(e.Args, "--settings");
        var customSettings = settingsIndex >= 0 && settingsIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[settingsIndex + 1]) : null;
        var checkIndex = Array.IndexOf(e.Args, "--widget-check");
        var checkDirectory = checkIndex >= 0 && checkIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[checkIndex + 1]) : null;
        if (screenshot is null && !(demo && checkDirectory is not null))
        {
            instance = new Mutex(true, "Local\\CodexAccountMonitor", out var created);
            if (!created) { MessageBox.Show("이미 실행 중입니다. 알림 영역의 아이콘을 클릭하세요.", "Codex Account Monitor"); Shutdown(); return; }
        }
        var window = new MainWindow(demo, screenshot, customSettings);
        MainWindow = window;
        if (screenshot is not null) window.Show();
        else if (checkDirectory is not null) RunWidgetCheck(window, checkDirectory);
        else if (!e.Args.Contains("--minimized") && (!window.StartsCollapsed || e.Args.Contains("--open"))) window.ShowDetails();
    }
    private async void RunWidgetCheck(MainWindow window, string directory)
    {
        try { await window.ExitAsync(await window.RunWidgetCheckAsync(directory) ? 0 : 1); }
        catch (Exception error)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "widget-error.txt"), error.ToString());
            await window.ExitAsync(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
