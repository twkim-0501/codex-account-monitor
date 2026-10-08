using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private const string CreditPurple = "#6951A1";
    internal CreditGrantNews? RenderedCreditNews { get; private set; }
    internal Border? CreditNewsCard { get; private set; }
    internal Border? ForecastCard { get; private set; }

    private Border BuildCreditGrantCard(CreditGrantNews news)
    {
        var confirmed = news.State == CreditGrantState.AccountConfirmed;
        var tone = confirmed ? ForecastGreen : CreditPurple;
        var content = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text("초기화권 지급", 12, Ink, FontWeights.SemiBold));
        var badge = Text(confirmed ? "지급 확인" : news.Badge, 9, tone, FontWeights.SemiBold);
        Grid.SetColumn(badge, 1); top.Children.Add(badge); content.Children.Add(top);

        var time = news.State == CreditGrantState.AwaitingConfirmation ? "완료 확인 중"
            : news.Timing.Replace("발급 확인 · ", "").Replace("완료 공지 · ", "").Replace("까지 예상", "까지");
        content.Children.Add(Text(time, 21, tone, FontWeights.SemiBold, new Thickness(0, 10, 0, 0)));
        var names = news.Accounts.Where(a => a.State == CreditGrantAccountState.Confirmed).Select(a => a.Name).ToArray();
        var status = confirmed ? (news.CreditsPerAccount is { } count ? $"+{count}장 · " : "")
                + string.Join(" · ", names.Take(2)) + (names.Length > 2 ? $" 외 {names.Length - 2}개 계정" : "")
            : news.State == CreditGrantState.Completed ? "지급 완료 공지 · 내 계정은 별도 확인"
            : news.State == CreditGrantState.AwaitingConfirmation ? "예정 " + news.Timing.Replace("까지 예상", "까지")
            : news.State == CreditGrantState.Hint ? "지급 예고 미확정" : "지급 예상 시각";
        content.Children.Add(Text(status, 10, Soft, margin: new Thickness(0, 4, 0, 0)));

        var links = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        if (news.SourceUrl is { } url) links.Children.Add(EvidenceLink("Tibo 공지 ↗", url));
        var signal = resetState.Outlook.Signals.FirstOrDefault(s => s.Id == news.SourceId);
        var parent = signal?.Context?.FirstOrDefault(c => c.Author.TrimStart('@').Equals("thsottiaux", StringComparison.OrdinalIgnoreCase));
        if (parent is not null && parent.Id != news.SourceId)
            links.Children.Add(EvidenceLink("앞선 글 ↗", "https://x.com/thsottiaux/status/" + parent.Id));
        content.Children.Add(links);
        if (news.DeadlineEvidence?.Contains("1시간") == true && !confirmed && news.State != CreditGrantState.Completed)
            content.Children.Add(Text("원문 시각에 1시간 차이 가능", 9, Warning, margin: new Thickness(0, 3, 0, 0)));
        CreditNewsCard = new Border { Background = Brush("#F8F5FD"), BorderBrush = Brush("#E6DEF2"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(15, 12, 15, 10), Margin = new Thickness(0, 0, 0, 10), Child = content };
        return CreditNewsCard;
    }
}
