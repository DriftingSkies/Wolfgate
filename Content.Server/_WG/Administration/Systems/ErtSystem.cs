using System.Linq;
using Content.Server._NF.CryoSleep;
using Content.Server._NF.SectorServices;
using Content.Server._WG.ShipAccess;
using Content.Server.Access.Systems;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Administration.Ert;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Administration;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Administration.Systems;

/// <summary>
/// Admin ERT Builder: spawns an optional ship and one ghost role per member aboard it, then prompts every ghost to
/// sign up. Each role builds its body on takeover, from the player's own character when its species is allowed.
/// </summary>
public sealed partial class ErtSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private AdminVesselSpawnSystem _vesselSpawn = default!;
    [Dependency] private GhostRoleSystem _ghostRoles = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private const string SpawnerPrototype = "WGErtSpawner";

    /// <summary>The ID card handed to a responder whose outfit has none; its own access is wiped.</summary>
    private static readonly EntProtoId FallbackIdPrototype = "PassengerIDCard";
    private const string IdSlot = "id";

    /// <summary>Jobs a responder's crew record starts from; the record then takes the slot's title.</summary>
    private static readonly ProtoId<JobPrototype> RecordJobLeader = "ERTLeader";
    private static readonly ProtoId<JobPrototype> RecordJobMember = "ERTSecurity";

    private static readonly SoundSpecifier CallSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    private sealed record ErtTeam(string Name, List<EntityUid> Slots);

    /// <summary>
    /// Teams with places still open, by id, so ghosts can sign up from their prompt.
    /// </summary>
    private readonly Dictionary<int, ErtTeam> _teams = new();
    private int _lastTeamId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ErtSpawnRequestEvent>(OnSpawnRequest);
        SubscribeNetworkEvent<ErtSignUpEvent>(OnSignUp);
        SubscribeLocalEvent<WolfgateErtSpawnerComponent, TakeGhostRoleEvent>(OnTakeRole);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _teams.Clear());
    }

    private void OnSpawnRequest(ErtSpawnRequestEvent ev, EntitySessionEventArgs args)
    {
        var admin = args.SenderSession;
        if (!_adminManager.HasAdminFlag(admin, AdminFlags.Spawn))
            return;

        var spawned = TrySpawnTeam(admin, ev.Config, out var message);
        RaiseNetworkEvent(new ErtSpawnResultEvent(message, !spawned), Filter.SinglePlayer(admin));
    }

    /// <summary>
    /// Validates the config, spawns the ship if one was picked, and places one ghost role per member aboard it.
    /// </summary>
    public bool TrySpawnTeam(ICommonSession admin, ErtConfig config, out string message)
    {
        var team = Clean(config.TeamName, ErtLimits.MaxTextLength);
        if (team.Length == 0)
        {
            message = Loc.GetString("wg-ert-error-no-name");
            return false;
        }

        if (config.Members is < 1 or > ErtLimits.MaxMembers)
        {
            message = Loc.GetString("wg-ert-error-members", ("max", ErtLimits.MaxMembers));
            return false;
        }

        var leaderOutfit = string.IsNullOrEmpty(config.LeaderOutfit) ? config.MemberOutfit : config.LeaderOutfit;
        foreach (var outfit in new[] { config.MemberOutfit, leaderOutfit })
        {
            if (!_prototypeManager.HasIndex<StartingGearPrototype>(outfit))
            {
                message = Loc.GetString("wg-ert-error-outfit", ("id", outfit));
                return false;
            }
        }

        if (admin.AttachedEntity is not { Valid: true } adminEntity)
        {
            message = Loc.GetString("wg-ert-error-no-entity");
            return false;
        }

        var adminXform = Transform(adminEntity);
        if (adminXform.MapID == MapId.Nullspace)
        {
            message = Loc.GetString("wg-ert-error-no-map");
            return false;
        }

        // Unknown or non-round-start species and unknown groups are dropped rather than failing the whole team.
        var species = config.Species
            .Where(id => _prototypeManager.TryIndex<SpeciesPrototype>(id, out var proto) && proto.RoundStart)
            .Distinct()
            .ToList();
        var groups = config.AccessGroups
            .Where(id => _prototypeManager.HasIndex<AccessGroupPrototype>(id))
            .Distinct()
            .ToList();

        var spawnAt = adminXform.Coordinates;
        var shipNote = string.Empty;
        EntityUid? ship = null;
        if (!string.IsNullOrEmpty(config.Vessel))
        {
            if (!_prototypeManager.TryIndex<VesselPrototype>(config.Vessel, out var vessel) || vessel.Abstract)
            {
                message = Loc.GetString("wg-ert-error-vessel", ("id", config.Vessel));
                return false;
            }

            if (!_vesselSpawn.TrySpawnVessel(vessel, adminXform.MapID, _transform.GetWorldPosition(adminXform), adminEntity, out var grid))
            {
                message = Loc.GetString("wg-ert-error-vessel-failed");
                return false;
            }

            _metaData.SetEntityName(grid.Value, $"{vessel.Name} ({team})");
            spawnAt = FindSpawnSpot(grid.Value) ?? spawnAt;
            shipNote = $" aboard {ToPrettyString(grid.Value)}";

            // The team's ship: each responder is registered to it as they take their place.
            _shipAccess.SetupRegisteredShip(grid.Value, team, Array.Empty<NetUserId>());
            ship = grid;
        }

        var briefing = Clean(config.Briefing, ErtLimits.MaxBriefingLength);
        if (briefing.Length == 0)
            briefing = Loc.GetString("wg-ert-default-briefing", ("team", team));

        var leaderTitle = Clean(config.LeaderTitle, ErtLimits.MaxTextLength);
        if (leaderTitle.Length == 0)
            leaderTitle = Loc.GetString("wg-ert-default-leader-title", ("team", team));

        var memberTitle = Clean(config.MemberTitle, ErtLimits.MaxTextLength);
        if (memberTitle.Length == 0)
            memberTitle = Loc.GetString("wg-ert-default-member-title", ("team", team));

        var teamId = ++_lastTeamId;
        var slots = new List<EntityUid>();
        for (var i = 0; i < config.Members; i++)
        {
            var leader = config.HasLeader && i == 0;
            slots.Add(SpawnSlot(spawnAt, new WolfgateErtSpawnerComponent
            {
                TeamId = teamId,
                Leader = leader,
                TeamName = team,
                Title = leader ? leaderTitle : memberTitle,
                Outfit = leader ? leaderOutfit : config.MemberOutfit,
                Species = species,
                GenericHumans = config.GenericHumans,
                AccessGroups = groups,
                KeepOutfitAccess = config.KeepOutfitAccess,
                Ship = ship,
            }, briefing));
        }

        _teams[teamId] = new ErtTeam(team, slots);
        var prompted = PromptGhosts(new ErtCalledEvent(teamId, team, briefing, config.Members, config.HasLeader));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{admin.Name} spawned ERT \"{team}\" with {config.Members} slots{shipNote}");

        message = Loc.GetString("wg-ert-spawned", ("team", team), ("count", config.Members), ("ghosts", prompted));
        return true;
    }

    /// <summary>
    /// Pops the sign-up prompt and a chime for every ghost. Returns how many were asked.
    /// </summary>
    private int PromptGhosts(ErtCalledEvent call)
    {
        var ghosts = Filter.Empty().AddWhere(session =>
            session.AttachedEntity is { } entity && HasComp<GhostComponent>(entity));

        RaiseNetworkEvent(call, ghosts);
        _audio.PlayGlobal(CallSound, ghosts, true);
        return ghosts.Recipients.Count();
    }

    /// <summary>
    /// Enters a ghost into the draw for the open slot with the fewest entrants, so sign-ups spread across places.
    /// </summary>
    private void OnSignUp(ErtSignUpEvent ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        string message;
        var isError = true;

        if (player.AttachedEntity is not { } ghost || !HasComp<GhostComponent>(ghost))
        {
            message = Loc.GetString("wg-ert-signup-not-ghost");
        }
        else if (!_teams.TryGetValue(ev.TeamId, out var team))
        {
            message = Loc.GetString("wg-ert-signup-closed");
        }
        else if (PickSlot(team, player, ev.Leader, out var alreadyJoined) is not { } slot)
        {
            message = Loc.GetString(alreadyJoined ? "wg-ert-signup-already"
                : ev.Leader ? "wg-ert-signup-no-leader" : "wg-ert-signup-full");
        }
        else
        {
            _ghostRoles.Request(player, slot.Comp.Identifier);
            message = Loc.GetString("wg-ert-signup-joined", ("title", Comp<WolfgateErtSpawnerComponent>(slot).Title));
            isError = false;
        }

        RaiseNetworkEvent(new ErtSignUpResultEvent(ev.TeamId, message, isError), Filter.SinglePlayer(player));
    }

    private Entity<GhostRoleComponent>? PickSlot(ErtTeam team, ICommonSession player, bool leader, out bool alreadyJoined)
    {
        alreadyJoined = false;
        Entity<GhostRoleComponent>? best = null;
        var fewest = int.MaxValue;

        foreach (var uid in team.Slots)
        {
            if (!TryComp<WolfgateErtSpawnerComponent>(uid, out var slot) || slot.Taken
                || !TryComp<GhostRoleComponent>(uid, out var role))
                continue;

            // The draw only exists once someone has joined.
            var entrants = TryComp<GhostRoleRaffleComponent>(uid, out var raffle) ? raffle.CurrentMembers : null;
            if (entrants != null && entrants.Contains(player))
            {
                alreadyJoined = true;
                return null;
            }

            if (slot.Leader != leader)
                continue;

            var count = entrants?.Count ?? 0;
            if (count >= fewest)
                continue;

            fewest = count;
            best = (uid, role);
        }

        return best;
    }

    /// <summary>
    /// Closes the team's prompts once every place is filled.
    /// </summary>
    private void CloseTeamIfFull(int teamId, EntityUid justTaken)
    {
        if (!_teams.TryGetValue(teamId, out var team))
            return;

        var open = team.Slots.Any(uid => uid != justTaken
                                          && !TerminatingOrDeleted(uid)
                                          && TryComp<WolfgateErtSpawnerComponent>(uid, out var slot)
                                          && !slot.Taken);
        if (open)
            return;

        _teams.Remove(teamId);
        RaiseNetworkEvent(new ErtClosedEvent(teamId));
    }

    /// <summary>
    /// Creates one ghost role marker. It's filled in before initialising so the role registers with the right text.
    /// </summary>
    private EntityUid SpawnSlot(EntityCoordinates at, WolfgateErtSpawnerComponent settings, string briefing)
    {
        var uid = EntityManager.CreateEntityUninitialized(SpawnerPrototype, at);

        var slot = Comp<WolfgateErtSpawnerComponent>(uid);
        slot.TeamId = settings.TeamId;
        slot.Leader = settings.Leader;
        slot.TeamName = settings.TeamName;
        slot.Title = settings.Title;
        slot.Outfit = settings.Outfit;
        slot.Species = settings.Species;
        slot.GenericHumans = settings.GenericHumans;
        slot.AccessGroups = settings.AccessGroups;
        slot.KeepOutfitAccess = settings.KeepOutfitAccess;
        slot.Ship = settings.Ship;

        var role = Comp<GhostRoleComponent>(uid);
        role.RoleName = settings.Title;
        role.RoleDescription = briefing;

        _metaData.SetEntityName(uid, settings.Title);
        EntityManager.InitializeAndStartEntity(uid);
        return uid;
    }

    /// <summary>
    /// Somewhere sensible aboard: a spawn point, then a cryopod, then the shuttle console.
    /// </summary>
    private EntityCoordinates? FindSpawnSpot(EntityUid grid)
    {
        return FindOnGrid<SpawnPointComponent>(grid)
               ?? FindOnGrid<CryoSleepComponent>(grid)
               ?? FindOnGrid<ShuttleConsoleComponent>(grid);
    }

    private EntityCoordinates? FindOnGrid<T>(EntityUid grid) where T : IComponent
    {
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return xform.Coordinates;
        }

        return null;
    }

    /// <summary>
    /// Builds the responder: the player's own character if its species is allowed, otherwise a random allowed one.
    /// </summary>
    private void OnTakeRole(Entity<WolfgateErtSpawnerComponent> slot, ref TakeGhostRoleEvent args)
    {
        if (slot.Comp.Taken || !TryComp<GhostRoleComponent>(slot, out var role))
        {
            args.TookRole = false;
            return;
        }

        slot.Comp.Taken = true;
        _ghostRoles.UnregisterGhostRole((slot, role));

        var profile = PickProfile(args.Player, slot.Comp);
        var mob = _stationSpawning.SpawnPlayerMob(Transform(slot).Coordinates, null, profile, null);
        _transform.AttachToGridOrMap(mob);

        SetOutfitCommand.SetOutfit(mob, slot.Comp.Outfit, EntityManager);
        var ship = slot.Comp.Ship is { } teamShip && !TerminatingOrDeleted(teamShip) ? slot.Comp.Ship : null;

        // A ship's doors and the chosen access both need a card to sit on.
        if (ship != null || slot.Comp.AccessGroups.Count > 0)
            EnsureId(mob);

        SetUpId(mob, slot.Comp);
        if (ship != null)
            GiveRecord(mob, profile, slot.Comp);

        EnsureComp<MindContainerComponent>(mob);
        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(args.Player, slot, mob, role);

        // Registered once the player is in the body, so the card it wears is keyed to the ship's doors at once.
        if (ship != null)
            _shipAccess.AddOwnerUser(ship.Value, args.Player.UserId);

        QueueDel(slot);
        args.TookRole = true;
        CloseTeamIfFull(slot.Comp.TeamId, slot);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{args.Player.Name} joined ERT \"{slot.Comp.TeamName}\" as {ToPrettyString(mob):mob} ({slot.Comp.Title})");
    }

    private HumanoidCharacterProfile PickProfile(ICommonSession player, WolfgateErtSpawnerComponent slot)
    {
        // Anonymous team: the stock bald male human, each with a random human name.
        if (slot.GenericHumans)
        {
            var generic = HumanoidCharacterProfile.DefaultWithSpecies(SharedHumanoidAppearanceSystem.DefaultSpecies);
            return generic.WithName(HumanoidCharacterProfile.GetName(SharedHumanoidAppearanceSystem.DefaultSpecies, generic.Gender));
        }

        var species = slot.Species;
        if (_preferences.GetPreferencesOrNull(player.UserId)?.SelectedCharacter is HumanoidCharacterProfile own
            && (species.Count == 0 || species.Contains(own.Species.Id)))
        {
            return own;
        }

        var pick = species.Count > 0 ? _random.Pick(species) : SharedHumanoidAppearanceSystem.DefaultSpecies;
        return HumanoidCharacterProfile.RandomWithSpecies(pick);
    }

    /// <summary>
    /// Gives a responder whose outfit has no ID card a plain one with no access, worn in the ID slot, since a ship's
    /// doors are only keyed to a worn card. A body that can't wear one gets it in hand, and the admin log says so.
    /// </summary>
    private void EnsureId(EntityUid mob)
    {
        if (_idCard.TryFindIdCard(mob, out _))
            return;

        var card = Spawn(FallbackIdPrototype, Transform(mob).Coordinates);
        _access.TrySetTags(card, Array.Empty<ProtoId<AccessLevelPrototype>>());
        if (TryWearId(mob, card))
            return;

        var held = _hands.TryPickupAnyHand(mob, card);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"ERT responder {ToPrettyString(mob):mob} could not wear {ToPrettyString(card):card} ({(held ? "in hand" : "left at their feet")}); no ship door is keyed to it until they do");
    }

    /// <summary>
    /// Puts a card in the ID slot: into the PDA worn there, else in place of whatever the outfit put there, which
    /// goes to a hand or the floor.
    /// </summary>
    private bool TryWearId(EntityUid mob, EntityUid card)
    {
        if (!_inventory.TryGetSlotEntity(mob, IdSlot, out var worn))
            return _inventory.TryEquip(mob, card, IdSlot, silent: true, force: true);

        if (TryComp<PdaComponent>(worn, out var pda) && _itemSlots.TryInsert(worn.Value, pda.IdSlot, card, null))
            return true;

        if (!_inventory.TryUnequip(mob, IdSlot, silent: true, force: true))
            return false;

        if (!_inventory.TryEquip(mob, card, IdSlot, silent: true, force: true))
        {
            _inventory.TryEquip(mob, worn.Value, IdSlot, silent: true, force: true);
            return false;
        }

        _hands.TryPickupAnyHand(mob, worn.Value);
        return true;
    }

    /// <summary>
    /// Puts the responder's name and team title on their ID, then applies the chosen access.
    /// </summary>
    private void SetUpId(EntityUid mob, WolfgateErtSpawnerComponent slot)
    {
        if (!_idCard.TryFindIdCard(mob, out var id))
            return;

        _idCard.TryChangeFullName(id, Name(mob), id.Comp);
        _idCard.TryChangeJobTitle(id, slot.Title, id.Comp);

        if (!slot.KeepOutfitAccess)
            _access.TrySetTags(id, Array.Empty<ProtoId<AccessLevelPrototype>>());

        if (slot.AccessGroups.Count > 0)
            _access.TryAddGroups(id, slot.AccessGroups.Select(group => new ProtoId<AccessGroupPrototype>(group)));
    }

    /// <summary>
    /// Gives a responder with a team ship the crew record a normal spawn gets, so their ID card carries a record key
    /// the ship's doors can be keyed to. A character that already has a record keeps it, and its job.
    /// </summary>
    private void GiveRecord(EntityUid mob, HumanoidCharacterProfile profile, WolfgateErtSpawnerComponent slot)
    {
        var service = _sectorService.GetServiceEntity();
        if (!TryComp<StationRecordsComponent>(service, out var records) || !_idCard.TryFindIdCard(mob, out var id))
            return;

        var name = Name(mob);
        var existing = _records.GetRecordByName(service, name, records) != null;
        TryComp<FingerprintComponent>(mob, out var fingerprint);
        TryComp<DnaComponent>(mob, out var dna);
        _records.CreateGeneralRecord(service, id, name, profile.Age, profile.Species, profile.Gender,
            slot.Leader ? RecordJobLeader : RecordJobMember, fingerprint?.Fingerprint, dna?.DNA, profile, records);

        if (existing
            || !TryComp<StationRecordKeyStorageComponent>(id, out var storage)
            || storage.Key is not { } key
            || !_records.TryGetRecord<GeneralStationRecord>(key, out var record))
            return;

        record.JobTitle = slot.Title;
        _records.Synchronize(key);
    }

    private static string Clean(string text, int maxLength)
    {
        var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}
