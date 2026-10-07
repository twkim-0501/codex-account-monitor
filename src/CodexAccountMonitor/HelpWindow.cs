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
        Step(stack, "3. 숫자와 상세 정보", "표시된 %는 남은 사용 한도입니다. 계정을 누르면 토큰 사용량과 초기화 시각을 펼칩니다. 앞의 3개를 표시하고 추가 계정은 +N으로 전체 목록을 엽니다.");
        Step(stack, "4. 접기와 종료", "− 또는 창 닫기는 상세 창만 접습니다. 앱을 종료하려면 미니 위젯이나 오른쪽 알림 영역 아이콘을 우클릭하고 ‘종료’를 선택하세요.");
        Step(stack, "5. 리셋 예측과 초기화권", "계정의 정기 리셋 시각과 초기화권 개수·만료일은 Codex 서버 조회값입니다. ‘다음 특별 리셋’의 두 게이지는 CodexReset의 24시간·48시간 커뮤니티 추정 확률입니다. 근거 보기를 누르면 짧은 설명과 원문 링크가 나옵니다. 상태 표시는 Tibo의 투표·답글·약속을 앱이 판단한 결과입니다. 소식은 10분마다 확인하며, 오래된 확률은 ‘—’로 표시합니다.");
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
