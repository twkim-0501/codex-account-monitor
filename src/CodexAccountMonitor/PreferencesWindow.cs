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
        Title = "설정"; Width = 420; Height = Math.Min(680, SystemParameters.WorkArea.Height - 32); ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)new BrushConverter().ConvertFromString("#FAFAFC")!;
        Foreground = (Brush)new BrushConverter().ConvertFromString("#30323B")!;
        var stack = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = stack };
        stack.Children.Add(new TextBlock { Text = "위젯 설정", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) });
        stack.Children.Add(new TextBlock { Text = "갱신 간격 · 30~3600초", FontSize = 11, Margin = new Thickness(0, 0, 0, 5) });
        var interval = new TextBox { Text = settings.RefreshSeconds.ToString() }; stack.Children.Add(interval);
        var top = new CheckBox { Content = "항상 위에 표시", IsChecked = settings.AlwaysOnTop }; stack.Children.Add(top);
        var alerts = new CheckBox { Content = "잔여 한도 10% 이하 알림", IsChecked = settings.AlertsEnabled }; stack.Children.Add(alerts);
        var publicResets = new CheckBox { Content = "다음 특별 리셋 소식 확인 · 10분 간격", IsChecked = settings.WatchPublicResets }; stack.Children.Add(publicResets);
        var resetAlerts = new CheckBox { Content = "새 리셋 예고·계정 한도 회복·시각 변경 알림", IsChecked = settings.ResetAlertsEnabled }; stack.Children.Add(resetAlerts);
        var hints = new CheckBox { Content = "투표·답글의 미확정 힌트도 알림", IsChecked = settings.HintAlertsEnabled }; stack.Children.Add(hints);
        var expiry = new CheckBox { Content = "초기화권 만료 3일·24시간 전 알림", IsChecked = settings.CreditExpiryAlertsEnabled }; stack.Children.Add(expiry);
        stack.Children.Add(new TextBlock { Text = "공개 수집본에서 본문·답글 맥락을 읽고, X의 공개 카드에서 투표 선택지를 확인합니다. 수집 지연·누락이 있을 수 있습니다. 모니터링은 모델 작업을 실행하지 않습니다.", FontSize = 10, TextWrapping = TextWrapping.Wrap });
        var mini = new CheckBox { Content = "왼쪽 아래 미니 위젯 표시", IsChecked = settings.ShowMiniWidget }; stack.Children.Add(mini);
        var dock = new CheckBox { Content = "미니 위젯을 작업표시줄 안에 붙이기", IsChecked = settings.DockMiniWidget }; stack.Children.Add(dock);
        stack.Children.Add(new TextBlock { Text = "최대 3개를 한 줄로 표시합니다. 나머지는 +N에서 전체 목록을 엽니다. 도킹을 해제하면 작업표시줄 바로 위에 표시합니다.", FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)new BrushConverter().ConvertFromString("#90919B")! });
        var startup = new CheckBox { Content = "Windows 로그인 시 접힌 상태로 시작", IsChecked = settings.StartWithWindows }; stack.Children.Add(startup);
        var save = new Button { Content = "저장", Margin = new Thickness(0, 14, 0, 0) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(interval.Text, out var seconds) || seconds is < 30 or > 3600) { MessageBox.Show("30~3600 사이의 초를 입력하세요.", Title); return; }
            settings.RefreshSeconds = seconds; settings.AlwaysOnTop = top.IsChecked == true; settings.AlertsEnabled = alerts.IsChecked == true; settings.StartWithWindows = startup.IsChecked == true;
            settings.ShowMiniWidget = mini.IsChecked == true; settings.DockMiniWidget = dock.IsChecked == true;
            settings.WatchPublicResets = publicResets.IsChecked == true; settings.ResetAlertsEnabled = resetAlerts.IsChecked == true;
            settings.HintAlertsEnabled = hints.IsChecked == true; settings.CreditExpiryAlertsEnabled = expiry.IsChecked == true;
            DialogResult = true;
        };
        stack.Children.Add(save);
    }
}
