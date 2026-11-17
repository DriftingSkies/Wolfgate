using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._WG.MappingTools;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.MappingMode;

public sealed partial class WGMappingModeSystem
{
    /// <summary>Every map mapping mode made: what it is for and its session.</summary>
    private readonly Dictionary<EntityUid, (WGMappingMapKind Kind, int SessionId)> _registry = new();

    private void InitializeRegistry()
    {
        SubscribeLocalEvent<WGMappingSessionMapComponent, EntityTerminatingEvent>(OnMapTerminating);
        SubscribeLocalEvent<WGMappingStackChangedEvent>(OnStackChanged);
    }

    private void RegisterMap(EntityUid map, WGMappingMapKind kind, int sessionId)
    {
        var tag = EnsureComp<WGMappingSessionMapComponent>(map);
        tag.Kind = kind;
        tag.SessionId = sessionId;
        _registry[map] = (kind, sessionId);
    }

    /// <summary>Whether a map is a level of the session.</summary>
    public bool IsSessionMap(EntityUid map, WGMappingSession session)
    {
        return _registry.TryGetValue(map, out var entry) &&
               entry.Kind == WGMappingMapKind.Session &&
               entry.SessionId == session.Id;
    }

    /// <summary>The session a map is a level of.</summary>
    public bool TryGetSessionOfMap(EntityUid map, [NotNullWhen(true)] out WGMappingSession? session)
    {
        session = null;
        return _registry.TryGetValue(map, out var entry) &&
               entry.Kind == WGMappingMapKind.Session &&
               _sessions.TryGetValue(entry.SessionId, out session);
    }

    /// <summary>The levels of a session.</summary>
    public List<EntityUid> SessionMaps(int sessionId)
    {
        return _registry
            .Where(pair => pair.Value.Kind == WGMappingMapKind.Session && pair.Value.SessionId == sessionId)
            .Select(pair => pair.Key)
            .ToList();
    }

    private void OnMapTerminating(Entity<WGMappingSessionMapComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_registry.Remove(ent.Owner, out var entry))
            return;

        if (entry.Kind == WGMappingMapKind.Admin && _adminMap == ent.Owner)
            _adminMap = null;

        // A level deleted under the session: fall back to another of its levels, or let the update close it.
        if (entry.Kind == WGMappingMapKind.Session &&
            _sessions.TryGetValue(entry.SessionId, out var session) &&
            session.RootMap == ent.Owner &&
            SessionMaps(session.Id).FirstOrDefault(m => !TerminatingOrDeleted(m)) is { Valid: true } other)
        {
            session.RootMap = other;
        }
    }

    /// <summary>
    /// A z-stack changed through the mapping tools: its new levels join the session of the levels already tagged,
    /// and the session's ground follows depth 0.
    /// </summary>
    private void OnStackChanged(WGMappingStackChangedEvent ev)
    {
        int? sessionId = null;
        foreach (var level in ev.Levels)
        {
            if (_registry.TryGetValue(level, out var entry) && entry.Kind == WGMappingMapKind.Session)
            {
                sessionId = entry.SessionId;
                break;
            }
        }

        if (sessionId is not { } id || !_sessions.TryGetValue(id, out var session))
            return;

        foreach (var level in ev.Levels)
        {
            if (!_registry.ContainsKey(level) && HasComp<MapComponent>(level))
                RegisterMap(level, WGMappingMapKind.Session, id);
        }

        if (ev.Ground >= 0 && ev.Ground < ev.Levels.Count)
            session.RootMap = ev.Levels[ev.Ground];

        MarkDirty(session);
    }
}
