using System.Text;
using Gplus.Filtering;
using Gplus.Mail;

namespace Gplus.State;

/// <summary>
/// One tab-separated line per judged message, in logs\decisions-yyyy-MM-dd.tsv. Opens straight
/// in Excel — sort by Score to tune thresholds, or filter Action=REMOVED to audit.
/// </summary>
public sealed class DecisionLog(string folder, int retentionDays)
{
    private readonly Lock _gate = new();

    public void Write(MailItem m, Verdict v, string action)
    {
        var path = Path.Combine(folder, $"decisions-{DateTime.Now:yyyy-MM-dd}.tsv");
        var line = string.Join('\t',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), action, v.Score, Clean(v.Category),
            Clean(m.FromName), Clean(m.FromAddress), Clean(m.Subject), Clean(v.ReasonText), m.Id);
        lock (_gate)
        {
            Directory.CreateDirectory(folder);
            var isNew = !File.Exists(path);
            using var w = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew));
            if (isNew) w.WriteLine("Time\tAction\tScore\tCategory\tFromName\tFromAddress\tSubject\tReasons\tMessageId");
            w.WriteLine(line);
        }
    }

    public void Prune()
    {
        if (!Directory.Exists(folder)) return;
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var f in Directory.GetFiles(folder, "*.tsv").Concat(Directory.GetFiles(folder, "*.log")))
            if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
    }

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
