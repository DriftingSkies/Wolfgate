using System.Numerics;
using Content.Server._WG.Remade.Corruption;
using Content.Server.Light.EntitySystems;
using Content.Server.Station.Components;
using Content.Shared._WG.Announcements;
using Content.Shared._WG.Lattice;
using Content.Shared.Damage;
using Content.Shared.Construction.Components;
using Content.Shared.Destructible;
using Content.Shared.Popups;
using Content.Shared.Light.Components;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WG.Lattice;

/// <summary>
/// The end of the Lattice. When the last primary anchor falls the Unstringing comes: no anchor holds anything anywhere and
/// the Coda is everywhere. Some time later Kyphrus implodes (Tacet): every bulb in the sector bursts, and new ones keep
/// failing. Also places Caelestinus Central's own anchor, which keeps corruption off the station while it stands.
/// </summary>
public sealed partial class WGLatticeSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private PoweredLightSystem _lights = default!;
    [Dependency] private Content.Server.Lightning.LightningSystem _lightning = default!;
    [Dependency] private WGCorruptionSystem _corruption = default!;

    private const string CentralPrototype = "WGLatticeCentralAnchor";
    private static readonly Color EndColor = Color.FromHex("#8a1a5a");
    private static readonly SoundSpecifier UnstringSound = new SoundPathSpecifier("/Audio/Misc/delta_alt.ogg");
    private static readonly SoundSpecifier TacetSound = new SoundPathSpecifier("/Audio/_WG/Lattice/drift_pulse.ogg");

    /// <summary>Maps whose central anchor has been placed or found.</summary>
    private readonly HashSet<EntityUid> _central = new();

    /// <summary>When each unstrung map goes to Tacet, and after it, when its bulbs are next rolled.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _tacetAt = new();

    /// <summary>Unstrung maps whose centre the Coda has reached, so Tacet is close.</summary>
    private readonly HashSet<EntityUid> _codaAtCentre = new();

    /// <summary>An anchor's hold letting go: where it was, its signed strength, and when it started fading.</summary>
    private readonly record struct Fading(Vector2 Position, float Strength, TimeSpan Start);

    /// <summary>Holds still fading on each map.</summary>
    private readonly Dictionary<EntityUid, List<Fading>> _fading = new();

    /// <summary>Starts an anchor's hold fading out where it stood, unless fading is off.</summary>
    private void StartFade(EntityUid map, Vector2 position, float strength)
    {
        if (_config.GetCVar(WGLatticeEndgameCVars.AnchorFade) <= 0f)
            return;

        if (!_fading.TryGetValue(map, out var list))
            _fading[map] = list = new List<Fading>();

        list.Add(new Fading(position, strength, _timing.CurTime));
    }

    /// <summary>Adds what is still fading to the gathered anchors, weaker the longer it has faded, and forgets what is gone.</summary>
    private void AddFading(EntityUid map)
    {
        if (!_fading.TryGetValue(map, out var list))
            return;

        var fade = _config.GetCVar(WGLatticeEndgameCVars.AnchorFade);
        var now = _timing.CurTime;
        list.RemoveAll(f => fade <= 0f || (now - f.Start).TotalSeconds >= fade);
        foreach (var f in list)
        {
            var left = 1f - (float) (now - f.Start).TotalSeconds / fade;
            _anchors.Add((f.Position, f.Strength * left));
        }
    }

    private void InitializeEndgame()
    {
        SubscribeLocalEvent<WGLatticeBreachEvent>(OnBreach);
        SubscribeLocalEvent<WGLatticeCentralAnchorComponent, DestructionEventArgs>(OnCentralDestroyed);
        SubscribeLocalEvent<WGLatticeCentralAnchorComponent, DamageChangedEvent>(OnCentralDamaged);
        SubscribeLocalEvent<WGLatticeCentralAnchorComponent, UnanchorAttemptEvent>(OnCentralTamper);
    }

    /// <summary>When Central's anchor may next name someone tampering with it.</summary>
    private TimeSpan _nextTamperAlert;

    /// <summary>Central's anchor won't come loose: whoever tries is struck by lightning and named to the sector.</summary>
    private void OnCentralTamper(Entity<WGLatticeCentralAnchorComponent> ent, ref UnanchorAttemptEvent args)
    {
        args.Cancel();
        _lightning.ShootLightning(ent, args.User);
        _popup.PopupEntity(Loc.GetString("wg-lattice-central-tamper-popup"), args.User, args.User, PopupType.LargeCaution);

        var now = _timing.CurTime;
        if (now < _nextTamperAlert)
            return;

        _nextTamperAlert = now + CentralAlertGap;
        var text = Loc.GetString("wg-lattice-central-tamper", ("attacker", Name(args.User)));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wg-lattice-chip-sender"), true, BreachSound, EndColor);
        RaiseNetworkEvent(new WGAnnouncementChipEvent
        {
            Level = WGAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString("wg-lattice-central-struck-title"),
            Text = text,
            Sender = Loc.GetString("wg-lattice-chip-sender"),
            Duration = 20f,
            Flash = true,
        });
    }

    /// <summary>When Central's anchor may next call out an attack on it.</summary>
    private TimeSpan _nextCentralAlert;

    private static readonly TimeSpan CentralAlertGap = TimeSpan.FromSeconds(30);

    /// <summary>Anyone hurting Central's anchor is named to the sector.</summary>
    private void OnCentralDamaged(Entity<WGLatticeCentralAnchorComponent> ent, ref DamageChangedEvent args)
    {
        var now = _timing.CurTime;
        if (!args.DamageIncreased || now < _nextCentralAlert)
            return;

        _nextCentralAlert = now + CentralAlertGap;
        var attacker = Loc.GetString("wg-lattice-defence-unknown");
        if (args.Origin is { } origin && Exists(origin))
        {
            var who = _crew.Wielder(origin);
            attacker = HasComp<Content.Shared.Mobs.Components.MobStateComponent>(who)
                ? Name(who)
                : Transform(who).GridUid is { } grid ? Name(grid) : Name(who);
        }

        var text = Loc.GetString("wg-lattice-central-struck", ("attacker", attacker));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wg-lattice-chip-sender"), true, BreachSound, EndColor);
        RaiseNetworkEvent(new WGAnnouncementChipEvent
        {
            Level = WGAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString("wg-lattice-central-struck-title"),
            Text = text,
            Sender = Loc.GetString("wg-lattice-chip-sender"),
            Duration = 20f,
            Flash = true,
        });
    }

    private void OnBreach(ref WGLatticeBreachEvent args)
    {
        if (Transform(args.Anchor).MapUid is not { } map || !TryComp<WGLatticeFieldComponent>(map, out var field))
            return;

        // The bands don't snap in: the lost anchor's hold fades where it stood.
        if (TryComp<WGLatticeAnchorComponent>(args.Anchor, out var lost))
            StartFade(map, _transform.GetWorldPosition(args.Anchor), lost.Strength);

        if (field.Unstrung)
            return;

        var query = EntityQueryEnumerator<WGLatticeAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (anchor.Primary && anchor.Active && xform.MapUid == map && !TerminatingOrDeleted(uid))
                return;
        }

        Unstring((map, field));
    }

    /// <summary>The Unstringing: nothing holds the map any more. Starts the clock on Tacet.</summary>
    public void Unstring(Entity<WGLatticeFieldComponent> map)
    {
        if (map.Comp.Unstrung)
            return;

        // Everything else that still holds lets go too, slowly, so the Coda engulfs the sector rather than appearing.
        // Mobile relays don't: they are the last havens.
        var holding = EntityQueryEnumerator<WGLatticeAnchorComponent, TransformComponent>();
        while (holding.MoveNext(out var holder, out var anchor, out var anchorXform))
        {
            if (anchorXform.MapUid == map.Owner && anchor.Active && !IsMobileRelay(holder))
                StartFade(map, _transform.GetWorldPosition(anchorXform), anchor.Primary ? anchor.Strength : -anchor.Strength);
        }

        _unstrungAt[map] = _timing.CurTime;
        map.Comp.Unstrung = true;
        Dirty(map);
        GatherAnchors(map);
        SyncAnchors(map);
        SyncHex(map);
        Announce("wg-lattice-unstring", "wg-lattice-unstring-title", "wg-lattice-unstring-chip", UnstringSound);
        Log.Info($"The Lattice of {ToPrettyString(map)} is unstrung.");

        if (_config.GetCVar(WGLatticeEndgameCVars.Tacet))
        {
            var min = _config.GetCVar(WGLatticeEndgameCVars.TacetMin);
            var max = Math.Max(min, _config.GetCVar(WGLatticeEndgameCVars.TacetMax));
            // Once the Coda has had the whole sector, then some while after.
            var fade = Math.Max(0f, _config.GetCVar(WGLatticeEndgameCVars.AnchorFade));
            _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(fade + _random.NextFloat(min, max));
        }
    }

    /// <summary>Tacet: Kyphrus begins to implode. The burst, the shockwaves and the dark follow (Implosion).</summary>
    public void Tacet(Entity<WGLatticeFieldComponent> map)
    {
        if (map.Comp.Tacet)
            return;

        if (!map.Comp.Unstrung)
            Unstring(map);

        map.Comp.Tacet = true;
        map.Comp.TacetStart = _timing.CurTime;
        Dirty(map);
        // The bulbs keep failing only once the last shockwave has passed.
        _tacetAt.Remove(map);
        StartImplosion(map);
        Log.Info($"Tacet on {ToPrettyString(map)}.");
    }

    private void UpdateEndgame(Entity<WGLatticeFieldComponent> map)
    {
        if (!map.Comp.Unstrung)
            return;

        if (!map.Comp.Tacet)
            WatchCentre(map);

        if (!_tacetAt.TryGetValue(map, out var at) || _timing.CurTime < at)
            return;

        if (!map.Comp.Tacet)
        {
            Tacet(map);
            return;
        }

        _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(_config.GetCVar(WGLatticeEndgameCVars.DarkInterval));
        BurstBulbs(map, _config.GetCVar(WGLatticeEndgameCVars.DarkChance));
    }

    /// <summary>Once the Coda reaches the centre of an unstrung map, Kyphrus implodes shortly after.</summary>
    private void WatchCentre(Entity<WGLatticeFieldComponent> map)
    {
        if (_codaAtCentre.Contains(map)
            || !_config.GetCVar(WGLatticeEndgameCVars.Tacet)
            || !_proto.TryIndex(map.Comp.Profile, out var profile)
            || Locate(map, Vector2.Zero).Band < profile.HazardBand)
            return;

        _codaAtCentre.Add(map);
        var at = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, _config.GetCVar(WGLatticeEndgameCVars.TacetCentreDelay)));
        if (!_tacetAt.TryGetValue(map, out var planned) || at < planned)
            _tacetAt[map] = at;

        Log.Info($"The Coda has reached the centre of {ToPrettyString(map)}.");
    }

    /// <summary>Bursts each working bulb on the map with the given chance, except aboard a mobile anchor's haven.</summary>
    private void BurstBulbs(EntityUid map, float chance)
    {
        _havens.Clear();
        var relays = EntityQueryEnumerator<WGLatticeRelayComponent, WGLatticeAnchorComponent, TransformComponent>();
        while (relays.MoveNext(out var relay, out var anchor, out var relayXform))
        {
            if (relay.NeedsFuel && anchor.Active && relayXform.GridUid is { } grid)
                _havens.Add(grid);
        }

        var query = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.MapUid == map && !(xform.GridUid is { } grid && _havens.Contains(grid)) && _random.Prob(chance))
                _lights.TryDestroyBulb(uid, light);
        }
    }

    private readonly HashSet<EntityUid> _havens = new();

    private void Announce(string text, string title, string chip, SoundSpecifier sound)
    {
        _chat.DispatchGlobalAnnouncement(Loc.GetString(text), Loc.GetString("wg-lattice-chip-sender"), true, sound, EndColor);
        RaiseNetworkEvent(new WGAnnouncementChipEvent
        {
            Level = WGAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString(title),
            Text = Loc.GetString(chip),
            Sender = Loc.GetString("wg-lattice-chip-sender"),
            Duration = 30f,
            Flash = true,
        });
    }

    /// <summary>Where Central's anchor and status console stand on the station, as tile indices, and which way the console faces.</summary>
    private static readonly Vector2i CentralAnchorTile = new(1, 17);
    private static readonly Vector2i CentralConsoleTile = new(-19, -6);
    private static readonly Direction CentralConsoleFacing = Direction.East;
    private const string CentralConsolePrototype = "WGLatticeStatusConsole";

    /// <summary>The railing that boxes the anchor in: a round one on three sides and a straight one turned to close the fourth.</summary>
    private const string RailingRound = "RailingRound";
    private const string Railing = "Railing";

    /// <summary>
    /// Puts Caelestinus Central's anchor, railed in, and its Lattice status console on the station once it exists, unless an
    /// anchor is there already.
    /// </summary>
    private void PlaceCentral(EntityUid map)
    {
        if (_central.Contains(map))
            return;

        var stations = EntityQueryEnumerator<BecomesStationComponent, MapGridComponent, TransformComponent>();
        while (stations.MoveNext(out var grid, out _, out var gridComp, out var xform))
        {
            if (xform.MapUid != map || !_corruption.IsMainStation(grid))
                continue;

            _central.Add(map);
            var existing = EntityQueryEnumerator<WGLatticeCentralAnchorComponent, TransformComponent>();
            while (existing.MoveNext(out _, out var anchorXform))
            {
                if (anchorXform.GridUid == grid)
                    return;
            }

            var anchorAt = _maps.GridTileToLocal(grid, gridComp, CentralAnchorTile);
            Spawn(CentralPrototype, anchorAt);
            Spawn(RailingRound, anchorAt);
            var closing = Spawn(Railing, anchorAt);
            _transform.SetLocalRotation(closing, Direction.North.ToAngle());

            var console = Spawn(CentralConsolePrototype, _maps.GridTileToLocal(grid, gridComp, CentralConsoleTile));
            _transform.SetLocalRotation(console, CentralConsoleFacing.ToAngle());
            return;
        }
    }

    private void OnCentralDestroyed(Entity<WGLatticeCentralAnchorComponent> ent, ref DestructionEventArgs args)
    {
        Announce("wg-lattice-central-lost", "wg-lattice-central-lost-title", "wg-lattice-central-lost-chip", BreachAlarm);
    }
}
