using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gplus.Tests;

public class AuthAlertTests
{
    private sealed class Harness
    {
        public DateTime Clock = new(2026, 10, 3, 9, 0, 0);
        public List<string> Shown = [];
        public bool SignedIn = true;
        public readonly AuthAlert Alert;

        public Harness()
        {
            Alert = new AuthAlert(NullLogger<AuthAlert>.Instance)
            {
                Now = () => Clock,
                Display = text => { if (!SignedIn) return false; Shown.Add(text); return true; },
            };
        }
    }

    private static readonly GplusOptions Opt = new() { AuthAlert = true, AuthAlertRepeatHours = 12 };

    [Fact]
    public void FirstFailureAlertsImmediately_ThenNotAgainWithinTheRepeatWindow()
    {
        var h = new Harness();
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Single(h.Shown);
        Assert.Contains("authorize", h.Shown[0]);

        h.Clock = h.Clock.AddHours(11);       // service retries every 15 min — no spam
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Single(h.Shown);

        h.Clock = h.Clock.AddHours(1);        // 12 h later it reminds again
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Equal(2, h.Shown.Count);
    }

    [Fact]
    public void RecoveryResets_SoTheNextOutageAlertsRightAway()
    {
        var h = new Harness();
        h.Alert.LoginFailed(Opt, "rejected");
        h.Alert.LoginOk();
        h.Clock = h.Clock.AddMinutes(30);
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Equal(2, h.Shown.Count);
    }

    [Fact]
    public void NobodySignedIn_RetriesOnTheNextSweep()
    {
        var h = new Harness { SignedIn = false };
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Empty(h.Shown);

        h.SignedIn = true;
        h.Clock = h.Clock.AddMinutes(15);
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Single(h.Shown);
    }

    [Fact]
    public void Disabled_NeverAlerts()
    {
        var h = new Harness();
        h.Alert.LoginFailed(new GplusOptions { AuthAlert = false }, "rejected");
        Assert.Empty(h.Shown);
    }

    [Fact]
    public void MessageSaysWhenItStartedFailing()
    {
        var h = new Harness();
        var start = h.Clock;
        h.Alert.LoginFailed(Opt, "rejected");
        h.Clock = h.Clock.AddHours(13);
        h.Alert.LoginFailed(Opt, "rejected");
        Assert.Contains($"since {start:g}", h.Shown[1]);
    }
}
