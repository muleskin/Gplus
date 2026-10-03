using System.Runtime.InteropServices;

namespace Gplus;

/// <summary>
/// Tells the signed-in user when Gplus has lost its Gmail login. The service runs as LocalSystem,
/// so it can't raise a normal toast — instead it uses WTSSendMessage, which puts a message box on
/// the active console session from session 0. The first failure alerts right away; while the
/// login stays broken it repeats every <see cref="GplusOptions.AuthAlertRepeatHours"/>.
/// </summary>
public sealed class AuthAlert(ILogger<AuthAlert> logger)
{
    private DateTime? _failingSince;
    private DateTime _lastAlert = DateTime.MinValue;

    /// <summary>How the alert is displayed (replaced in tests so they don't pop real message boxes).</summary>
    internal Func<string, bool> Display { get; set; } = Show;
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>Call on every sweep that failed because Gmail rejected or lacks the login.</summary>
    public void LoginFailed(GplusOptions opt, string reason)
    {
        _failingSince ??= Now();
        if (!opt.AuthAlert) return;
        if (Now() - _lastAlert < TimeSpan.FromHours(Math.Max(1, opt.AuthAlertRepeatHours))) return;

        if (Display(Message(reason, _failingSince.Value)))
        {
            _lastAlert = Now();
            logger.LogWarning("Shown the 'Gmail login needed' alert on the desktop.");
        }
        // Nobody signed in yet: try again on the next sweep rather than waiting the full repeat window.
    }

    /// <summary>Call after a sweep that authenticated fine.</summary>
    public void LoginOk()
    {
        if (_failingSince is null) return;
        logger.LogInformation("Gmail login restored (it had failed since {Since:g}).", _failingSince);
        _failingSince = null;
        _lastAlert = DateTime.MinValue;
    }

    public static string Message(string reason, DateTime since)
    {
        var exe = Environment.ProcessPath ?? @"C:\Program Files\Gplus\Gplus.exe";
        return $"Gplus can't sign in to Gmail, so junk filtering and Gplus-Train learning are paused " +
               $"(since {since:g}).\n\n{reason}\n\n" +
               $"To fix it, open an elevated (Run as administrator) prompt and run:\n\n" +
               $"    \"{exe}\" authorize\n\n" +
               "Then sign in with your Google account in the browser that opens.";
    }

    /// <summary>Shows a non-blocking message box on the active console session. False if nobody is signed in.</summary>
    public static bool Show(string text)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == NoSession) return false;
        const string title = "Gplus — Gmail login needed";
        return WTSSendMessage(IntPtr.Zero, session, title, title.Length * 2, text, text.Length * 2,
                              MB_OK | MB_ICONWARNING | MB_SETFOREGROUND, 0, out _, false);
    }

    private const uint NoSession = 0xFFFFFFFF;
    private const int MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_SETFOREGROUND = 0x10000;

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSSendMessage(IntPtr hServer, uint sessionId, string title, int titleLength,
        string message, int messageLength, int style, int timeoutSeconds, out int response,
        [MarshalAs(UnmanagedType.Bool)] bool wait);
}
