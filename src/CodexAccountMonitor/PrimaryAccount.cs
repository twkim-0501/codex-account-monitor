using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private readonly AccountSource currentDesktop = new() { Id = "desktop-current", Name = "현재 계정", ShortName = "현재" };
    private AccountConnection? desktopConnection;
    private AccountSnapshot? desktopIdentity;
    private readonly DispatcherTimer primaryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool primaryRefreshing;
    private DateTimeOffset nextIdentityCheck;
    private (bool Exists, long Length, long Written)? desktopAuthRevision;

    private AccountDisplayOrder DisplayOrder() => PrimaryAccountOrdering.Build(settings.Sources, snapshots, desktopIdentity, currentDesktop);

    private void InitializePrimaryAccount()
    {
        if (demo)
        {
            desktopIdentity = snapshots.GetValueOrDefault(settings.Sources.First().Id);
            return;
        }
        desktopConnection = new(currentDesktop);
        primaryTimer.Tick += async (_, _) =>
        {
            var revision = desktopConnection.LocalAuthRevision();
            if (revision != desktopAuthRevision || DateTimeOffset.UtcNow >= nextIdentityCheck) await RefreshPrimaryAccountAsync();
        };
        if (screenshotPath is null) primaryTimer.Start();
    }

    private async Task RefreshPrimaryAccountAsync()
    {
        if (demo || exiting || primaryRefreshing || desktopConnection is null) return;
        primaryRefreshing = true;
        try
        {
            desktopAuthRevision = desktopConnection.LocalAuthRevision();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var identity = await desktopConnection.ReadIdentityAsync(timeout.Token);
            if (!string.Equals(desktopIdentity?.Email, identity.Email, StringComparison.OrdinalIgnoreCase))
            {
                snapshots.Remove(currentDesktop.Id); healthy.Remove(currentDesktop.Id); errors.Remove(currentDesktop.Id);
            }
            desktopIdentity = identity;
            // Reuse the named local/SSH account when registered; otherwise show the current login automatically.
            if (DisplayOrder().PrimarySourceId == currentDesktop.Id &&
                (!healthy.Contains(currentDesktop.Id) || !snapshots.TryGetValue(currentDesktop.Id, out var previous) ||
                    DateTimeOffset.UtcNow - previous.UpdatedAt >= TimeSpan.FromSeconds(settings.RefreshSeconds)))
            {
                try
                {
                    var snapshot = await desktopConnection.ReadAsync(timeout.Token);
                    desktopIdentity = snapshot;
                    snapshots[currentDesktop.Id] = snapshot; healthy.Add(currentDesktop.Id); errors.Remove(currentDesktop.Id);
                }
                catch (Exception error) when (!lifetime.IsCancellationRequested)
                {
                    healthy.Remove(currentDesktop.Id); errors[currentDesktop.Id] = SafeError(error);
                }
            }
            desktopAuthRevision = desktopConnection.LocalAuthRevision();
        }
        catch (Exception) { desktopIdentity = null; }
        finally
        {
            nextIdentityCheck = DateTimeOffset.UtcNow.AddSeconds(30);
            primaryRefreshing = false;
            if (!exiting) RenderCards();
        }
    }

    private MiniAccount[] MiniAccounts()
    {
        var order = DisplayOrder();
        return order.Sources.Select(source =>
        {
            snapshots.TryGetValue(source.Id, out var data);
            return new MiniAccount(source.Name, data?.Windows.Count > 0 ? data.Windows.Min(x => x.RemainingPercent) : null,
                healthy.Contains(source.Id), data?.OrdinaryUsageAllowed == false, source.ShortName, source.Id == order.PrimarySourceId);
        }).ToArray();
    }
}
