using System.Linq;
using System.Text.Json;
using Content.Server._Mono.Persistence;
using Content.Server.Administration.Commands;
using Content.Server.Medical.SuitSensors;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StatusIcon.Components;
using Content.Shared.Strip.Components;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.AdminObserver;

/// <summary>
/// The body behind a character avatar: one of the admin's saved characters as a real humanoid, dressed, carried
/// in a container on the observer and copied onto it by each client. Being a real humanoid, its markings, colours
/// and clothing are drawn by the code that draws anyone's. It has no mind, cannot be hurt and is kept paused, so
/// nothing that runs a living body runs it: it does not breathe, bleed, starve or sleep. This system also keeps
/// it out of what counts people: suit sensors, and the items saved at round end.
/// </summary>
public sealed partial class WGAvatarBodySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedGodmodeSystem _godmode = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;
    [Dependency] private SuitSensorSystem _sensors = default!;
    [Dependency] private WGAdminObserverKitSystem _kit = default!;
    [Dependency] private WGAdminObserverStore _store = default!;
    [Dependency] private WGManifestSystem _manifest = default!;

    /// <summary>The container on the observer that holds the body.</summary>
    public const string Slot = "wg_avatar_body";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAvatarBodyComponent, DidEquipEvent>(OnEquipped);
        SubscribeLocalEvent<WGAvatarBodyComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded, before: new[] { typeof(PersistAtRoundEndSystem) });
        SubscribeNetworkEvent<WGAvatarDressRequestEvent>((ev, args) => OpenDresser(args.SenderSession, ev.Preset));
        SubscribeNetworkEvent<WGAvatarSaveOutfitRequestEvent>((ev, args) => SaveOutfit(args.SenderSession, ev.Preset));
    }

    /// <summary>The saved character a preset shows: its slot, or the admin's selected character. Null if there is none.</summary>
    public HumanoidCharacterProfile? Profile(ICommonSession admin, WGAvatarPreset preset)
    {
        if (!_preferences.TryGetCachedPreferences(admin.UserId, out var preferences))
            return null;

        var slot = preset.CharacterSlot ?? preferences.SelectedCharacterIndex;
        return preferences.Characters.GetValueOrDefault(slot) as HumanoidCharacterProfile;
    }

    /// <summary>
    /// The body for a character preset, in the observer's container: the one it already carries if that was built
    /// from the same preset, else a new one in its place. Null if the admin has no such character.
    /// </summary>
    public EntityUid? Ensure(Entity<WGAdminObserverComponent> observer, ICommonSession admin, WGAvatarPreset preset)
    {
        if (preset.Kind != WGAvatarKind.Character || Profile(admin, preset) is not { } profile)
            return null;

        var built = Built(preset, profile);
        if (observer.Comp.AvatarBody is { } old && !TerminatingOrDeleted(old))
        {
            // An edited character is rebuilt; a bank balance moving changes nothing drawn.
            if (TryComp<WGAvatarBodyComponent>(old, out var carried)
                && carried.Built == built
                && carried.Profile is { } was
                && was.WithBankBalance(profile.BankBalance).MemberwiseEquals(profile))
            {
                return old;
            }

            // An observer shown as this body hides first, rather than be left drawn as nothing.
            if (TryComp<WGManifestedComponent>(observer.Owner, out var manifested) && manifested.Body == old)
                _manifest.Unmanifest(observer.Owner, effects: false);

            Del(old);
        }

        // Without the character's persistent items and components: those belong to the character played for real.
        var look = profile.WithPersistentData(profile.Flags, new List<PersistentProfileComponent>(), new List<PersistentProfileItem>());
        var dressed = preset.OutfitLayout != null || preset.Outfit != null;
        var body = _spawning.SpawnPlayerMob(Transform(observer).Coordinates, dressed ? null : DefaultJob(profile), look, null);

        if (preset.OutfitLayout != null)
            _kit.Restore(body, preset.OutfitLayout);
        else if (preset.Outfit != null)
            SetOutfitCommand.SetOutfit(body, preset.Outfit, EntityManager);

        var comp = EnsureComp<WGAvatarBodyComponent>(body);
        comp.Observer = observer;
        comp.Built = built;
        comp.Profile = profile;
        _godmode.EnableGodmode(body);

        // Ghosts see status icons through containers, so the body's SSD and job icons would be drawn on the observer.
        RemComp<StatusIconComponent>(body);

        var worn = _inventory.GetSlotEnumerator(body);
        while (worn.NextItem(out var item))
        {
            Silence(item);
        }

        _containers.Insert(body, _containers.EnsureContainer<ContainerSlot>(observer, Slot));
        _metaData.SetEntityPaused(body, true);
        observer.Comp.AvatarBody = body;
        return body;
    }

    /// <summary>
    /// Moving to another map wakes everything carried along. A body left awake would breathe, bleed and starve, so
    /// it goes straight back to sleep.
    /// </summary>
    private void OnUnpaused(Entity<WGAvatarBodyComponent> ent, ref EntityUnpausedEvent args)
    {
        if (!TerminatingOrDeleted(ent))
            _metaData.SetEntityPaused(ent, true);
    }

    /// <summary>Opens the strip window of a character preset's body for its admin, to dress it by hand.</summary>
    public bool OpenDresser(ICommonSession admin, string presetName)
    {
        if (!TryGetBody(admin, presetName, out var observer, out var body))
            return false;

        _ui.OpenUi(body, StrippingUiKey.Key, observer.Owner);
        return true;
    }

    /// <summary>Saves what a character preset's body wears into the preset, under the kit's save rules.</summary>
    public bool SaveOutfit(ICommonSession admin, string presetName)
    {
        if (!TryGetBody(admin, presetName, out var observer, out var body))
            return false;

        var layout = _kit.Snapshot(body, observer.Comp.SavesAnything);
        var saved = _store.Change(admin, data =>
        {
            if (data.Presets.FirstOrDefault(p => p.Name == presetName) is { } preset)
                preset.OutfitLayout = layout;
        });

        if (!saved)
            return false;

        // The body already wears what was saved, so it is not rebuilt for it.
        if (_store.Get(admin)?.Presets.FirstOrDefault(p => p.Name == presetName) is { } now
            && Profile(admin, now) is { } profile)
        {
            Comp<WGAvatarBodyComponent>(body).Built = Built(now, profile);
        }

        _popup.PopupCursor(Loc.GetString("wg-observer-preset-outfit-saved"), admin);
        return true;
    }

    private bool TryGetBody(ICommonSession admin, string presetName, out Entity<WGAdminObserverComponent> observer, out EntityUid body)
    {
        body = default;
        if (!_kit.TryGetObserver(admin, out observer)
            || _store.Get(admin)?.Presets.FirstOrDefault(p => p.Name == presetName) is not { } preset
            || Ensure(observer, admin, preset) is not { } found)
        {
            return false;
        }

        body = found;
        return true;
    }

    /// <summary>
    /// The job whose loadout dresses a character with no outfit chosen: the one it most wants that spawns a
    /// humanoid, else the job everyone can take.
    /// </summary>
    private ProtoId<JobPrototype>? DefaultJob(HumanoidCharacterProfile profile)
    {
        foreach (var (job, _) in profile.JobPriorities.Where(p => p.Value != JobPriority.Never).OrderByDescending(p => p.Value))
        {
            if (_prototypes.TryIndex(job, out var prototype) && prototype.JobEntity == null)
                return job;
        }

        if (_prototypes.HasIndex(SharedGameTicker.FallbackOverflowJob))
            return SharedGameTicker.FallbackOverflowJob;

        return null;
    }

    private static string Built(WGAvatarPreset preset, HumanoidCharacterProfile profile)
    {
        return $"{preset.Name}|{profile.Name}|{preset.CharacterSlot}|{preset.Outfit}|{JsonSerializer.Serialize(preset.OutfitLayout)}";
    }

    private void OnEquipped(Entity<WGAvatarBodyComponent> ent, ref DidEquipEvent args)
    {
        Silence(args.Equipment);
    }

    /// <summary>Switches off an item's suit sensors, which would put the body on crew monitors as a crew member.</summary>
    private void Silence(EntityUid item)
    {
        if (TryComp<SuitSensorComponent>(item, out var sensor))
            _sensors.SetSensor((item, sensor), SuitSensorMode.SensorOff);
    }

    private void OnRoundEnded(RoundEndedEvent args)
    {
        RemoveBodies();
    }

    /// <summary>
    /// Deletes every avatar body and hides the observers shown as one. Done at round end, before the round's
    /// persistent items are saved: what a body wears hangs under the admin's own entity, where that save would
    /// take it for theirs.
    /// </summary>
    public void RemoveBodies()
    {
        var bodies = new List<Entity<WGAvatarBodyComponent>>();
        var query = AllEntityQuery<WGAvatarBodyComponent>();
        while (query.MoveNext(out var uid, out var body))
        {
            bodies.Add((uid, body));
        }

        foreach (var body in bodies)
        {
            if (TryComp<WGManifestedComponent>(body.Comp.Observer, out var manifested) && manifested.Body == body.Owner)
                _manifest.Unmanifest(body.Comp.Observer);

            Del(body);
        }
    }
}
