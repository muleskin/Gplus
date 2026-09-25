using System.Security.AccessControl;
using System.Security.Principal;

namespace Gplus;

/// <summary>
/// Everything mutable lives in %ProgramData%\Gplus so the interactive "authorize" step and the
/// service (LocalSystem) share one token, one rules file and one set of logs.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Environment.GetEnvironmentVariable("GPLUS_DATA") is { Length: > 0 } d
        ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Gplus");

    public static string TokenDir => Path.Combine(DataDir, "token");
    public static string ClientSecret => Path.Combine(DataDir, "client_secret.json");
    public static string Rules => Path.Combine(DataDir, "rules.json");
    public static string Learned => Path.Combine(DataDir, "learned.json");
    public static string Words => Path.Combine(DataDir, "words.json");
    public static string UserSettings => Path.Combine(DataDir, "appsettings.json");
    public static string Seen => Path.Combine(DataDir, "seen.json");
    public static string Correspondents => Path.Combine(DataDir, "correspondents.json");
    public static string LogDir => Path.Combine(DataDir, "logs");

    /// <summary>
    /// Creates the data folder locked down to SYSTEM + Administrators (+ the creating user), since
    /// it holds the OAuth refresh token. Existing folders are left as they are.
    /// </summary>
    public static void EnsureDataDir()
    {
        if (Directory.Exists(DataDir))
        {
            Directory.CreateDirectory(LogDir);
            return;
        }

        var dir = Directory.CreateDirectory(DataDir);
        try
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var sid in new[]
            {
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                WindowsIdentity.GetCurrent().User!,
            })
            {
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            dir.SetAccessControl(acl);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PrivilegeNotHeldException)
        {
            // Not fatal: the folder still works, it just keeps inherited permissions.
        }
        Directory.CreateDirectory(LogDir);
    }

    /// <summary>Writes the shipped default rules (embedded in the exe) into the data folder on first use.</summary>
    public static void EnsureRulesFile()
    {
        if (File.Exists(Rules)) return;
        EnsureDataDir();
        using var src = OpenEmbedded("rules.default.json");
        using var dst = File.Create(Rules);
        src.CopyTo(dst);
    }

    /// <summary>A default file compiled into the exe (see the EmbeddedResource items in Gplus.csproj).</summary>
    public static Stream OpenEmbedded(string name) =>
        typeof(AppPaths).Assembly.GetManifestResourceStream("Gplus." + name)
        ?? throw new InvalidOperationException($"Embedded resource {name} is missing from the build.");
}
