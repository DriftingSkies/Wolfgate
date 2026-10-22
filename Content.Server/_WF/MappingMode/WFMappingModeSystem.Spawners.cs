using Content.Server._WF.MappingTools;
using Content.Server.Spawners.Components;
using Content.Shared._WF.MappingMode;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private WFMappingSpawnerIndex? _spawnerIndex;

    /// <summary>The prototypes each player was warned about, in order; forgotten when they leave or disconnect.</summary>
    private readonly Dictionary<NetUserId, List<string>> _unsavedWarned = new();

    private void InitializeSpawners()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnSpawnersPrototypesReloaded);
        SubscribeLocalEvent<WFMappingMembershipEndedEvent>(ev => _unsavedWarned.Remove(ev.User));
        SubscribeNetworkEvent<WFMappingSpawnerIndexRequest>(OnSpawnerIndexRequest);
    }

    private void OnSpawnersPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            _spawnerIndex = null;
    }

    /// <summary>
    /// For each prototype a map file leaves out, the spawner to place instead. Spawners the mappers can't place, and
    /// ones that wait for a game rule, are left out. Built on first use and again after a prototype reload.
    /// </summary>
    public WFMappingSpawnerIndex SpawnerIndex => _spawnerIndex ??= BuildSpawnerIndex();

    private WFMappingSpawnerIndex BuildSpawnerIndex()
    {
        var conditional = _factory.GetComponentName<ConditionalSpawnerComponent>();
        var random = _factory.GetComponentName<RandomSpawnerComponent>();
        var sources = new List<WFMappingSpawnerSource>();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract || prototype.HideSpawnMenu || IsDenied(prototype))
                continue;

            if (prototype.Components.TryGetValue(random, out var rare) && rare.Component is RandomSpawnerComponent r)
            {
                if (r.GameRules.Count == 0)
                    sources.Add(new WFMappingSpawnerSource(prototype.ID, Ids(r.Prototypes), Ids(r.RarePrototypes), r.Chance));
            }
            else if (prototype.Components.TryGetValue(conditional, out var entry) &&
                     entry.Component is ConditionalSpawnerComponent c && c.GameRules.Count == 0)
            {
                sources.Add(new WFMappingSpawnerSource(prototype.ID, Ids(c.Prototypes), Array.Empty<string>(), c.Chance));
            }
        }

        return WFMappingSpawnerIndex.Build(sources, id =>
            _prototypes.TryIndex<EntityPrototype>(id, out var spawned) && WFMappingSpawnerIndex.WontSave(spawned));
    }

    private static List<string> Ids(List<EntProtoId> ids)
    {
        var result = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            result.Add(id);
        }

        return result;
    }

    private void OnSpawnerIndexRequest(WFMappingSpawnerIndexRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !IsAdminMapper(player) && !TryGetPresent(player, out _) ||
            !TryThrottle(_listThrottle, player, "spawner index"))
            return;

        RaiseNetworkEvent(new WFMappingSpawnerIndexEvent(new Dictionary<string, string>(SpawnerIndex.Spawners)), player);
    }

    /// <summary>The prototypes a player was warned about since they joined their session, in order.</summary>
    public IReadOnlyList<string> UnsavedWarnings(NetUserId user)
    {
        return _unsavedWarned.TryGetValue(user, out var warned) ? warned : Array.Empty<string>();
    }

    /// <summary>
    /// Tells a player once that a prototype won't be saved with the map, naming the spawner that would be: a popup
    /// and a status line. The edit goes through.
    /// </summary>
    public void WarnUnsaved(ICommonSession player, string id)
    {
        if (!_prototypes.TryIndex<EntityPrototype>(id, out var prototype) || !WFMappingSpawnerIndex.WontSave(prototype))
            return;

        if (!_unsavedWarned.TryGetValue(player.UserId, out var warned))
            _unsavedWarned[player.UserId] = warned = new List<string>();

        if (warned.Contains(id))
            return;

        warned.Add(id);
        if (SpawnerIndex.TryGetSpawner(id, out var spawnerId) &&
            _prototypes.TryIndex<EntityPrototype>(spawnerId, out var spawner))
        {
            Notify(player, false, "wf-mapping-mode-unsaved-spawner",
                ("name", ProtoName(prototype)), ("spawner", ProtoName(spawner)));
            return;
        }

        Notify(player, false, "wf-mapping-mode-unsaved-none", ("name", ProtoName(prototype)));
    }

    private static string ProtoName(EntityPrototype prototype)
    {
        return string.IsNullOrEmpty(prototype.Name) ? prototype.ID : prototype.Name;
    }

    /// <summary>The unsaved warning for an engine placement request: an entity, not a tile.</summary>
    public void WarnPlacement(ICommonSession player, MsgPlacement msg)
    {
        if (Enabled && msg.PlaceType == PlacementManagerMessage.RequestPlacement && !msg.IsTile &&
            msg.EntityTemplateName is { } id)
            WarnUnsaved(player, id);
    }

    /// <summary>The unsaved warning for a mapping tools draw or paste that is going ahead.</summary>
    private void WarnUnsaved(WFMappingCanEditEvent ev)
    {
        if (ev.Cancelled || ev.Quiet || ev.Kind is not (WFMappingEditKind.Edit or WFMappingEditKind.Paste))
            return;

        if (ev.Prototypes != null)
        {
            foreach (var id in ev.Prototypes)
            {
                WarnUnsaved(ev.Session, id);
            }
        }

        if (ev.CarriedPrototypes != null)
        {
            foreach (var id in ev.CarriedPrototypes)
            {
                WarnUnsaved(ev.Session, id);
            }
        }
    }
}
