using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Administration.Managers;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Station.Systems;
using Content.Server.Symphony;
using Content.Shared._WF.Feedback;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.StatusEffect;
using Content.Shared.Symphony;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Exceptions;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Feedback;

// The snapshot stored with each report: the build, the round, the player's body and surroundings, and the logs.
public sealed partial class FeedbackSystem
{
    /// <summary>Version of the context JSON. Bump it when a field changes meaning or goes away.</summary>
    public const int ContextSchema = 1;

    private const float NearbyRange = 5f;
    private const int NearbyGroups = 80;
    private const int ServerLogLines = 40;
    private const int ContainerDepth = 6;

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IGameMapManager _gameMaps = default!;
    [Dependency] private IRuntimeLog _runtimeLog = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private PlayTimeTrackingManager _playTime = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private JsonObject BuildContext(ICommonSession session, FeedbackSubmitEvent ev, bool screenshot)
    {
        var body = session.AttachedEntity is { } attached && Exists(attached) ? attached : (EntityUid?) null;
        var root = new JsonObject { ["schema"] = ContextSchema };

        Section(root, "build", BuildInfo);
        Section(root, "server", ServerInfo);
        Section(root, "reporter", () => ReporterInfo(session));
        Section(root, "character", () => body is { } uid ? CharacterInfo(uid, session) : null);
        Section(root, "nearby", () => body is { } uid ? NearbyInfo(uid) : null);
        Section(root, "server_log", ServerLogInfo);
        Section(root, "client", () => ClientInfo(ev, screenshot));
        return root;
    }

    /// <summary>One part of the snapshot. A part that throws is logged and left out, and the report still goes in.</summary>
    private void Section(JsonObject root, string name, Func<JsonNode?> build)
    {
        try
        {
            root[name] = build();
        }
        catch (Exception e)
        {
            Log.Error($"The {name} part of a report's context failed: {e}");
            root[name] = new JsonObject { ["error"] = e.Message };
        }
    }

    private JsonNode BuildInfo()
    {
        var engine = _cfg.GetCVar(CVars.BuildEngineVersion);
        if (engine == string.Empty)
            engine = typeof(EntitySystem).Assembly.GetName().Version?.ToString() ?? string.Empty;

        return new JsonObject
        {
            ["fork_id"] = _cfg.GetCVar(CVars.BuildForkId),
            ["version"] = _cfg.GetCVar(CVars.BuildVersion),
            ["engine_version"] = engine,
            ["manifest_hash"] = _cfg.GetCVar(CVars.BuildManifestHash),
            ["symphony_module"] = SharedSymphony.ModuleVersion,
            ["test_merges"] = SymphonyTestMerges.StatusJson(),
        };
    }

    private JsonNode ServerInfo()
    {
        var stations = new JsonArray();
        foreach (var (name, _) in _station.GetStationNames())
        {
            stations.Add(name);
        }

        var inRound = _ticker.RunLevel != GameRunLevel.PreRoundLobby;
        return new JsonObject
        {
            ["name"] = _cfg.GetCVar(CVars.GameHostName),
            ["time_utc"] = DateTime.UtcNow.ToString("o"),
            ["uptime_seconds"] = (long) _timing.RealTime.TotalSeconds,
            ["tick"] = _timing.CurTick.Value,
            ["tick_rate"] = _timing.TickRate,
            ["players"] = _players.PlayerCount,
            ["round_id"] = _ticker.RoundId,
            ["run_level"] = _ticker.RunLevel.ToString(),
            ["round_seconds"] = inRound ? (long) _ticker.RoundDuration().TotalSeconds : null,
            ["preset"] = _ticker.CurrentPreset?.ID,
            ["map"] = _gameMaps.GetSelectedMap()?.ID,
            ["stations"] = stations,
            ["exceptions"] = _runtimeLog.ExceptionCount,
        };
    }

    private JsonNode ReporterInfo(ICommonSession session)
    {
        var admin = _admins.GetAdminData(session, includeDeAdmin: true);
        int? playtime = null;
        try
        {
            playtime = (int) _playTime.GetOverallPlaytime(session).TotalMinutes;
        }
        catch (InvalidOperationException)
        {
            // Play time not loaded yet: the player has only just connected.
        }

        return new JsonObject
        {
            ["user_id"] = session.UserId.UserId.ToString(),
            ["name"] = session.Name,
            ["state"] = StateOf(session),
            ["ping"] = session.Channel.Ping,
            ["connected_utc"] = session.ConnectedTime.ToUniversalTime().ToString("o"),
            ["playtime_minutes"] = playtime,
            ["admin"] = admin != null,
            ["admin_active"] = admin is { Active: true },
            ["admin_title"] = admin?.Title,
        };
    }

    /// <summary>Where the player is in the round, in the words /symphony/players uses.</summary>
    private string StateOf(ICommonSession session)
    {
        if (session.Status != SessionStatus.InGame)
            return "connecting";
        if (!_ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) || status != PlayerGameStatus.JoinedGame)
            return status == PlayerGameStatus.ReadyToPlay ? "ready" : "lobby";
        if (session.AttachedEntity is { } entity && HasComp<GhostComponent>(entity))
            return "ghost";
        return session.AttachedEntity == null ? "joining" : "playing";
    }

    private JsonNode CharacterInfo(EntityUid body, ICommonSession session)
    {
        var info = Describe(body);
        info["description"] = MetaData(body).EntityDescription;
        info["ghost"] = HasComp<GhostComponent>(body);

        if (_minds.TryGetMind(session, out var mindId, out var mind))
        {
            if (_jobs.MindTryGetJob(mindId, out var job))
            {
                info["job"] = job.ID;
                info["job_name"] = job.LocalizedName;
            }

            var antags = new JsonArray();
            foreach (var role in _roles.MindGetAllRoleInfo(mindId).Where(r => r.Antagonist))
            {
                antags.Add(role.Prototype);
            }

            info["antag_roles"] = antags;

            // A ghost or a possessed body: the body the mind owns is somewhere else.
            if (mind.OwnedEntity is { } owned && owned != body && Exists(owned))
                info["own_body"] = Describe(owned);
        }

        if (TryComp<HumanoidAppearanceComponent>(body, out var humanoid))
            info["species"] = humanoid.Species.Id;

        if (TryComp<MobStateComponent>(body, out var mobState))
        {
            var state = mobState.CurrentState;
            info["mob_state"] = state.ToString();
        }

        if (TryComp<DamageableComponent>(body, out var damageable))
        {
            var damage = damageable.Damage;
            var types = new JsonObject();
            foreach (var (type, amount) in damage.DamageDict)
            {
                if (amount != 0)
                    types[type] = amount.Float();
            }

            info["damage_total"] = damageable.TotalDamage.Float();
            info["damage"] = types;
        }

        info["position"] = PositionInfo(body);
        info["containers"] = ContainerChain(body);

        if (TryComp<BuckleComponent>(body, out var buckle) && buckle.BuckledTo is { } strap)
            info["buckled_to"] = Describe(strap);
        if (TryComp<PullerComponent>(body, out var puller) && puller.Pulling is { } pulling)
            info["pulling"] = Describe(pulling);
        if (TryComp<PullableComponent>(body, out var pullable) && pullable.Puller is { } pulledBy)
            info["pulled_by"] = Describe(pulledBy);

        var held = new JsonArray();
        foreach (var item in _hands.EnumerateHeld(body))
        {
            held.Add(Describe(item));
        }

        info["hands"] = held;

        var worn = new JsonObject();
        if (_inventory.TryGetContainerSlotEnumerator(body, out var slots))
        {
            while (slots.NextItem(out var item, out var slot))
            {
                worn[slot.Name] = Describe(item);
            }
        }

        info["equipment"] = worn;

        if (TryComp<StatusEffectsComponent>(body, out var status))
        {
            var effects = new JsonArray();
            foreach (var effect in status.ActiveEffects.Keys)
            {
                effects.Add(effect);
            }

            info["status_effects"] = effects;
        }

        var uis = new JsonArray();
        foreach (var (owner, key) in _ui.GetActorUis(body))
        {
            var ui = Describe(owner);
            ui["key"] = $"{key.GetType().Name}.{key}";
            uis.Add(ui);
        }

        info["open_uis"] = uis;

        var components = new List<string>();
        foreach (var component in AllComps(body))
        {
            components.Add(Factory.GetComponentName(component.GetType()));
        }

        components.Sort(StringComparer.Ordinal);
        info["components"] = new JsonArray(components.Select(c => (JsonNode?) c).ToArray());
        return info;
    }

    private JsonNode PositionInfo(EntityUid body)
    {
        var xform = Transform(body);
        var world = _xform.GetMapCoordinates(body, xform);
        var info = new JsonObject
        {
            ["map_id"] = (int) world.MapId,
            ["map"] = xform.MapUid is { } map ? Describe(map) : null,
            ["world"] = Point(world.Position),
            ["rotation_degrees"] = Math.Round(_xform.GetWorldRotation(xform).Degrees, 1),
        };

        if (xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var gridComp))
        {
            info["grid"] = Describe(grid);
            info["grid_local"] = Point(_map.WorldToLocal(grid, gridComp, world.Position));

            var indices = _map.TileIndicesFor(grid, gridComp, world);
            info["tile"] = new JsonArray(indices.X, indices.Y);
            if (_map.TryGetTileRef(grid, gridComp, indices, out var tile))
                info["tile_type"] = _tileDefs[tile.Tile.TypeId].ID;

            if (_station.GetOwningStation(grid) is { } station)
                info["station"] = Name(station);
        }

        if (xform.ParentUid.IsValid() && xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid)
            info["parent"] = Describe(xform.ParentUid);

        if (_atmos.GetContainingMixture((body, xform)) is { } air)
        {
            info["air"] = new JsonObject
            {
                ["pressure_kpa"] = Math.Round(air.Pressure, 2),
                ["temperature_k"] = Math.Round(air.Temperature, 2),
                ["moles"] = Math.Round(air.TotalMoles, 2),
            };
        }

        return info;
    }

    /// <summary>What the body is inside, innermost first: a locker, a pod, a bag on someone's back.</summary>
    private JsonArray ContainerChain(EntityUid body)
    {
        var chain = new JsonArray();
        var current = body;
        while (chain.Count < ContainerDepth && _containers.TryGetContainingContainer(current, out var container))
        {
            var holder = Describe(container.Owner);
            holder["container"] = container.ID;
            chain.Add(holder);
            current = container.Owner;
        }

        return chain;
    }

    /// <summary>What stands around the body, by prototype, nearest first.</summary>
    private JsonNode? NearbyInfo(EntityUid body)
    {
        var centre = _xform.GetMapCoordinates(body);
        if (centre.MapId == MapId.Nullspace)
            return null;

        var groups = new Dictionary<string, (int Count, float Nearest)>();
        foreach (var uid in _lookup.GetEntitiesInRange(centre, NearbyRange, LookupFlags.Uncontained))
        {
            if (uid == body || MetaData(uid).EntityPrototype?.ID is not { } prototype)
                continue;

            var distance = (_xform.GetWorldPosition(uid) - centre.Position).Length();
            groups[prototype] = groups.TryGetValue(prototype, out var group)
                ? (group.Count + 1, Math.Min(group.Nearest, distance))
                : (1, distance);
        }

        var entities = new JsonArray();
        foreach (var (prototype, group) in groups.OrderBy(g => g.Value.Nearest).ThenBy(g => g.Key, StringComparer.Ordinal).Take(NearbyGroups))
        {
            entities.Add(new JsonObject
            {
                ["prototype"] = prototype,
                ["count"] = group.Count,
                ["nearest"] = Math.Round(group.Nearest, 1),
            });
        }

        return new JsonObject
        {
            ["range"] = NearbyRange,
            ["prototypes"] = groups.Count,
            ["entities"] = entities,
        };
    }

    private JsonNode ServerLogInfo()
    {
        var lines = new JsonArray();
        foreach (var entry in _buffer.Latest(ServerLogLines))
        {
            lines.Add(new JsonObject
            {
                ["time_utc"] = entry.Time.ToString("o"),
                ["level"] = entry.Level,
                ["sawmill"] = entry.Sawmill,
                ["message"] = Clean(entry.Message, 4000),
                ["repeats"] = entry.Repeats,
            });
        }

        return lines;
    }

    /// <summary>What the client said about itself, cut to size. None of it is checked.</summary>
    private static JsonNode ClientInfo(FeedbackSubmitEvent ev, bool screenshot)
    {
        var info = new JsonObject();
        foreach (var (key, value) in ev.ClientInfo.Take(FeedbackRules.MaxClientInfoEntries))
        {
            var name = Clean(key, FeedbackRules.MaxClientInfoKey, false);
            if (name.Length > 0)
                info[name] = Clean(value, FeedbackRules.MaxClientInfoValue, false);
        }

        var log = new JsonArray();
        foreach (var line in ev.ClientLog.Take(FeedbackRules.MaxClientLogLines))
        {
            log.Add(Clean(line, FeedbackRules.MaxClientLogLine));
        }

        return new JsonObject
        {
            ["trusted"] = false,
            ["info"] = info,
            ["log"] = log,
            ["screenshot"] = screenshot,
        };
    }

    private JsonObject Describe(EntityUid uid)
    {
        var meta = MetaData(uid);
        return new JsonObject
        {
            ["entity"] = GetNetEntity(uid, meta).Id,
            ["prototype"] = meta.EntityPrototype?.ID,
            ["name"] = meta.EntityName,
        };
    }

    private static JsonArray Point(Vector2 point)
    {
        return new JsonArray(Math.Round(point.X, 2), Math.Round(point.Y, 2));
    }
}
