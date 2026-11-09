using System.Numerics;
using Content.Server._WF.Remade.Corruption;
using Content.Server.Light.EntitySystems;
using Content.Server.Station.Components;
using Content.Shared._WF.Announcements;
using Content.Shared._WF.Lattice;
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

namespace Content.Server._WF.Lattice;

/// <summary>
/// The end of the Lattice. When the last primary anchor falls the Unstringing comes: no anchor holds anything anywhere and
/// the Coda is everywhere. Some time later Kyphrus implodes (Tacet): every bulb in the sector bursts, and new ones keep
/// failing. Also places Caelestinus Central's own anchor, which keeps corruption off the station while it stands.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private PoweredLightSystem _lights = default!;
    [Dependency] private Content.Server.Lightning.LightningSystem _lightning = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;

    private const string CentralPrototype = "WFLatticeCentralAnchor";
    private static readonly Color EndColor = Color.FromHex("#8a1a5a");
    private static readonly SoundSpecifier UnstringSound = new SoundPathSpecifier("/Audio/Misc/delta_alt.ogg");
    private static readonly SoundSpecifier TacetSound = new SoundPathSpecifier("/Audio/_WF/Lattice/drift_pulse.ogg");

    /// <summary>Maps whose central anchor has been placed or found.</summary>
    private readonly HashSet<EntityUid> _central = new();

    /// <summary>When each unstrung map goes to Tacet, and after it, when its bulbs are next rolled.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _tacetAt = new();

    /// <summary>An anchor's hold letting go: where it was, its signed strength, and when it started fading.</summary>
    private readonly record struct Fading(Vector2 Position, float Strength, TimeSpan Start);

    /// <summary>Holds still fading on each map.</summary>
    private readonly Dictionary<EntityUid, List<Fading>> _fading = new();

    /// <summary>Starts an anchor's hold fading out where it stood, unless fading is off.</summary>
    private void StartFade(EntityUid map, Vector2 position, float strength)
    {
        if (_config.GetCVar(WFLatticeEndgameCVars.AnchorFade) <= 0f)
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

        var fade = _config.GetCVar(WFLatticeEndgameCVars.AnchorFade);
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
        SubscribeLocalEvent<WFLatticeBreachEvent>(OnBreach);
        SubscribeLocalEvent<WFLatticeCentralAnchorComponent, DestructionEventArgs>(OnCentralDestroyed);
        SubscribeLocalEvent<WFLatticeCentralAnchorComponent, DamageChangedEvent>(OnCentralDamaged);
        SubscribeLocalEvent<WFLatticeCentralAnchorComponent, UnanchorAttemptEvent>(OnCentralTamper);
    }

    /// <summary>When Central's anchor may next name someone tampering with it.</summary>
    private TimeSpan _nextTamperAlert;

    /// <summary>Central's anchor won't come loose: whoever tries is struck by lightning and named to the sector.</summary>
    private void OnCentralTamper(Entity<WFLatticeCentralAnchorComponent> ent, ref UnanchorAttemptEvent args)
    {
        args.Cancel();
        _lightning.ShootLightning(ent, args.User);
        _popup.PopupEntity(Loc.GetString("wf-lattice-central-tamper-popup"), args.User, args.User, PopupType.LargeCaution);

        var now = _timing.CurTime;
        if (now < _nextTamperAlert)
            return;

        _nextTamperAlert = now + CentralAlertGap;
        var text = Loc.GetString("wf-lattice-central-tamper", ("attacker", Name(args.User)));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wf-lattice-chip-sender"), true, BreachSound, EndColor);
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString("wf-lattice-central-struck-title"),
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 20f,
            Flash = true,
        });
    }

    /// <summary>When Central's anchor may next call out an attack on it.</summary>
    private TimeSpan _nextCentralAlert;

    private static readonly TimeSpan CentralAlertGap = TimeSpan.FromSeconds(30);

    /// <summary>Anyone hurting Central's anchor is named to the sector.</summary>
    private void OnCentralDamaged(Entity<WFLatticeCentralAnchorComponent> ent, ref DamageChangedEvent args)
    {
        var now = _timing.CurTime;
        if (!args.DamageIncreased || now < _nextCentralAlert)
            return;

        _nextCentralAlert = now + CentralAlertGap;
        var attacker = Loc.GetString("wf-lattice-defence-unknown");
        if (args.Origin is { } origin && Exists(origin))
        {
            var who = _crew.Wielder(origin);
            attacker = HasComp<Content.Shared.Mobs.Components.MobStateComponent>(who)
                ? Name(who)
                : Transform(who).GridUid is { } grid ? Name(grid) : Name(who);
        }

        var text = Loc.GetString("wf-lattice-central-struck", ("attacker", attacker));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wf-lattice-chip-sender"), true, BreachSound, EndColor);
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString("wf-lattice-central-struck-title"),
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 20f,
            Flash = true,
        });
    }

    private void OnBreach(ref WFLatticeBreachEvent args)
    {
        if (Transform(args.Anchor).MapUid is not { } map || !TryComp<WFLatticeFieldComponent>(map, out var field))
            return;

        // The bands don't snap in: the lost anchor's hold fades where it stood.
        if (TryComp<WFLatticeAnchorComponent>(args.Anchor, out var lost))
            StartFade(map, _transform.GetWorldPosition(args.Anchor), lost.Strength);

        if (field.Unstrung)
            return;

        var query = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (anchor.Primary && anchor.Active && xform.MapUid == map && !TerminatingOrDeleted(uid))
                return;
        }

        Unstring((map, field));
    }

    /// <summary>The Unstringing: nothing holds the map any more. Starts the clock on Tacet.</summary>
    public void Unstring(Entity<WFLatticeFieldComponent> map)
    {
        if (map.Comp.Unstrung)
            return;

        // Everything else that still holds lets go too, slowly, so the Coda engulfs the sector rather than appearing.
        var holding = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        while (holding.MoveNext(out _, out var anchor, out var anchorXform))
        {
            if (anchorXform.MapUid == map.Owner && anchor.Active)
                StartFade(map, _transform.GetWorldPosition(anchorXform), anchor.Primary ? anchor.Strength : -anchor.Strength);
        }

        map.Comp.Unstrung = true;
        Dirty(map);
        GatherAnchors(map);
        SyncAnchors(map);
        SyncHex(map);
        Announce("wf-lattice-unstring", "wf-lattice-unstring-title", "wf-lattice-unstring-chip", UnstringSound);
        Log.Info($"The Lattice of {ToPrettyString(map)} is unstrung.");

        if (_config.GetCVar(WFLatticeEndgameCVars.Tacet))
        {
            var min = _config.GetCVar(WFLatticeEndgameCVars.TacetMin);
            var max = Math.Max(min, _config.GetCVar(WFLatticeEndgameCVars.TacetMax));
            // Once the Coda has had the whole sector, then some while after.
            var fade = Math.Max(0f, _config.GetCVar(WFLatticeEndgameCVars.AnchorFade));
            _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(fade + _random.NextFloat(min, max));
        }
    }

    /// <summary>Tacet: Kyphrus implodes and the sector goes dark.</summary>
    public void Tacet(Entity<WFLatticeFieldComponent> map)
    {
        if (map.Comp.Tacet)
            return;

        if (!map.Comp.Unstrung)
            Unstring(map);

        map.Comp.Tacet = true;
        Dirty(map);
        Announce("wf-lattice-tacet", "wf-lattice-tacet-title", "wf-lattice-tacet-chip", TacetSound);
        BurstBulbs(map, 1f);
        _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(_config.GetCVar(WFLatticeEndgameCVars.DarkInterval));
        Log.Info($"Tacet on {ToPrettyString(map)}.");
    }

    private void UpdateEndgame(Entity<WFLatticeFieldComponent> map)
    {
        if (!map.Comp.Unstrung || !_tacetAt.TryGetValue(map, out var at) || _timing.CurTime < at)
            return;

        if (!map.Comp.Tacet)
        {
            Tacet(map);
            return;
        }

        _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(_config.GetCVar(WFLatticeEndgameCVars.DarkInterval));
        BurstBulbs(map, _config.GetCVar(WFLatticeEndgameCVars.DarkChance));
    }

    /// <summary>Bursts each working bulb on the map with the given chance.</summary>
    private void BurstBulbs(EntityUid map, float chance)
    {
        var query = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.MapUid == map && _random.Prob(chance))
                _lights.TryDestroyBulb(uid, light);
        }
    }

    private void Announce(string text, string title, string chip, SoundSpecifier sound)
    {
        _chat.DispatchGlobalAnnouncement(Loc.GetString(text), Loc.GetString("wf-lattice-chip-sender"), true, sound, EndColor);
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString(title),
            Text = Loc.GetString(chip),
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 30f,
            Flash = true,
        });
    }

    /// <summary>Where Central's anchor and status console stand on the station, as tile indices, and which way the console faces.</summary>
    private static readonly Vector2i CentralAnchorTile = new(1, 17);
    private static readonly Vector2i CentralConsoleTile = new(-19, -6);
    private static readonly Direction CentralConsoleFacing = Direction.East;
    private const string CentralConsolePrototype = "WFLatticeStatusConsole";

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
            var existing = EntityQueryEnumerator<WFLatticeCentralAnchorComponent, TransformComponent>();
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

    private void OnCentralDestroyed(Entity<WFLatticeCentralAnchorComponent> ent, ref DestructionEventArgs args)
    {
        Announce("wf-lattice-central-lost", "wf-lattice-central-lost-title", "wf-lattice-central-lost-chip", BreachAlarm);
    }
}
