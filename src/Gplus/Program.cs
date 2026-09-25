using System.Text.Json;
using Gplus;
using Gplus.Filtering;
using Gplus.Gmail;
using Gplus.Mail;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "run";
if (WindowsServiceHelpers.IsWindowsService()) command = "run";
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "run" => RunHost(),
        "authorize" or "auth" => await Authorize(rest),
        "scan" => await Scan(rest),
        "test" => Test(rest),
        "install" => ServiceInstaller.Install(),
        "uninstall" => ServiceInstaller.Uninstall(),
        "start" => ServiceInstaller.Start(),
        "stop" => ServiceInstaller.Stop(),
        "status" => ServiceInstaller.Status(),
        "help" or "-h" or "--help" or "/?" => Help(),
        _ => Help(1),
    };
}
catch (NotAuthorizedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (Google.GoogleApiException ex)
{
    Console.Error.WriteLine($"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}");
    return 4;
}

// ---------------------------------------------------------------------------------------------

static HostApplicationBuilder CreateBuilder(bool console)
{
    AppPaths.EnsureDataDir();
    AppPaths.EnsureRulesFile();

    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        ContentRootPath = AppContext.BaseDirectory, // services start in System32
    });
    // Lowest priority: the defaults compiled into the exe. Then (already added by the host) an
    // appsettings.json beside the exe if one exists, then the user's %ProgramData%\Gplus copy.
    builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Json.JsonStreamConfigurationSource
    {
        Stream = AppPaths.OpenEmbedded("appsettings.json"),
    });
    builder.Configuration.AddJsonFile(AppPaths.UserSettings, optional: true, reloadOnChange: true);
    builder.Services.Configure<GplusOptions>(builder.Configuration.GetSection("Gplus"));
    builder.Services.AddSingleton<Sweeper>();

    builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogDir));
    if (console)
    {
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
    }
    else
    {
        builder.Services.AddWindowsService(o => o.ServiceName = ServiceInstaller.ServiceName);
        builder.Services.Configure<EventLogSettings>(s => s.SourceName = ServiceInstaller.ServiceName);
        builder.Services.AddHostedService<Worker>();
    }
    return builder;
}

static int RunHost()
{
    CreateBuilder(console: false).Build().Run();
    return 0;
}

static async Task<int> Authorize(string[] a)
{
    var secret = a.FirstOrDefault();
    if (secret is null && !File.Exists(AppPaths.ClientSecret))
    {
        Console.Error.WriteLine("Usage: Gplus authorize <path-to-client_secret.json>   (see README for how to get it)");
        return 1;
    }
    if (!ServiceInstaller.IsAdmin())
        Console.WriteLine("Tip: run this from an elevated prompt so the saved login lands in a folder only admins and the service can read.");

    Console.WriteLine("Opening your browser for Google sign-in...");
    var email = await GmailConnector.AuthorizeInteractiveAsync(secret, CancellationToken.None);
    Console.WriteLine($"Authorized: {email}");
    Console.WriteLine($"Login saved (encrypted) under {AppPaths.TokenDir}");
    Console.WriteLine("Next: 'Gplus scan' to preview what would be removed, then 'Gplus install'.");
    return 0;
}

static async Task<int> Scan(string[] a)
{
    bool apply = a.Contains("--apply");
    bool junkOnly = a.Contains("--junk-only");
    int? max = Arg(a, "--max") is { } m ? int.Parse(m) : null;
    var query = Arg(a, "--query");

    using var host = CreateBuilder(console: true).Build();
    var sweeper = host.Services.GetRequiredService<Sweeper>();
    sweeper.Judged += (item, v, action) =>
    {
        if (junkOnly && !v.IsJunk) return;
        Console.ForegroundColor = v.IsJunk ? ConsoleColor.Red : ConsoleColor.DarkGray;
        Console.Write($"{(v.IsJunk ? "JUNK" : "keep"),-4} {v.Score,4}  {Trim(v.Category, 22),-22}  ");
        Console.ResetColor();
        Console.WriteLine($"{Trim(item.FromAddress, 32),-32}  {Trim(item.Subject, 60)}");
        if (v.IsJunk || a.Contains("--why"))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"           {v.ReasonText}");
            Console.ResetColor();
        }
    };

    Console.WriteLine(apply ? "Scanning and REMOVING junk..." : "Preview only (nothing is changed; add --apply to act)...");
    var r = await sweeper.SweepAsync(new SweepOptions(Apply: apply, UseSeen: false, max, query), CancellationToken.None);
    Console.WriteLine();
    Console.WriteLine($"{sweeper.Account}: {r.Examined} examined, {r.Junk} junk{(apply ? $", {r.Removed} removed" : " (preview)")}.");
    Console.WriteLine($"Details: {AppPaths.LogDir}\\decisions-{DateTime.Now:yyyy-MM-dd}.tsv");
    return 0;
}

/// <summary>Classify messages from a JSON file — for tuning rules without touching Gmail.</summary>
static int Test(string[] a)
{
    if (a.Length == 0)
    {
        Console.Error.WriteLine("Usage: Gplus test <messages.json> [rules.json]");
        return 1;
    }
    AppPaths.EnsureRulesFile();
    var rulesPath = a.Length > 1 ? a[1] : AppPaths.Rules;
    var classifier = new JunkClassifier(RuleSet.Load(rulesPath));
    var samples = JsonSerializer.Deserialize<List<TestMessage>>(File.ReadAllText(a[0]),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip }) ?? [];

    var ctx = new ClassifyContext();
    int wrong = 0;
    foreach (var s in samples)
    {
        var v = classifier.Classify(s.ToMailItem(), ctx);
        var miss = s.Expect is { } e && e != v.IsJunk;
        if (miss) wrong++;
        Console.ForegroundColor = miss ? ConsoleColor.Yellow : v.IsJunk ? ConsoleColor.Red : ConsoleColor.DarkGray;
        Console.WriteLine($"{(miss ? "MISS" : v.IsJunk ? "JUNK" : "keep"),-4} {v.Score,4}  {Trim(v.Category, 22),-22}  {Trim(s.From, 34),-34}  {Trim(s.Subject, 60)}");
        Console.ResetColor();
        if (miss || a.Contains("--why")) Console.WriteLine($"           {v.ReasonText}");
    }
    var labelled = samples.Count(s => s.Expect is not null);
    if (labelled > 0) Console.WriteLine($"\n{labelled - wrong}/{labelled} labelled samples classified as expected.");
    return wrong == 0 ? 0 : 3;
}

static int Help(int rc = 0)
{
    Console.WriteLine("""
        Gplus — removes junk from unread Gmail. Runs as a Windows service.

          Gplus authorize <client_secret.json>   One-time Google sign-in (opens a browser)
          Gplus scan [--apply] [--max N] [--query "..."] [--junk-only] [--why]
                                                 Preview (or with --apply, act on) unread inbox mail now
          Gplus test <messages.json> [rules.json] Classify sample messages offline, for tuning rules
          Gplus install | uninstall | start | stop | status
                                                 Manage the Windows service (elevated prompt)

        Settings, rules, login and logs live in %ProgramData%\Gplus.
        """);
    return rc;
}

static string? Arg(string[] a, string name)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

internal sealed class TestMessage
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string ReplyTo { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int Links { get; set; }
    public List<string> Labels { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    /// <summary>true = should be junk, false = should be kept, null = unlabelled.</summary>
    public bool? Expect { get; set; }

    public MailItem ToMailItem()
    {
        var (name, addr) = Addresses.Parse(From);
        return new MailItem
        {
            Id = Guid.NewGuid().ToString("N"),
            FromName = name,
            FromAddress = addr,
            ReplyToAddress = Addresses.Parse(ReplyTo).Address,
            Recipients = Addresses.ParseList(To).ToList(),
            Subject = Subject,
            Body = Body,
            LinkCount = Links,
            Labels = Labels,
            Headers = new Dictionary<string, string>(Headers, StringComparer.OrdinalIgnoreCase),
        };
    }
}
