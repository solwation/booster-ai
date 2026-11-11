using Avalonia;
using Avalonia.Headless;
using BoosterAI;

namespace BoosterAI.Tests;

/// <summary>
/// One headless Avalonia platform for the whole test run, on its own UI thread: Avalonia binds its dispatcher to the
/// thread that starts it, and xUnit runs tests on varying threads. Everything that touches controls runs through
/// <see cref="Run"/>. Nothing is shown on screen. The App is created once (it sets the language from the config file
/// it finds then), so a test that needs a language sets it inside its own work.
/// </summary>
internal static class UiSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(UiSession), AvaloniaTestIsolationLevel.PerAssembly));

    /// <summary>Called by <see cref="HeadlessUnitTestSession"/>: the real App with Skia, so fonts and resources resolve.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public static T Run<T>(Func<T> work) => Session.Value.Dispatch(work, CancellationToken.None).GetAwaiter().GetResult();

    public static void Run(Action work) => Session.Value.Dispatch(work, CancellationToken.None).GetAwaiter().GetResult();
}
