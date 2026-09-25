namespace Gplus.State;

/// <summary>
/// Message ids already judged "keep", so unread mail you're leaving alone isn't re-fetched every
/// sweep. Entries expire after <see cref="Ttl"/>, which also lets rule changes re-evaluate recent mail.
/// Kept locally rather than as a Gmail label so your mailbox isn't cluttered.
/// </summary>
public sealed class SeenStore(string path)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(35);

    private readonly Dictionary<string, long> _seen = JsonFile.LoadOr(path, () => new Dictionary<string, long>());
    private bool _dirty;

    public bool Contains(string id) => _seen.ContainsKey(id);

    public void Add(string id)
    {
        _seen[id] = DateTimeOffset.UtcNow.Add(Ttl).ToUnixTimeSeconds();
        _dirty = true;
    }

    /// <summary>Forget everything (used when rules.json changes so recent mail is re-judged).</summary>
    public void Clear()
    {
        if (_seen.Count == 0) return;
        _seen.Clear();
        _dirty = true;
    }

    public void Save()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var id in _seen.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList())
        {
            _seen.Remove(id);
            _dirty = true;
        }
        if (!_dirty) return;
        JsonFile.Save(path, _seen);
        _dirty = false;
    }
}
