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

    /// <summary>Learn from the two labels below. Put a message in <see cref="TrainLabel"/> to block its
    /// sender and trash it; put one in <see cref="NotJunkLabel"/> to allow its sender and restore it.</summary>
    public bool LearnFromLabels { get; set; } = true;

    public string TrainLabel { get; set; } = "Gplus-Train";

    public string NotJunkLabel { get; set; } = "Gplus-NotJunk";

    /// <summary>Learn words and phrases from junk and good mail, and score new mail with them.</summary>
    public bool WordFilter { get; set; } = true;

    /// <summary>Junk examples needed before the word filter starts scoring.</summary>
    public int WordFilterMinSpam { get; set; } = 30;

    /// <summary>Good examples needed before the word filter starts scoring.</summary>
    public int WordFilterMinHam { get; set; } = 30;

    /// <summary>Examples gathered automatically per sweep (training labels are always learned).</summary>
    public int WordFilterLearnPerSweep { get; set; } = 40;

    /// <summary>Stop gathering automatic examples for a side once it has this many.</summary>
    public int WordFilterMaxPerSide { get; set; } = 5000;

    /// <summary>Removed mail counts as confirmed junk once it has sat this many days without being rescued.</summary>
    public int WordFilterConfirmDays { get; set; } = 2;

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

    /// <summary>Pop up a desktop message when Gmail rejects the saved login (the service can't sweep until you re-authorize).</summary>
    public bool AuthAlert { get; set; } = true;

    /// <summary>Hours between repeat alerts while the login stays broken.</summary>
    public int AuthAlertRepeatHours { get; set; } = 12;

    /// <summary>Days of decision logs to keep.</summary>
    public int LogRetentionDays { get; set; } = 60;
}
