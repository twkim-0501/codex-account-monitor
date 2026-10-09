using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountMonitor;

public sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = "사용 방법"; Width = 450; Height = Math.Min(520, SystemParameters.WorkArea.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)new BrushConverter().ConvertFromString("#FAFAFC")!;
        FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        var stack = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = stack };
        stack.Children.Add(new TextBlock { Text = "처음 사용하시나요?", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        Step(stack, "1. 이 PC의 계정", "현재 Codex 로그인 계정을 자동으로 조회합니다. 로그인 필요 메시지가 나오면 Codex 앱이나 CLI에서 먼저 로그인하세요.");
        Step(stack, "2. 서버 또는 다른 계정 추가", "+ 계정에서 연결 방식을 선택하고 ‘연결 확인’으로 실제 계정을 확인한 뒤 ‘모니터에 추가’를 누르세요. VSCode나 Codex 앱의 SSH 연결은 이 모니터에 자동 등록되지 않습니다.");
        Step(stack, "3. Primary와 주간 배분", "현재 데스크톱 로그인 계정은 맨 앞에 놓고 파란 배경과 PRIMARY 배지로 구분합니다. 로그인 계정을 바꾸면 표시도 바뀝니다. 계정 옆의 큰 %는 가장 적게 남은 한도입니다. 아래에는 주간 잔여 한도, 정기 리셋까지 남은 기간, 하루 배분량을 표시합니다. 40%가 남고 리셋까지 4일이면 하루에 전체 주간 한도의 10%씩 쓰는 기준입니다. 계정을 누르면 한도 사용 비율과 지난 기간을 비교합니다.");
        Step(stack, "4. 접기와 종료", "− 또는 창 닫기는 상세 창만 접습니다. 앱을 종료하려면 미니 위젯이나 오른쪽 알림 영역 아이콘을 우클릭하고 ‘종료’를 선택하세요.");
        Step(stack, "5. 다음 특별 리셋", "카드 위쪽에 리셋 예상 시각을 크게 표시합니다. 직접 예고와 앱의 추정은 상태로 구분합니다. 두 게이지는 CodexReset의 예상 확률이며, 근거는 링크로 확인합니다. 리셋이 완료되면 그 전에 나온 예고와 확률은 만료시킵니다. 새 시각을 알 수 없으면 ‘시각 미정’, 확률은 ‘—’로 표시합니다.");
        Step(stack, "6. 초기화권 지급", "지급 예정 시각을 크게 표시합니다. 내 계정에 들어온 것이 확인되면 예정 시각 대신 확인 시각, 새 지급량, 계정을 표시합니다. 보유 개수와 만료일은 계정 상세에서 확인합니다. 공지는 카드의 링크로 열 수 있습니다.");
        var readme = new Button { Content = "설치·SSH 설정·문제 해결 안내 열기", Background = (Brush)new BrushConverter().ConvertFromString("#EEEEF4")!, Margin = new Thickness(0, 14, 0, 8) };
        readme.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/twkim-0501/codex-account-monitor#readme") { UseShellExecute = true });
        stack.Children.Add(readme);
        var close = new Button { Content = "닫기", IsCancel = true }; close.Click += (_, _) => Close(); stack.Children.Add(close);
    }
    private static void Step(StackPanel stack, string title, string body)
    {
        stack.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 5) });
        stack.Children.Add(new TextBlock { Text = body, FontSize = 11, Foreground = (Brush)new BrushConverter().ConvertFromString("#777985")!, TextWrapping = TextWrapping.Wrap });
    }
}
