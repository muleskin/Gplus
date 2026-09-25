namespace Gplus;

public enum JunkAction
{
    /// <summary>Move to Gmail Trash (auto-purged by Gmail after 30 days).</summary>
    Trash,
    /// <summary>Report as spam (moves to Spam and trains Gmail's own filter).</summary>
    Spam,
    /// <summary>Remove from Inbox and keep under the Gplus label only (nothing is deleted).</summary>
    Archive,
}

/// <summary>Bound from the "Gplus" section of appsettings.json.</summary>
public sealed class GplusOptions
{
    /// <summary>Seconds between inbox sweeps.</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Gmail search used to find candidates.</summary>
    public string Query { get; set; } = "is:unread in:inbox newer_than:30d";

    /// <summary>Max messages examined per sweep (protects the API quota on the first run).</summary>
    public int MaxPerPass { get; set; } = 250;

    public JunkAction Action { get; set; } = JunkAction.Trash;

    /// <summary>Label added to every removed message so you can find them: label:Gplus-Removed.</summary>
    public string RemovedLabel { get; set; } = "Gplus-Removed";

    /// <summary>When true the service only logs what it would do.</summary>
    public bool DryRun { get; set; }

    /// <summary>Other addresses or whole domains that are "you" (forwarded accounts), e.g.
    /// "me@work.com" or "mydomain.com". Your Gmail address and send-as aliases are added automatically.</summary>
    public List<string> MyAddresses { get; set; } = [];

    /// <summary>Learn the addresses you have sent mail to and never remove mail from them.</summary>
    public bool LearnCorrespondents { get; set; } = true;

    /// <summary>Hours between refreshes of the sent-mail correspondent list.</summary>
    public int CorrespondentRefreshHours { get; set; } = 12;

    /// <summary>Cap on sent messages read the first time the correspondent list is built.</summary>
    public int CorrespondentScanMax { get; set; } = 2000;

    /// <summary>Days of decision logs to keep.</summary>
    public int LogRetentionDays { get; set; } = 60;
}
