using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Torment;
using Content.Server._WG.Remade.Monolith;
using Content.Shared._WG.Remade;
using Content.Shared._WG.Remade.Betrayal;
using Content.Shared._WG.Remade.Dread;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Betrayal;

/// <summary>
/// What the stone does with the player's own senses and mind. Once a second every player under its influence gets a chance of a
/// false sound placed by their client (knocking behind a door, scraping in a wall, breath at their back, footsteps that stop when
/// they stop), a crewmate who looks like a Remade for a moment, and, once their stress passes what the Director calls shaken, a
/// breaking mind (<c>.Mind</c>): shaking aim, dropped things, nosebleeds, twitching, and lies. Everything scales with the stone's
/// stage and the player's stress, uses the dread rates, and goes to that player alone.
/// </summary>
public sealed partial class WGBetrayalFearSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public const float FlickerRange = 12f;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan KnockGap = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ScrapeGap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BreathGap = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan StepsGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FlickerGap = TimeSpan.FromSeconds(20);

    private readonly List<(WGMonolithStage Stage, MapCoordinates At, EntityUid? LocalGrid)> _stones = new();
    private readonly List<(EntityUid Uid, ActorComponent Actor)> _others = new();
    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TickRate;
        Sweep(now);
    }

    private void Sweep(TimeSpan now)
    {
        var rate = _config.GetCVar(WGBetrayalCVars.Rate);
        if (rate <= 0f)
            return;

        _stones.Clear();
        var monoliths = EntityQueryEnumerator<WGMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Stage != WGMonolithStage.Quiet && xform.MapID != MapId.Nullspace)
                _stones.Add((monolith.Stage, _transform.GetMapCoordinates(uid, xform), monolith.Local ? xform.GridUid ?? uid : null));
        }

        _others.Clear();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out _, out var xform))
        {
            if (HasComp<WGRemadeComponent>(uid) || _mobState.IsDead(uid) || xform.MapID == MapId.Nullspace)
                continue;

            _others.Add((uid, actor));
        }

        foreach (var (uid, actor) in _others)
        {
            var here = _transform.GetMapCoordinates(uid);
            var influence = 0f;
            var stage = WGMonolithStage.Quiet;
            var grid = Transform(uid).GridUid;
            foreach (var (stoneStage, at, localGrid) in _stones)
            {
                if (localGrid != null && grid != localGrid)
                    continue;

                var same = at.MapId == here.MapId;
                // Knocks, breath and footsteps are for the crew near a stone; the rest of the sector gets the score.
                var share = WGDreadMath.Influence(stoneStage, same ? (at.Position - here.Position).Length() : 0f, same, false);
                if (share > influence || share == influence && stoneStage > stage)
                {
                    influence = share;
                    stage = stoneStage;
                }
            }

            var tormented = HasComp<WGTormentedComponent>(uid);
            if (tormented)
            {
                influence = 1f;
                stage = WGMonolithStage.Crescendo;
            }

            var subject = EnsureComp<WGBetrayalSubjectComponent>(uid);
            Drip(uid, subject, now);
            if (influence <= 0f)
            {
                ReleaseAim(uid);
                continue;
            }

            var stress = CompOrNull<WGRemadeStressComponent>(uid)?.Stress ?? 0f;
            if (tormented)
                stress = Math.Max(stress, WGTormentedComponent.Stress);

            Senses(uid, actor, subject, stage, influence, stress, influence * WGDreadMath.StressFactor(stress) * rate, now);
            Mind(uid, actor, subject, stage, influence, stress, rate, now);
        }
    }

    private bool Roll(float perMinute) => _random.Prob(WGDreadMath.Chance(perMinute, 1f));

    /// <summary>Gives one player a chance of each false sense in a second. <paramref name="factor"/> is influence, stress and the rate setting.</summary>
    private void Senses(EntityUid uid, ActorComponent actor, WGBetrayalSubjectComponent subject, WGMonolithStage stage, float influence,
        float stress, float factor, TimeSpan now)
    {
        var glimpses = WGDreadMath.GlimpsesPerMinute(stage) * factor;
        var intensity = Math.Clamp(influence * 0.6f + stress / 250f, 0.2f, 1f);

        if (now >= subject.NextKnock && Roll(glimpses * 1.2f))
        {
            subject.NextKnock = now + KnockGap;
            Sound(actor, subject, WGBetrayalSoundKind.Knock, intensity, 0f);
        }

        if (now >= subject.NextScrape && Roll(glimpses))
        {
            subject.NextScrape = now + ScrapeGap;
            Sound(actor, subject, WGBetrayalSoundKind.Scrape, intensity, 0f);
        }

        if (stage >= WGMonolithStage.Growth && now >= subject.NextBreath && Roll(glimpses * 0.7f))
        {
            subject.NextBreath = now + BreathGap;
            Sound(actor, subject, WGBetrayalSoundKind.Breath, intensity, 0f);
        }

        if (stage >= WGMonolithStage.Harvest && now >= subject.NextSteps && Roll(glimpses * 0.5f))
        {
            subject.NextSteps = now + StepsGap;
            Sound(actor, subject, WGBetrayalSoundKind.Footsteps, intensity, _random.NextFloat(10f, 20f));
        }

        if (now >= subject.NextFlicker && Roll(glimpses * 0.7f))
        {
            subject.NextFlicker = now + FlickerGap;
            Flicker(uid, actor, subject, stage);
        }
    }

    /// <summary>Tells a player's client to place a false sound.</summary>
    public void Sound(ActorComponent actor, WGBetrayalSoundKind kind, float intensity, float seconds)
    {
        RaiseNetworkEvent(new WGBetrayalSoundEvent(kind, intensity, seconds), actor.PlayerSession);
    }

    private void Sound(ActorComponent actor, WGBetrayalSubjectComponent subject, WGBetrayalSoundKind kind, float intensity, float seconds)
    {
        subject.Sounds++;
        Sound(actor, kind, intensity, seconds);
    }

    /// <summary>Makes another living player in sight look like a Remade to this player alone, for a moment. False if nobody is near.</summary>
    public bool Flicker(EntityUid uid, ActorComponent actor, WGBetrayalSubjectComponent subject, WGMonolithStage stage)
    {
        var options = new List<EntityUid>();
        foreach (var (other, _) in _others)
        {
            if (other != uid && _examine.InRangeUnOccluded(uid, other, FlickerRange))
                options.Add(other);
        }

        var sprites = new List<int>(WGDreadGlimpses.Available(stage));
        if (options.Count == 0 || sprites.Count == 0)
            return false;

        subject.Flickers++;
        RaiseNetworkEvent(new WGBetrayalFlickerEvent(GetNetEntity(_random.Pick(options)), _random.Pick(sprites), _random.NextFloat(0.2f, 0.4f)),
            actor.PlayerSession);
        return true;
    }

    /// <summary>Feeds the sweep's list of living players, so a caller outside the sweep can use <see cref="Flicker"/>.</summary>
    public void Gather()
    {
        _others.Clear();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out _, out _))
        {
            if (!HasComp<WGRemadeComponent>(uid) && !_mobState.IsDead(uid))
                _others.Add((uid, actor));
        }
    }
}
