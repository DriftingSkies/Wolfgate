using Content.Server.Ghost.Roles.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Clothing;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.NameIdentifier;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Reopens the ghost role of an AI core whose player ghosted, and names a player who takes one after the cyborg
/// name in their character's loadout, since the Station AI job and its loadout are hidden from the editor.
/// </summary>
public sealed class WGAiCoreGhostRoleSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private static readonly string AiLoadout = LoadoutSystem.GetJobPrototype("Borg");

    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationAiHeldComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(EntityUid uid, StationAiHeldComponent component, MindAddedMessage args)
    {
        // Only a player taking the ghost role; an AI coming back from a borg finds the role already gone.
        if (!HasComp<GhostRoleComponent>(uid) || args.Mind.Comp.UserId is not { } userId)
            return;

        if (_prefs.GetPreferencesOrNull(userId)?.SelectedCharacter is not HumanoidCharacterProfile profile
            || !profile.Loadouts.TryGetValue(AiLoadout, out var loadout)
            || string.IsNullOrWhiteSpace(loadout.EntityName)
            || !_proto.TryIndex<RoleLoadoutPrototype>(AiLoadout, out var role)
            || !role.CanCustomizeName)
            return;

        var name = loadout.EntityName.Trim();
        if (TryComp<NameIdentifierComponent>(uid, out var identifier) && !string.IsNullOrEmpty(identifier.FullIdentifier))
            name = $"{name} {identifier.FullIdentifier}";

        _metaData.SetEntityName(uid, name);
        args.Mind.Comp.CharacterName = name;
        Dirty(args.Mind);

        // The core copies the brain's name only when the brain goes in.
        if (_stationAi.TryGetCore(uid, out var core))
            _metaData.SetEntityName(core.Owner, name);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var abandoned = new List<EntityUid>();
        var query = EntityQueryEnumerator<StationAiHeldComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!HasComp<GhostRoleComponent>(uid)
                && !_mind.TryGetMind(uid, out _, out _)
                && _stationAi.TryGetCore(uid, out _)
                && !IsInBorg(uid))
                abandoned.Add(uid);
        }

        foreach (var uid in abandoned)
            Reopen(uid);
    }

    /// <summary>Whether this AI's mind is out controlling a borg.</summary>
    private bool IsInBorg(EntityUid ai)
    {
        var query = EntityQueryEnumerator<AiRemoteControllerComponent>();
        while (query.MoveNext(out var borg, out var remote))
        {
            if (remote.AiHolder == ai
                && remote.LinkedMind is { } linked
                && _mind.TryGetMind(borg, out var mind, out _)
                && mind == linked)
                return true;
        }

        return false;
    }

    /// <summary>Puts the brain's ghost role back from its prototype. A brain spawned without one is left alone.</summary>
    private void Reopen(EntityUid uid)
    {
        if (Prototype(uid) is not { } proto)
            return;

        var roleName = _factory.GetComponentName<GhostRoleComponent>();
        var takeoverName = _factory.GetComponentName<GhostTakeoverAvailableComponent>();
        if (!proto.Components.TryGetValue(roleName, out var role)
            || !proto.Components.TryGetValue(takeoverName, out var takeover))
            return;

        // A borg the old AI ghosted out of would otherwise hand that player's mind back to the core on shutdown.
        var remotes = EntityQueryEnumerator<AiRemoteControllerComponent>();
        while (remotes.MoveNext(out _, out var remote))
        {
            if (remote.AiHolder != uid)
                continue;

            remote.AiHolder = null;
            remote.LinkedMind = null;
        }

        EntityManager.AddComponents(uid, new ComponentRegistry
        {
            [takeoverName] = takeover,
            [roleName] = role,
        });

        Log.Info($"Reopened the ghost role of abandoned AI {ToPrettyString(uid)}");
    }
}
