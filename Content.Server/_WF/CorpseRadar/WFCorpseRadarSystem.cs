using Content.Server._Mono.Radar;
using Content.Shared._Mono.Radar;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Robust.Shared.Timing;

namespace Content.Server._WF.CorpseRadar;

/// <summary>
/// Shows a player's body on ship radar once it has been dead for <see cref="Delay"/>, until it is revived or destroyed.
/// </summary>
public sealed class WFCorpseRadarSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMindSystem _minds = default!;

    /// <summary>How long a body is dead before it shows, so a quick revival or a fight still going on gives nothing away.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    /// <summary>A small red diamond, drawn the same at every zoom so it reads as a marker rather than a hull.</summary>
    private static readonly BlipConfig CorpseBlip = new()
    {
        Bounds = new Box2(-0.3f, -0.3f, 0.3f, 0.3f),
        Color = Color.FromHex("#D23C3C"),
        Shape = RadarBlipShape.Diamond,
    };

    private TimeSpan _nextCheck;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        var body = args.Target;
        if (args.NewMobState == MobState.Dead)
        {
            // Only a player's body; another blip already on it (a mapped marker, say) is left as it is.
            if (!_minds.TryGetMind(body, out _, out var mind) || mind.UserId == null || HasComp<RadarBlipComponent>(body))
                return;

            EnsureComp<WFCorpseRadarComponent>(body).ShowAt = _timing.CurTime + Delay;
            return;
        }

        if (args.OldMobState != MobState.Dead || !TryComp<WFCorpseRadarComponent>(body, out var corpse))
            return;

        if (corpse.Shown)
            RemComp<RadarBlipComponent>(body);
        RemComp<WFCorpseRadarComponent>(body);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        ShowDue();
    }

    /// <summary>Adds the blip to every body whose wait is over.</summary>
    public void ShowDue()
    {
        var query = EntityQueryEnumerator<WFCorpseRadarComponent>();
        while (query.MoveNext(out var body, out var corpse))
        {
            if (corpse.Shown || _timing.CurTime < corpse.ShowAt || HasComp<RadarBlipComponent>(body))
                continue;

            var blip = AddComp<RadarBlipComponent>(body);
            blip.Config = CorpseBlip;
            blip.VisibleFromOtherGrids = true;
            corpse.Shown = true;
        }
    }
}
