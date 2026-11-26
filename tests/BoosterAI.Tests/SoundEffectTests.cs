using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

[Collection(ConfigFileCollection.Name)]
public class SoundEffectTests : IDisposable
{
    private readonly string _language = Loc.Language;

    public SoundEffectTests() => Loc.Use("sv");

    public void Dispose() => Loc.Use(_language);

    private sealed class Capture : ISoundPlayer
    {
        public List<SoundEffect> Played { get; } = [];
        public void Play(SoundEffect effect) => Played.Add(effect);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static AccountLimits Limits(double session) =>
        new(new LimitWindow(session, Now.AddHours(2), 300), null, null, Now);

    private static (MainViewModel Vm, Capture Sound, List<string> Toasts, DateTime[] Clock) Make(bool notifications = true, bool sounds = true, bool quotaWarn = true)
    {
        var sound = new Capture();
        var toasts = new List<string>();
        var clock = new[] { new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc) };
        var vm = new MainViewModel(new AppConfig { Notifications = notifications, SoundEffects = sounds, QuotaWarn = quotaWarn, Tabs = [new() { Name = "Atlas", Llm = "claude" }] })
        {
            LimitsClock = () => Now,
            QuotaNotify = (_, body) => toasts.Add(body),
            Sound = sound,
            SoundClock = () => clock[0],
        };
        return (vm, sound, toasts, clock);
    }

    [Fact]
    public void EachEventPlaysItsOwnSound()
    {
        var (vm, sound, _, _) = Make();
        vm.PlaySound(SoundEffect.Finished);
        vm.PlaySound(SoundEffect.Error);
        vm.CheckQuota("claude", "", null, Limits(97));
        vm.CheckQuota("claude", "", null, Limits(20));
        Assert.Equal([SoundEffect.Finished, SoundEffect.Error, SoundEffect.QuotaLow, SoundEffect.QuotaBack], sound.Played);
    }

    [Fact]
    public void TheSameSoundIsThrottledButOtherSoundsAndLaterOnesPlay()
    {
        var (vm, sound, _, clock) = Make();
        vm.PlaySound(SoundEffect.Finished);
        clock[0] = clock[0].AddSeconds(1);
        vm.PlaySound(SoundEffect.Finished);
        vm.PlaySound(SoundEffect.Error);
        clock[0] = clock[0].Add(SoundCatalog.Throttle);
        vm.PlaySound(SoundEffect.Finished);
        Assert.Equal([SoundEffect.Finished, SoundEffect.Error, SoundEffect.Finished], sound.Played);
    }

    [Fact]
    public void SoundEffectsOffSilencesEverythingButKeepsToastsAndBar()
    {
        var (vm, sound, toasts, _) = Make(sounds: false);
        vm.PlaySound(SoundEffect.Finished);
        vm.PlaySound(SoundEffect.Error);
        vm.CheckQuota("claude", "", null, Limits(97));
        vm.CheckQuota("claude", "", null, Limits(20));
        Assert.Empty(sound.Played);
        Assert.Equal(2, toasts.Count);
    }

    [Fact]
    public void NotificationsOffSilencesQuotaToastsButKeepsTheBarAndTheSound()
    {
        var (vm, sound, toasts, _) = Make(notifications: false);
        vm.CheckQuota("claude", "", null, Limits(97));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(toasts);
        Assert.Single(vm.Quota.Items);
        Assert.Equal([SoundEffect.QuotaLow], sound.Played);
        vm.CheckQuota("claude", "", null, Limits(20));
        Assert.Empty(toasts);
        Assert.Equal([SoundEffect.QuotaLow, SoundEffect.QuotaBack], sound.Played);
    }

    [Fact]
    public void WithoutAnInjectedPlayerNothingIsAudible()
    {
        var vm = new MainViewModel(new AppConfig());
        Assert.IsType<NoSoundPlayer>(vm.Sound);
        vm.PlaySound(SoundEffect.Error);
    }

    [Fact]
    public void EverySoundHasABundledWavAndAPlayName()
    {
        foreach (var e in Enum.GetValues<SoundEffect>())
        {
            var bytes = SoundCatalog.Bytes(e);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(e, SoundCatalog.Parse(Path.GetFileNameWithoutExtension(SoundCatalog.FileName(e))));
        }
        Assert.Null(SoundCatalog.Parse("nope"));
    }
}
