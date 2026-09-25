using System.Text.Json;
using Gplus.Filtering;

namespace Gplus.State;

/// <summary>
/// Senders learned from the Gplus-Train and Gplus-NotJunk labels, kept in learned.json apart from
/// the hand-written rules.json (so rewriting it never loses your comments or edits). Merged into
/// the rule set's BlockSenders / AllowSenders at load. Delete an entry to undo it.
/// </summary>
public sealed class LearnedRules
{
    public sealed class Entry
    {
        /// <summary>Same syntax as rules.json: "a@b.com" or a bare domain "b.com".</summary>
        public string Sender { get; set; } = "";
        public DateTime Added { get; set; }
        /// <summary>The message it was learned from: "from — subject".</summary>
        public string Example { get; set; } = "";
    }

    public List<Entry> Block { get; set; } = [];
    public List<Entry> Allow { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>A missing file is empty; a broken one throws, so a bad hand edit is never overwritten.</summary>
    public static LearnedRules Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<LearnedRules>(File.ReadAllText(path), JsonOpts) ?? new LearnedRules()
            : new LearnedRules();

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Adds a block entry. False if an existing learned block already covers the sender.</summary>
    public bool AddBlock(string sender, string example) => Add(Block, sender, example);

    /// <summary>Adds an allow entry. False if an existing learned allow already covers the sender.</summary>
    public bool AddAllow(string sender, string example) => Add(Allow, sender, example);

    /// <summary>Removes every learned block that matches this address (the undo for a wrong training).</summary>
    public List<string> RemoveBlocksMatching(string address) => RemoveMatching(Block, address);

    /// <summary>Removes every learned allow that matches this address (you changed your mind).</summary>
    public List<string> RemoveAllowsMatching(string address) => RemoveMatching(Allow, address);

    public void MergeInto(RuleSet rules)
    {
        rules.BlockSenders.AddRange(Block.Select(e => e.Sender).Where(s => s.Length > 0));
        rules.AllowSenders.AddRange(Allow.Select(e => e.Sender).Where(s => s.Length > 0));
    }

    private static bool Add(List<Entry> list, string sender, string example)
    {
        sender = sender.Trim().ToLowerInvariant();
        if (sender.Length == 0) return false;
        var probe = sender.Contains('@') ? sender : "x@" + sender;
        if (JunkClassifier.MatchesAny(list.Select(e => e.Sender), probe, Mail.Addresses.DomainOf(probe))) return false;
        list.Add(new Entry { Sender = sender, Added = DateTime.Now, Example = example });
        return true;
    }

    private static List<string> RemoveMatching(List<Entry> list, string address)
    {
        var domain = Mail.Addresses.DomainOf(address);
        var hits = list.Where(e => JunkClassifier.MatchesAny([e.Sender], address, domain)).ToList();
        foreach (var e in hits) list.Remove(e);
        return hits.Select(e => e.Sender).ToList();
    }
}
