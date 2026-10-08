using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private const string CreditPurple = "#6951A1";
    private bool creditNewsExpanded;
    internal CreditGrantNews? RenderedCreditNews { get; private set; }
    internal Expander? CreditNewsExpander { get; private set; }
    internal Expander? ForecastExpander { get; private set; }

    private Border BuildCreditGrantCard(CreditGrantNews news)
    {
        var confirmed = news.State == CreditGrantState.AccountConfirmed;
        var tone = confirmed ? ForecastGreen : CreditPurple;
        var header = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text("초기화권 지급 소식", 12, Ink, FontWeights.SemiBold));
        var badge = new Border { Background = Brush(confirmed ? "#E5F1EB" : "#EEE8F8"), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3, 7, 3),
            Child = Text(news.Badge, 9, tone, FontWeights.SemiBold) };
        Grid.SetColumn(badge, 1); top.Children.Add(badge); header.Children.Add(top);

        var main = new Grid { Margin = new Thickness(0, 12, 0, 11) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(63) }); main.ColumnDefinitions.Add(new ColumnDefinition());
        var ticket = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var amount = Text(news.CreditsPerAccount is { } count ? $"+{count}" : "↻", 30, tone, FontWeights.SemiBold);
        amount.HorizontalAlignment = HorizontalAlignment.Center; ticket.Children.Add(amount);
        var caption = Text(news.CreditsPerAccount.HasValue ? "장 / 계정" : "초기화권", 9, tone); caption.HorizontalAlignment = HorizontalAlignment.Center; ticket.Children.Add(caption);
        main.Children.Add(new Border { Background = Brush("#FFFFFF"), CornerRadius = new CornerRadius(9), Padding = new Thickness(3), Margin = new Thickness(0, 0, 10, 0), Child = ticket });
        var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(Text(news.Headline, 15, Ink, FontWeights.SemiBold));
        var names = news.Accounts.Where(a => a.State == CreditGrantAccountState.Confirmed).Select(a => a.Name).ToArray();
        var accountSummary = names.Length > 0 ? string.Join(" · ", names.Take(2)) + (names.Length > 2 ? $" 외 {names.Length - 2}개" : "") + " 지급 확인"
            : news.State == CreditGrantState.Completed ? "내 계정에도 들어왔는지 확인 중" : "내 계정 지급 확인 대기";
        summary.Children.Add(Text(accountSummary, 10, Soft, margin: new Thickness(0, 5, 0, 0)));
        Grid.SetColumn(summary, 1); main.Children.Add(summary); header.Children.Add(main);

        var steps = new Grid();
        for (var i = 0; i < 3; i++) steps.ColumnDefinitions.Add(new ColumnDefinition());
        var started = confirmed || news.State is CreditGrantState.Distributing or CreditGrantState.Completed;
        var labels = new[] { "예고 확인", "지급 진행", "계정 반영" };
        var publicAnnouncement = resetState.Outlook.Signals.Any(s => s.Id == news.SourceId && s.Level == ResetSignalLevel.Announced)
            || resetState.Outlook.Completions.Any(c => c.Id == news.SourceId && c.Kind == ResetSignalKind.CreditGrant);
        var done = new[] { publicAnnouncement, started, confirmed };
        for (var i = 0; i < 3; i++)
        {
            var item = new Border { Background = Brush(done[i] ? i == 2 ? "#E5F1EB" : "#EEE8F8" : "#F0EFF3"), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(3, 5, 3, 5), Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
            var label = Text((done[i] ? "✓ " : "○ ") + labels[i], 9, done[i] ? i == 2 ? ForecastGreen : CreditPurple : Soft, FontWeights.Medium);
            label.HorizontalAlignment = HorizontalAlignment.Center; item.Child = label; Grid.SetColumn(item, i); steps.Children.Add(item);
        }
        header.Children.Add(steps);
        var footer = new Grid { Margin = new Thickness(0, 11, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var timing = Text(news.Timing, 9, Soft, margin: new Thickness(0, 0, 8, 0)); timing.ToolTip = news.DeadlineEvidence ?? news.Explanation;
        footer.Children.Add(timing);
        var disclosure = Text(creditNewsExpanded ? "근거 닫기 ⌃" : "근거 보기 ⌄", 10, CreditPurple, FontWeights.Medium);
        Grid.SetColumn(disclosure, 1); footer.Children.Add(disclosure); header.Children.Add(footer);

        var expander = new Expander { Header = header, Content = BuildCreditGrantReasons(news), IsExpanded = creditNewsExpanded,
            ToolTip = "지급 소식의 출처와 내 계정에 들어왔는지 확인합니다" };
        expander.Expanded += (_, _) => { creditNewsExpanded = true; disclosure.Text = "근거 닫기 ⌃"; QueuePanelSize(); };
        expander.Collapsed += (_, _) => { creditNewsExpanded = false; disclosure.Text = "근거 보기 ⌄"; QueuePanelSize(); };
        CreditNewsExpander = expander;
        return new Border { Background = Brush("#F8F5FD"), BorderBrush = Brush("#E6DEF2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 1, 6, 0), Margin = new Thickness(0, 0, 0, 10), Child = expander };
    }

    private StackPanel BuildCreditGrantReasons(CreditGrantNews news)
    {
        var details = new StackPanel();
        details.Children.Add(new Border { Height = 1, Background = Brush("#E6DEF2"), Margin = new Thickness(0, 0, 0, 10) });
        if (news.SourceUrl is { } url)
        {
            var publicReason = resetState.Outlook.Signals.FirstOrDefault(s => s.Id == news.SourceId)?.Reason
                ?? "Tibo가 초기화권 지급을 마쳤다고 알렸습니다. 내 계정에 들어왔는지는 아래에서 확인합니다.";
            details.Children.Add(ReasonRow("Tibo의 공지", publicReason, url, demo));
            if (news.ConfirmedAccounts > 0) details.Children.Add(Text("아래 계정의 발급 기록에서 새 초기화권이 들어온 것을 확인했습니다.", 10, Soft, margin: new Thickness(0, 8, 0, 0)));
        }
        else details.Children.Add(Text(news.Explanation, 10, Soft));
        if (news.DeadlineEvidence is { } timing) details.Children.Add(Text(timing, 9, Soft, margin: new Thickness(0, 8, 0, 0)));
        foreach (var account in news.Accounts)
        {
            var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(account.Name, 10, Ink, FontWeights.Medium, new Thickness(0, 0, 8, 0)));
            var status = account.State switch
            {
                CreditGrantAccountState.Confirmed => $"+{account.NewCredits}장 확인" + (account.AvailableCount is { } count ? $" · 보유 {count}개" : ""),
                CreditGrantAccountState.Awaiting => "아직 지급 확인 안 됨",
                _ => "지급 여부를 확인할 정보 부족"
            };
            var value = Text(status, 9, account.State == CreditGrantAccountState.Confirmed ? ForecastGreen : Soft);
            if (account.GrantedAt is { } at) value.ToolTip = "서버 발급 시각 · " + ResetJudgment.KoreanTime(at);
            Grid.SetColumn(value, 1); row.Children.Add(value);
            details.Children.Add(row);
        }
        details.Children.Add(Text("발급 시각을 알 수 없는 계정은 아직 확인하지 못한 것으로 표시합니다. 초기화권을 직접 사용하면 한도가 초기화됩니다.", 9, Soft, margin: new Thickness(0, 10, 0, 0)));
        return details;
    }
}
