using System.Diagnostics;
using System.Security.Principal;

namespace Gplus;

/// <summary>Thin wrapper over sc.exe — keeps install/uninstall one command for the user.</summary>
public static class ServiceInstaller
{
    public const string ServiceName = "Gplus";
    private const string DisplayName = "Gplus Gmail Junk Remover";
    private const string Description = "Sweeps unread Gmail and removes loan offers, marketing, Temu, horoscopes, weather, crime reports and other junk. Config and logs: %ProgramData%\\Gplus";

    public static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static int Install()
    {
        if (!RequireAdmin()) return 1;
        var exe = Environment.ProcessPath!;
        if (exe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("Note: installing from a Debug build folder. For a permanent install, publish first (see README).");

        int rc = Sc($"create {ServiceName} binPath= \"\\\"{exe}\\\"\" start= delayed-auto DisplayName= \"{DisplayName}\"");
        if (rc != 0) return rc;
        Sc($"description {ServiceName} \"{Description}\"");
        // Restart after 1 min on the first three failures; reset the failure count daily.
        Sc($"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000");
        rc = Sc($"start {ServiceName}");
        Console.WriteLine(rc == 0
            ? $"Installed and started. Activity log: {AppPaths.LogDir}"
            : "Installed, but it did not start. Check that 'Gplus authorize' has been run.");
        return 0;
    }

    public static int Uninstall()
    {
        if (!RequireAdmin()) return 1;
        Sc($"stop {ServiceName}");
        return Sc($"delete {ServiceName}");
    }

    public static int Start() => RequireAdmin() ? Sc($"start {ServiceName}") : 1;
    public static int Stop() => RequireAdmin() ? Sc($"stop {ServiceName}") : 1;
    public static int Status() => Sc($"query {ServiceName}");

    private static bool RequireAdmin()
    {
        if (IsAdmin()) return true;
        Console.Error.WriteLine("This needs an elevated (Run as administrator) prompt.");
        return false;
    }

    private static int Sc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        var text = output.Trim();
        if (text.Length > 0) Console.WriteLine(text);
        return p.ExitCode;
    }
}
