using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipCommand;
using Content.Server.Chat.Systems;
using Content.Shared._WG.ShipCommand;
using Content.Shared._WG.Traders;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.CrewBarks;

/// <summary>
/// Mother Base manners for a commanded ship's roster crew and counter staff: they thank the Boss for his blows, greet
/// people by their standing with the ship, think aloud and react to what happens aboard. Each line is a random one
/// of the speaker's voice and the common lines of its kind, said aloud, within a gap per speaker and per area.
/// </summary>
public sealed partial class WGCrewBarkSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewSecuritySystem _security = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;
    [Dependency] private WGShipCommandSystem _command = default!;

    /// <summary>The voice whose lines every speaker shares.</summary>
    public const string Common = "common";

    private const string Prefix = "wg-crew-bark-";

    /// <summary>Voices of roles whose name doesn't make a good one.</summary>
    private static readonly Dictionary<string, string> RoleVoices = new()
    {
        ["WGCrewRadioOperator"] = "radio",
    };

    /// <summary>Where lines were said lately, and until when they keep others nearby quiet.</summary>
    private readonly List<(EntityCoordinates Where, TimeSpan Until)> _recent = new();

    private readonly List<string> _pool = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrewBarksComponent, ComponentStartup>(OnBarksStartup);
        SubscribeLocalEvent<WGShipPostStaffedEvent>(OnStaffed);
        InitializeReactions();
        _timer = _random.NextFloat(); // its own phase, apart from the crew's other 1 Hz sweeps
    }

    /// <summary>Roster members already aboard when the ship starts barking get their voices.</summary>
    private void OnBarksStartup(Entity<WGCrewBarksComponent> grid, ref ComponentStartup args)
    {
        var members = EntityQueryEnumerator<WGShipRosterMemberComponent>();
        while (members.MoveNext(out var uid, out var member))
        {
            if (member.Grid == grid.Owner)
                Enroll(uid, grid);
        }
    }

    private void OnStaffed(ref WGShipPostStaffedEvent args)
    {
        if (!TryComp<WGCrewBarksComponent>(args.Grid, out var barks))
            return;

        var barker = Enroll(args.Mob, (args.Grid, barks));
        barker.Arriving = args.FromBay;
    }

    /// <summary>Gives a crewman or trader aboard a barking ship his voice.</summary>
    public WGCrewBarkerComponent Enroll(EntityUid mob, Entity<WGCrewBarksComponent> grid)
    {
        var barker = EnsureComp<WGCrewBarkerComponent>(mob);
        barker.Grid = grid;
        barker.Voice = VoiceOf(mob, grid.Comp);
        barker.NextIdle = _timing.CurTime + IdleGap(grid.Comp);
        return barker;
    }

    private string VoiceOf(EntityUid mob, WGCrewBarksComponent barks)
    {
        if (MetaData(mob).EntityPrototype?.ID is { } proto && barks.Voices.TryGetValue(proto, out var voice))
            return voice;

        if (CompOrNull<WGCrewComponent>(mob)?.Role?.Id is { } role)
        {
            if (barks.Voices.TryGetValue(role, out voice) || RoleVoices.TryGetValue(role, out voice))
                return voice;
            return role.Replace("WGCrew", string.Empty).ToLowerInvariant();
        }

        return HasComp<TraderComponent>(mob) ? "trader" : Common;
    }

    private TimeSpan IdleGap(WGCrewBarksComponent barks)
    {
        var spread = Math.Max(0, (barks.IdleMax - barks.IdleMin).TotalSeconds);
        return barks.IdleMin + TimeSpan.FromSeconds(_random.NextDouble() * spread);
    }

    /// <summary>The Fluent key of a kind of line.</summary>
    public static string Key(WGCrewBark kind)
    {
        return kind switch
        {
            WGCrewBark.Struck => "struck",
            WGCrewBark.Attacked => "attacked",
            WGCrewBark.GreetBoss => "greet-boss",
            WGCrewBark.GreetStaff => "greet-staff",
            WGCrewBark.GreetGuest => "greet-guest",
            WGCrewBark.GreetBarred => "greet-barred",
            WGCrewBark.Idle => "idle",
            WGCrewBark.Healed => "healed",
            WGCrewBark.Relieved => "relieved",
            WGCrewBark.StoodDown => "stood-down",
            WGCrewBark.Reporting => "reporting",
            WGCrewBark.Fight => "fight",
            WGCrewBark.Jump => "jump",
            WGCrewBark.Arrived => "arrived",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Every Fluent id a voice has for a kind of line, its own first, then the common ones.</summary>
    public List<string> Lines(string voice, WGCrewBark kind)
    {
        var lines = new List<string>();
        Collect(voice, Key(kind), lines);
        if (voice != Common)
            Collect(Common, Key(kind), lines);
        return lines;
    }

    private void Collect(string voice, string kind, List<string> into)
    {
        for (var n = 1; ; n++)
        {
            var id = $"{Prefix}{voice}-{kind}-{n}";
            if (!_loc.HasString(id))
                return;
            into.Add(id);
        }
    }

    /// <summary>
    /// Whether a barker may say a line of a kind now: alive and not a player, past his gap, and, unless it answers
    /// someone directly, with nobody near him having just spoken. The Boss's blows have a short gap of their own;
    /// relief lines have none.
    /// </summary>
    public bool CanBark(Entity<WGCrewBarkerComponent> mob, WGCrewBark kind)
    {
        if (!TryComp<WGCrewBarksComponent>(mob.Comp.Grid, out var barks) || !_mobs.IsAlive(mob)
            || HasComp<ActorComponent>(mob))
            return false;

        var now = _timing.CurTime;
        return kind switch
        {
            WGCrewBark.Struck => now >= mob.Comp.NextStruck,
            WGCrewBark.Relieved or WGCrewBark.StoodDown => true,
            _ => now >= mob.Comp.NextLine && !AreaBusy(_transform.GetMoverCoordinates(mob), barks, now),
        };
    }

    /// <summary>Has a barker say a random line of a kind, if <see cref="CanBark"/>. Lines may use <c>$name</c>.</summary>
    public bool Bark(Entity<WGCrewBarkerComponent?> mob, WGCrewBark kind, params (string, object)[] args)
    {
        if (!Resolve(mob, ref mob.Comp, false) || !CanBark((mob, mob.Comp), kind)
            || !TryComp<WGCrewBarksComponent>(mob.Comp.Grid, out var barks))
            return false;

        var barker = mob.Comp;
        _pool.Clear();
        Collect(barker.Voice, Key(kind), _pool);
        if (barker.Voice != Common)
            Collect(Common, Key(kind), _pool);
        if (_pool.Count == 0)
            return false;

        // Never the same line twice running.
        if (_pool.Count > 1 && barker.LastLine is { } last)
            _pool.Remove(last);
        var id = _random.Pick(_pool);

        var now = _timing.CurTime;
        barker.LastLine = id;
        barker.Said[kind] = barker.Said.GetValueOrDefault(kind) + 1;
        barker.NextLine = now + barks.LineGap;
        if (kind == WGCrewBark.Struck)
            barker.NextStruck = now + barks.StruckGap;
        _recent.Add((_transform.GetMoverCoordinates(mob), now + barks.AreaGap));

        _chat.TrySendInGameICMessage(mob, _loc.GetString(id, args), InGameICChatType.Speak, hideChat: false,
            checkRadioPrefix: false);
        return true;
    }

    /// <summary>Whether someone within the ship's area range spoke a line too recently.</summary>
    private bool AreaBusy(EntityCoordinates where, WGCrewBarksComponent barks, TimeSpan now)
    {
        var range = barks.AreaRange * barks.AreaRange;
        foreach (var (spot, until) in _recent)
        {
            if (now < until && spot.EntityId == where.EntityId && (spot.Position - where.Position).LengthSquared() < range)
                return true;
        }
        return false;
    }

    /// <summary>Where someone stands with the barker's ship, and whether that makes them the Boss.</summary>
    private WGShipAuthority Standing(EntityUid grid, EntityUid person, out bool boss)
    {
        var authority = _command.GetAuthority(person, grid);
        boss = TryComp<WGShipCommandComponent>(grid, out var command)
            ? WGShipCommandSystem.IsBoss(authority, command)
            : authority == WGShipAuthority.Owner;
        return authority;
    }
}
