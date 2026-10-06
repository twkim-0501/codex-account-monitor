using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public sealed class PreferencesWindow : Window
{
    public PreferencesWindow(MonitorSettings settings)
    {
        Title = "설정"; Width = 400; Height = 470; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)new BrushConverter().ConvertFromString("#0C1016")!;
        Foreground = (Brush)new BrushConverter().ConvertFromString("#EDF4FA")!;
        var stack = new StackPanel { Margin = new Thickness(24) }; Content = stack;
        stack.Children.Add(new TextBlock { Text = "위젯 설정", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) });
        stack.Children.Add(new TextBlock { Text = "갱신 간격 · 30~3600초", FontSize = 11, Margin = new Thickness(0, 0, 0, 5) });
        var interval = new TextBox { Text = settings.RefreshSeconds.ToString() }; stack.Children.Add(interval);
        var top = new CheckBox { Content = "항상 위에 표시", IsChecked = settings.AlwaysOnTop }; stack.Children.Add(top);
        var alerts = new CheckBox { Content = "잔여 한도 10% 이하 알림", IsChecked = settings.AlertsEnabled }; stack.Children.Add(alerts);
        var mini = new CheckBox { Content = "왼쪽 아래 미니 위젯 표시", IsChecked = settings.ShowMiniWidget }; stack.Children.Add(mini);
        var dock = new CheckBox { Content = "미니 위젯을 작업표시줄 안에 붙이기", IsChecked = settings.DockMiniWidget }; stack.Children.Add(dock);
        stack.Children.Add(new TextBlock { Text = "해제하면 작업표시줄 바로 위에 표시합니다. Windows에서 도킹할 수 없는 경우에도 바로 위에 표시합니다.", FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)new BrushConverter().ConvertFromString("#93A4B8")! });
        var startup = new CheckBox { Content = "Windows 로그인 시 접힌 상태로 시작", IsChecked = settings.StartWithWindows }; stack.Children.Add(startup);
        var save = new Button { Content = "저장", Margin = new Thickness(0, 14, 0, 0) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(interval.Text, out var seconds) || seconds is < 30 or > 3600) { MessageBox.Show("30~3600 사이의 초를 입력하세요.", Title); return; }
            settings.RefreshSeconds = seconds; settings.AlwaysOnTop = top.IsChecked == true; settings.AlertsEnabled = alerts.IsChecked == true; settings.StartWithWindows = startup.IsChecked == true;
            settings.ShowMiniWidget = mini.IsChecked == true; settings.DockMiniWidget = dock.IsChecked == true; DialogResult = true;
        };
        stack.Children.Add(save);
    }
}
