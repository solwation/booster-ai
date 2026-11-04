namespace BoosterAI;

/// <summary>Refreshes a live profile even when its persisted font already matches the requested value.</summary>
internal static class ZoomRefresh
{
    internal static void Apply(bool restore, Func<int> latestChoice, Func<int, bool> sync, Action reload)
    {
        var desired = latestChoice();
        if (!restore)
        {
            if (sync(desired)) reload();
            return;
        }

        // Reproduce the user's size toggle rather than relying on an unchanged font to redraw.
        // Never change config.json, and put the latest saved choice back after a handled failure.
        try
        {
            sync(desired == 100 ? 110 : 100);
            reload();
        }
        finally
        {
            sync(latestChoice());
            reload();
        }
    }
}
