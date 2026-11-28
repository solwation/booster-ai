namespace BoosterAI;

/// <summary>
/// The status popup and status column of a tab whose account works on credits (#293): a used-up window while the account has
/// credits is not a stop. The popup says "Använder krediter · Krediter: okänt antal" and, when the Codex account still has a free
/// reset, offers "Använd reset" with the same question and redemption as the Accounts page (<see cref="AccountRow.UseResetAsync"/>).
/// Nothing is redeemed without that question.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The tab's account has a used-up window and credits: the agent works on credits.</summary>
    public bool PeekUsingCredits => PeekLimit?.UsingCredits == true;

    /// <summary>"Använder krediter · Krediter: okänt antal" (the balance only when the agent reported one); empty otherwise.</summary>
    public string PeekCreditsText => PeekLimit?.CreditsText ?? "";
    public bool HasPeekCredits => PeekCreditsText.Length > 0;

    /// <summary>The compact status column shows "Krediter" in the calmer credits colour instead of the warning one.</summary>
    public bool StatusIndicatorCredits => !IsStatusError && StatusIndicatorText.Length > 0 && StatusIndicatorText != "…" && PeekUsingCredits;

    /// <summary>The tab's Codex account on the Accounts page, whose reset count and redemption the popup shares; null for other agents.</summary>
    internal AccountRow? PeekResetRow => _llm == "codex" && QuotaAccountKey is { } key
        ? _owner.AccountsEditor("codex")?.Rows.FirstOrDefault(r => r.Key == key) : null;

    /// <summary>"Använd reset" in the clickable popup: on credits, with a reset left and no redemption running (<see cref="AccountRow.CanUseReset"/>).</summary>
    public bool CanPeekUseReset => PeekUsingCredits && PeekResetRow is { CanUseReset: true };

    public string PeekUseResetName => Loc.Format("peek.reset.credits.name", PeekResetRow?.Name ?? "");

    /// <summary>The popup's "Använd reset": the Accounts page's own flow, <paramref name="confirm"/> first; null when nothing was sent.</summary>
    internal Task<ResetOutcome?> UseResetAsync(Func<string, int, Task<bool>> confirm) =>
        CanPeekUseReset && PeekResetRow is { } row ? row.UseResetAsync(confirm) : Task.FromResult<ResetOutcome?>(null);

    private void RaiseCreditsPeek()
    {
        foreach (var n in new[] { nameof(PeekUsingCredits), nameof(PeekCreditsText), nameof(HasPeekCredits), nameof(StatusIndicatorCredits),
            nameof(CanPeekUseReset), nameof(PeekUseResetName) }) Raise(n);
    }
}

public sealed partial class MainViewModel
{
    /// <summary>
    /// Screenshots (--fake-credits [reset], #293), after <see cref="ShowSampleLimitsForAll"/>: codex's default account reports only a
    /// weekly window, used up, with credits of unknown amount (as Codex reports <c>balance: null</c>); <paramref name="reset"/> gives
    /// it one free reset, otherwise none. No file is read and nothing is redeemed.
    /// </summary>
    internal void ShowSampleCredits(bool reset)
    {
        var now = LimitsClock();
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var codex = AccountsEditor("codex")!;
        codex.SetSampleLimits("codex", new AccountLimits(null, new LimitWindow(100, hour.AddDays(2).AddHours(5), 10080),
            new LimitCredits(true, false, null), hour.AddMinutes(-3)) { SessionAbsent = true });
        codex.SetSampleResets(new ResetCredits(reset ? 1 : 0, false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
