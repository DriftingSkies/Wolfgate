using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WG.Paintings;
using Content.Shared.Administration;
using Content.Shared.Eui;

namespace Content.Server._WG.Paintings;

/// <summary>
/// Server half of the admin painting browser: searches every stored painting, blocked ones included, and blocks or
/// deletes them.
/// </summary>
public sealed partial class WGPaintingAdminEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    /// <summary>Most results sent at once; the count says how many more match.</summary>
    private const int MaxResults = 200;

    private readonly WGPaintingStoreSystem _store;

    private string _query = string.Empty;
    private WGPaintingAdminFilter _filter;
    private string? _selected;
    private byte[]? _pixels;

    public WGPaintingAdminEui()
    {
        IoCManager.InjectDependencies(this);
        _store = _systems.GetEntitySystem<WGPaintingStoreSystem>();
    }

    public override async void Opened()
    {
        _store.Changed += OnStoreChanged;
        StateDirty();
        if (await _store.LoadPlayerNames() && !IsShutDown)
            StateDirty();
    }

    public override void Closed()
    {
        _store.Changed -= OnStoreChanged;
    }

    /// <summary>Refreshes the results, or closes the browser for a player who is no longer a moderator.</summary>
    private void OnStoreChanged()
    {
        if (!_admin.HasAdminFlag(Player, AdminFlags.Moderator))
        {
            Close();
            return;
        }

        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        var matches = Search(_store, _query, _filter).OrderByDescending(e => e.CreatedAt).ToList();
        var selected = _selected != null && _store.All.FirstOrDefault(e => e.Hash == _selected) is { } entry
            ? ToAdmin(entry)
            : null;

        return new WGPaintingAdminEuiState
        {
            Query = _query,
            Filter = _filter,
            Results = matches.Take(MaxResults).Select(ToAdmin).ToList(),
            Total = matches.Count,
            Loaded = _store.Loaded,
            Selected = selected,
            Pixels = selected == null ? null : _pixels,
        };
    }

    private WGPaintingAdminEntry ToAdmin(WGPaintingEntry e)
    {
        return new WGPaintingAdminEntry(e.Hash, e.Width, e.Height, e.Title, e.Artist, e.Author,
            _store.PlayerName(e.Author), e.ScannedBy, _store.PlayerName(e.ScannedBy), e.CreatedAt, e.Blocked);
    }

    /// <summary>
    /// Paintings matching every word of the query. A word matches any value; <c>field:word</c> matches one field
    /// (title, artist, author, scanner, hash, size, date). Quotes keep a phrase together.
    /// </summary>
    public static IEnumerable<WGPaintingEntry> Search(WGPaintingStoreSystem store, string query,
        WGPaintingAdminFilter filter)
    {
        IEnumerable<WGPaintingEntry> source = store.All;
        source = filter switch
        {
            WGPaintingAdminFilter.Visible => source.Where(e => !e.Blocked),
            WGPaintingAdminFilter.Blocked => source.Where(e => e.Blocked),
            _ => source,
        };

        foreach (var term in Terms(query))
        {
            var colon = term.IndexOf(':');
            var field = colon > 0 ? term[..colon].ToLowerInvariant() : null;
            var value = colon > 0 ? term[(colon + 1)..] : term;
            if (value.Length == 0)
                continue;

            source = source.Where(e => Matches(store, e, field, value)).ToList();
        }

        return source;
    }

    private static bool Matches(WGPaintingStoreSystem store, WGPaintingEntry e, string? field, string value)
    {
        bool Has(string text) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
        bool Player(Guid? id) => id != null && (Has(store.PlayerName(id)) || Has(id.Value.ToString()));

        return field switch
        {
            "title" => Has(e.Title),
            "artist" => Has(e.Artist),
            "author" => Player(e.Author),
            "scanner" => Player(e.ScannedBy),
            "hash" => Has(e.Hash),
            "size" => Has($"{e.Width}x{e.Height}"),
            "date" => e.CreatedAt.ToString("yyyy-MM-dd").StartsWith(value, StringComparison.Ordinal),
            _ => Has(e.Title) || Has(e.Artist) || Player(e.Author) || Player(e.ScannedBy) || Has(e.Hash)
                || Has($"{e.Width}x{e.Height}") || e.CreatedAt.ToString("yyyy-MM-dd").StartsWith(value, StringComparison.Ordinal),
        };
    }

    /// <summary>The query's words, with quoted phrases kept whole and <c>field:"a phrase"</c> kept together.</summary>
    private static IEnumerable<string> Terms(string query)
    {
        var parts = query.Split('"');
        var field = string.Empty;
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                if (parts[i].Trim() is { Length: > 0 } phrase)
                    yield return field + phrase;
                field = string.Empty;
                continue;
            }

            var words = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var count = words.Length;
            // A field word touching the next quote qualifies the phrase in it.
            if (i + 1 < parts.Length && count > 0 && parts[i].EndsWith(':') && words[^1].Length > 1)
            {
                field = words[^1];
                count--;
            }

            for (var w = 0; w < count; w++)
                yield return words[w];
        }
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (!_admin.HasAdminFlag(Player, AdminFlags.Moderator))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case WGPaintingAdminSearchMessage search:
                _query = search.Query.Length > 200 ? search.Query[..200] : search.Query;
                _filter = search.Filter;
                StateDirty();
                break;
            case WGPaintingAdminSelectMessage select:
                Select(select.Hash);
                break;
            case WGPaintingAdminBlockMessage block when WGPaintingRules.IsValidHash(block.Hash):
                _ = _store.SetBlocked(block.Hash, block.Blocked, Player.Name);
                break;
            case WGPaintingAdminDeleteMessage delete when WGPaintingRules.IsValidHash(delete.Hash):
                _ = _store.Delete(delete.Hash, Player.Name);
                break;
        }
    }

    private async void Select(string hash)
    {
        if (!WGPaintingRules.IsValidHash(hash) || hash == _selected)
            return;

        _selected = hash;
        _pixels = null;
        StateDirty();

        var pixels = await _store.GetAnyPixelsAsync(hash);
        if (IsShutDown || _selected != hash)
            return;

        _pixels = pixels;
        StateDirty();
    }
}
