using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcDialogue;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcDialogue.Systems;

/// <summary>
/// What encounter ships say and how they feel about it. Each ship rates its chances from its hull, crew and guns
/// against the ship it is fighting; its warnings, threats and taunts are picked by that mood and by what its side
/// remembers of the vessel, and it speaks again only when something changes. A badly hurt ship stops threatening,
/// may run, and once beaten may surrender (<see cref="WFShipSurrenderSystem"/>).
/// </summary>
public sealed partial class WFNpcDialogueSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFShipSurrenderSystem _surrender = default!;

    public static readonly ProtoId<RadioChannelPrototype> Channel = "Traffic";

    /// <summary>Common lines for any voice that lacks its own.</summary>
    private const string Common = "wf-npc-voice";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Between a first and a final warning.</summary>
    private static readonly TimeSpan FinalWarnDelay = TimeSpan.FromSeconds(30);

    /// <summary>Between two lines about how the fight is going.</summary>
    private static readonly TimeSpan MoodLineGap = TimeSpan.FromSeconds(40);

    /// <summary>How long after its last fight a ship still counts as in one.</summary>
    private static readonly TimeSpan FightMemory = TimeSpan.FromMinutes(2);

    /// <summary>After this long out of a fight a ship's mood is read afresh, repairs and all.</summary>
    private static readonly TimeSpan Calm = TimeSpan.FromMinutes(5);

    /// <summary>Hits within this window decide who a ship is fighting.</summary>
    private static readonly TimeSpan FoeWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan GunCacheTime = TimeSpan.FromSeconds(5);

    /// <summary>A hull loses this share of its tiles before it is wrecked; health is read across what is left of that.</summary>
    private const float WreckedHull = 0.6f;

    /// <summary>A vessel below this share of its hull is worth gloating over.</summary>
    private const float GloatHull = 0.8f;

    /// <summary>A foe this many times stronger makes a whole ship wary.</summary>
    private const float Outgunned = 1.5f;

    /// <summary>Starts part way through the second, off the tick other one-second systems run on.</summary>
    private TimeSpan _next = TimeSpan.FromSeconds(0.4);

    private readonly Dictionary<string, WFNpcVoicePrototype> _voices = new();
    private readonly Dictionary<string, int> _variants = new();
    private readonly Dictionary<EntityUid, int> _crewPeak = new();
    private readonly HashSet<EntityUid> _hadGuns = new();
    private readonly Dictionary<EntityUid, (TimeSpan Until, int Guns, bool Armed)> _guns = new();
    private readonly Dictionary<(EntityUid Grid, string Group), int> _aboard = new();
    private GameTick _aboardTick;
    private readonly List<string> _keys = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        LoadVoices();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFNpcVoicePrototype>())
            LoadVoices();
    }

    private void LoadVoices()
    {
        _voices.Clear();
        foreach (var voice in _prototypes.EnumeratePrototypes<WFNpcVoicePrototype>())
        {
            _voices[voice.Lines] = voice;
        }
    }

    /// <summary>The voice a ship speaks with, by its zone lines; null for the defaults.</summary>
    public WFNpcVoicePrototype? Voice(WFEncounterShipState ship)
    {
        return _voices.GetValueOrDefault(ship.ZoneLines);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution != null)
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Virtual || ship.Building || ship.Derelict || ship.Lurking || ship.Surrendered
                    || TerminatingOrDeleted(ship.Grid))
                    continue;

                Tick((uid, encounter), ship, now);
            }
        }

        foreach (var grid in _crewPeak.Keys)
        {
            if (TerminatingOrDeleted(grid))
            {
                _crewPeak.Remove(grid);
                _hadGuns.Remove(grid);
                _guns.Remove(grid);
            }
        }
    }

    /// <summary>Rates a ship's chances, and has it speak of them or act on them when they get worse.</summary>
    private void Tick(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship, TimeSpan now)
    {
        if (CrewAboard(ship) == 0)
            return;

        var foe = MainFoe(encounter.Comp, ship, now);
        var fighting = foe != null || ship.LastFought is { } fought && now - fought < FightMemory;
        var mood = Assess(ship, foe);
        if (fighting)
        {
            if (mood > ship.Mood)
                ship.Mood = mood;
        }
        else if (ship.LastFought is not { } last || now - last >= Calm)
        {
            ship.Mood = mood;
            ship.SpokenMood = WFShipMood.Confident;
        }

        // Only a fight with players is worth talking about or giving up over.
        if (foe is not { } enemy || !IsPlayerVessel(enemy))
            return;

        var talk = Talk(encounter.Comp, ship.Side, enemy);
        if (ship.Mood > ship.SpokenMood && ship.Mood >= WFShipMood.Hurt && now >= ship.NextMoodLine)
        {
            Escalate(encounter, ship, enemy, talk);
            return;
        }

        if (now < ship.NextMoodLine)
            return;

        if ((talk.Known & WFTalkFacts.Killed) == 0 && LostShipTo(encounter.Comp, ship, enemy))
            talk.Known |= WFTalkFacts.Killed;
        if (Recall(encounter.Comp, ship, enemy, talk, WFTalkFacts.Killed, "recall-killed"))
        {
            ship.NextMoodLine = now + MoodLineGap;
            return;
        }

        if (ship.Mood <= WFShipMood.Wary && (talk.Said & WFTalkFacts.Gloated) == 0 && _status.HullShare(enemy) < GloatHull
            && Say(encounter.Comp, ship, "gloat", enemy, WFNpcRadioPriority.Normal, talk))
        {
            talk.Said |= WFTalkFacts.Gloated;
            ship.NextMoodLine = now + MoodLineGap;
        }
    }

    /// <summary>A ship's mood got worse in a fight with a player vessel: it says so, and a beaten one may give up.</summary>
    private void Escalate(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship, EntityUid enemy, WFTalkState talk)
    {
        var now = _timing.CurTime;
        var voice = Voice(ship);
        var retreats = voice?.Retreats ?? true;
        ship.SpokenMood = ship.Mood;
        ship.NextMoodLine = now + MoodLineGap;
        switch (ship.Mood)
        {
            case WFShipMood.Broken:
                if (!ship.SurrenderRolled && !ship.LastStand)
                {
                    ship.SurrenderRolled = true;
                    if (_random.Prob(Math.Clamp(voice?.Surrender ?? WFNpcVoice.DefaultSurrender, 0f, 1f))
                        && _surrender.Surrender(encounter, ship, enemy))
                        return;
                }

                if (retreats && Retreat(ship))
                    Say(encounter.Comp, ship, "flee", enemy, WFNpcRadioPriority.Normal, talk);
                else
                    Say(encounter.Comp, ship, "laststand", enemy, WFNpcRadioPriority.Normal, talk);
                break;
            case WFShipMood.Desperate:
                Say(encounter.Comp, ship, "desperate", enemy, WFNpcRadioPriority.Normal, talk);
                if (retreats)
                    Retreat(ship);
                break;
            default:
                Say(encounter.Comp, ship, "hurt", enemy, WFNpcRadioPriority.Normal, talk);
                break;
        }
    }

    /// <summary>Has the ship's pilots run from its attackers instead of circling them. False if it already was or has nobody to fly.</summary>
    public bool Retreat(WFEncounterShipState ship)
    {
        if (ship.Retreating)
            return false;

        var any = false;
        var pilots = EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent>();
        while (pilots.MoveNext(out var uid, out var duty, out var crew))
        {
            if (crew.Group != ship.Group || _crew.HomeGrid(uid, crew) != ship.Grid || !_mobs.IsAlive(uid))
                continue;

            duty.FleeAttackers = true;
            any = true;
        }

        ship.Retreating = any;
        return any;
    }

    /// <summary>
    /// The warning a side gives a vessel in its zone: a first one, after a while a final one, then nothing until it
    /// leaves and comes back. Its first words on a return bring up what the side remembers. Null for silence.
    /// </summary>
    public string? WarnLine(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid intruder, string name, int distance)
    {
        var now = _timing.CurTime;
        var talk = Talk(encounter, ship.Side, intruder);
        if (talk.Left)
        {
            talk.Left = false;
            talk.Stage = 0;
            talk.NextWarn = TimeSpan.Zero;
            talk.Known |= talk.WasEngaged ? WFTalkFacts.Fled : WFTalkFacts.Returned;
        }

        if (now < talk.NextWarn)
            return null;

        var mood = MoodAgainst(ship, intruder);
        var args = Args(ship, name, distance);
        string? line = null;
        if (talk.Stage == 0)
        {
            line = RecallLine(ship, talk, WFTalkFacts.Fled, "recall-fled", args)
                   ?? RecallLine(ship, talk, WFTalkFacts.Killed, "recall-killed", args)
                   ?? RecallLine(ship, talk, WFTalkFacts.Returned, "recall-returned", args)
                   ?? Line(Keys(ship, "warn", mood), talk, args);
        }
        // A ship in no state to back its words up says them once.
        else if (talk.Stage == 1 && mood <= WFShipMood.Wary)
            line = Line(Keys(ship, "final", mood), talk, args);

        if (line == null)
            return null;

        talk.Stage++;
        talk.NextWarn = now + FinalWarnDelay;
        return line;
    }

    /// <summary>What a side says as it opens fire on a vessel: why, if it fired first, or how it feels about the fight.</summary>
    public string AttackLine(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid intruder, string name, bool fired)
    {
        var talk = Talk(encounter, ship.Side, intruder);
        talk.WasEngaged = true;
        talk.Stage = Math.Max(talk.Stage, 2);
        var args = Args(ship, name, 0);
        if (fired)
            talk.Known |= WFTalkFacts.Fired;

        return RecallLine(ship, talk, WFTalkFacts.Fired, "fired", args)
               ?? RecallLine(ship, talk, WFTalkFacts.Fled, "recall-fled", args)
               ?? Line(Keys(ship, "attack", MoodAgainst(ship, intruder)), talk, args)
               ?? Loc.GetString($"{ship.ZoneLines}-attack", ("intruder", name));
    }

    /// <summary>A vessel left a ship's zone: it is warned afresh, and remembered, when it comes back.</summary>
    public void Left(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid intruder, bool engaged)
    {
        if (!encounter.Talk.TryGetValue((ship.Side, intruder), out var talk) || talk.Stage == 0 && !engaged)
            return;

        talk.Left = true;
        talk.WasEngaged |= engaged;
        talk.Stage = 0;
    }

    /// <summary>Puts a line for the ship on the radio, heard around it and <paramref name="near"/>. False if nothing was said.</summary>
    public bool Say(WFEncounterComponent encounter, WFEncounterShipState ship, string key, EntityUid near,
        WFNpcRadioPriority priority, WFTalkState? talk = null)
    {
        var text = Line(Keys(ship, key, ship.Mood), talk ?? Talk(encounter, ship.Side, near),
            Args(ship, NameOf(near), 0));
        return text != null && _encounters.TrySay(ship, Channel, text, near, priority);
    }

    /// <summary>A line for the ship by key, addressed to <paramref name="name"/>; the key itself if no voice has it.</summary>
    public string LineFor(WFTalkState? talk, WFEncounterShipState ship, string key, string name)
    {
        return Line(Keys(ship, key, ship.Mood), talk, Args(ship, name, 0)) ?? Loc.GetString($"{Common}-{key}");
    }

    /// <summary>What a side remembers of a vessel, made on first asking.</summary>
    public WFTalkState Talk(WFEncounterComponent encounter, string side, EntityUid intruder)
    {
        if (!encounter.Talk.TryGetValue((side, intruder), out var talk))
            encounter.Talk[(side, intruder)] = talk = new WFTalkState();
        return talk;
    }

    /// <summary>A ship's mood towards one vessel: a whole ship facing a much stronger one is wary.</summary>
    public WFShipMood MoodAgainst(WFEncounterShipState ship, EntityUid other)
    {
        if (ship.Mood != WFShipMood.Confident)
            return ship.Mood;

        return Power(other) > Power(ship.Grid) * Outgunned ? WFShipMood.Wary : WFShipMood.Confident;
    }

    /// <summary>
    /// Rates a ship from its hull, crew and guns. Hull counts from whole down to wrecked; guns count only for a ship
    /// that ever had them. Out of the fight is beaten whatever the numbers.
    /// </summary>
    public WFShipMood Assess(WFEncounterShipState ship, EntityUid? foe)
    {
        var grid = ship.Grid;
        if (_status.IsDisabled(grid))
            return WFShipMood.Broken;

        var hull = Math.Clamp((_status.HullShare(grid) - WreckedHull) / (1f - WreckedHull), 0f, 1f);
        var aboard = CrewAboard(ship);
        var peak = Math.Max(Math.Max(ship.Crew, _crewPeak.GetValueOrDefault(grid)), aboard);
        _crewPeak[grid] = peak;
        var crew = peak > 0 ? aboard / (float) peak : 0f;
        var (_, armed) = Guns(grid);
        if (armed)
            _hadGuns.Add(grid);

        float health;
        if (_hadGuns.Contains(grid))
        {
            health = 0.5f * hull + 0.3f * crew + (armed ? 0.2f : 0f);
            if (!armed && crew < 0.5f)
                return WFShipMood.Broken;
        }
        else
            health = (0.5f * hull + 0.3f * crew) / 0.8f;

        if (health < 0.3f)
            return WFShipMood.Broken;
        if (health < 0.5f)
            return WFShipMood.Desperate;
        if (health < 0.8f)
            return WFShipMood.Hurt;
        return foe is { } other && Power(other) > Power(grid) * Outgunned ? WFShipMood.Wary : WFShipMood.Confident;
    }

    /// <summary>The vessel whose fire on the ship's side has landed most in the last half minute, else one its crew is alerted to.</summary>
    public EntityUid? MainFoe(WFEncounterComponent encounter, WFEncounterShipState ship, TimeSpan now)
    {
        EntityUid? best = null;
        var most = 0;
        foreach (var ((attacker, side), hits) in encounter.Fire)
        {
            if (side != ship.Side || TerminatingOrDeleted(attacker))
                continue;

            var recent = 0;
            foreach (var at in hits)
            {
                if (now - at < FoeWindow)
                    recent++;
            }

            if (recent > most)
            {
                most = recent;
                best = attacker;
            }
        }

        if (best != null)
            return best;

        foreach (var hostile in _alerts.GetHostileShips(ship.Grid, ship.Group))
        {
            return hostile;
        }

        foreach (var engaged in ship.Engaged)
        {
            if (!TerminatingOrDeleted(engaged))
                return engaged;
        }

        return null;
    }

    /// <summary>Whether a vessel is one players fly, not an NPC crew's.</summary>
    public bool IsPlayerVessel(EntityUid grid)
    {
        return !HasComp<WFEncounterGridComponent>(grid) && !HasComp<WFCrewShipProfileComponent>(grid);
    }

    /// <summary>Living NPC crew of the ship aboard it, counted for every ship once a tick.</summary>
    public int CrewAboard(WFEncounterShipState ship)
    {
        if (_aboardTick != _timing.CurTick)
        {
            _aboardTick = _timing.CurTick;
            _aboard.Clear();
            var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
            while (crew.MoveNext(out var uid, out var member, out var xform))
            {
                if (xform.GridUid is not { } grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                    continue;

                var key = (grid, member.Group);
                _aboard[key] = _aboard.GetValueOrDefault(key) + 1;
            }
        }

        return _aboard.GetValueOrDefault((ship.Grid, ship.Group));
    }

    /// <summary>Whether another ship of the side is out of the fight after taking fire from the vessel.</summary>
    private bool LostShipTo(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid enemy)
    {
        if (!encounter.ShipHits.TryGetValue((enemy, ship.Side), out var hits) || hits == 0)
            return false;

        foreach (var other in encounter.Ships.Values)
        {
            if (other != ship && other.Side == ship.Side && !other.Virtual && !other.Derelict && !_encounters.InFight(other))
                return true;
        }

        return false;
    }

    /// <summary>Says a remembered fact once, if the side knows it and has not said it.</summary>
    private bool Recall(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid enemy, WFTalkState talk,
        WFTalkFacts fact, string key)
    {
        if ((talk.Known & fact) == 0 || (talk.Said & fact) != 0)
            return false;

        talk.Said |= fact;
        return Say(encounter, ship, key, enemy, WFNpcRadioPriority.Normal, talk);
    }

    private string? RecallLine(WFEncounterShipState ship, WFTalkState talk, WFTalkFacts fact, string key,
        (string, object)[] args)
    {
        if ((talk.Known & fact) == 0 || (talk.Said & fact) != 0)
            return null;

        talk.Said |= fact;
        return Line(Keys(ship, key, ship.Mood), talk, args);
    }

    /// <summary>
    /// The ids to try for a line, best first: the voice's own line for the mood, then the common one for the mood,
    /// then the voice's plain line. A ship in a bad way never falls back on a confident threat.
    /// </summary>
    private List<string> Keys(WFEncounterShipState ship, string key, WFShipMood mood)
    {
        var voice = ship.ZoneLines;
        var moodName = mood.ToString().ToLowerInvariant();
        _keys.Clear();
        switch (key)
        {
            case "warn":
                _keys.Add($"{voice}-warn-{moodName}");
                if (mood <= WFShipMood.Wary)
                    _keys.Add($"{voice}-warn");
                _keys.Add($"{Common}-warn-{moodName}");
                _keys.Add($"{voice}-warn");
                break;
            case "attack":
                if (mood >= WFShipMood.Hurt)
                {
                    var weak = mood == WFShipMood.Hurt ? "hurt" : "desperate";
                    _keys.Add($"{voice}-attack-{weak}");
                    _keys.Add($"{Common}-attack-{weak}");
                }

                _keys.Add($"{voice}-attack");
                break;
            default:
                _keys.Add($"{voice}-{key}");
                _keys.Add($"{Common}-{key}");
                break;
        }

        return _keys;
    }

    /// <summary>A variant of the first id that has any, not the one said last when there is a choice. Null when none has.</summary>
    private string? Line(List<string> ids, WFTalkState? talk, (string, object)[] args)
    {
        foreach (var id in ids)
        {
            var count = Variants(id);
            if (count == 0)
                continue;

            string picked;
            if (count < 0)
                picked = id;
            else
            {
                var roll = _random.Next(1, count + 1);
                if (count > 1 && talk?.Last == $"{id}-{roll}")
                    roll = roll % count + 1;
                picked = $"{id}-{roll}";
            }

            if (talk != null)
                talk.Last = picked;
            return _loc.GetString(picked, args);
        }

        return null;
    }

    /// <summary>How many numbered variants a line id has; -1 for a single unnumbered line, 0 for none.</summary>
    private int Variants(string id)
    {
        if (_variants.TryGetValue(id, out var cached))
            return cached;

        var count = 0;
        while (_loc.HasString($"{id}-{count + 1}"))
        {
            count++;
        }

        if (count == 0 && _loc.HasString(id))
            count = -1;
        _variants[id] = count;
        return count;
    }

    private (string, object)[] Args(WFEncounterShipState ship, string name, int distance)
    {
        return new (string, object)[] { ("intruder", name), ("distance", distance), ("ship", ship.Name) };
    }

    /// <summary>A vessel's name as a side would call it: "Unidentified vessel" when it runs without a label.</summary>
    public string NameOf(EntityUid grid)
    {
        return TryComp<Content.Shared.Shuttles.Components.IFFComponent>(grid, out var iff)
               && (iff.Flags & (Content.Shared.Shuttles.Components.IFFFlags.HideLabel | Content.Shared.Shuttles.Components.IFFFlags.Hide)) != 0
            ? Loc.GetString("wf-encounter-zone-unknown")
            : Name(grid);
    }

    /// <summary>How hard a ship hits: its working guns, weighted by how much of its hull is left.</summary>
    private float Power(EntityUid grid)
    {
        var (guns, _) = Guns(grid);
        return MathF.Max(guns, 0.5f) * MathF.Max(_status.HullShare(grid), 0.2f);
    }

    private (int Guns, bool Armed) Guns(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_guns.TryGetValue(grid, out var cached) && now < cached.Until)
            return (cached.Guns, cached.Armed);

        var guns = _status.GunCount(grid);
        var armed = guns > 0 && _status.CanFire(grid);
        _guns[grid] = (now + GunCacheTime, guns, armed);
        return (guns, armed);
    }
}
