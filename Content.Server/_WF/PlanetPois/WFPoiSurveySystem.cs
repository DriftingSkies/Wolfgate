using System.Numerics;
using Content.Shared._WF.PlanetPois;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>A handheld survey scanner: a sweep on a world's ground picks up the faint signals round it.</summary>
[RegisterComponent]
public sealed partial class WFPoiSurveyScannerComponent : Component
{
    /// <summary>How far a sweep reaches, in metres.</summary>
    [DataField]
    public float Range = 200f;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(4);
}

/// <summary>A ruin's clue, such as old survey notes: read, it points to a site on the same world no one has found yet.</summary>
[RegisterComponent]
public sealed partial class WFPoiClueComponent : Component
{
    [DataField]
    public bool Followed;
}

/// <summary>
/// Sweeps with survey scanners: faint signals in range show on radars, and the nearest unknown one is pointed out. Also
/// reads ruins' clues.
/// </summary>
public sealed class WFPoiSurveySystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private WFPlanetPoiSystem _pois = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPoiSurveyScannerComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WFPoiSurveyScannerComponent, WFPoiSurveyDoAfterEvent>(OnSurveyed);
        SubscribeLocalEvent<WFPoiClueComponent, UseInHandEvent>(OnClueUsed);
    }

    private void OnClueUsed(Entity<WFPoiClueComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.Followed)
        {
            _popup.PopupEntity(Loc.GetString("wf-poi-clue-read"), args.User, args.User);
            return;
        }

        if (!TryFollowClue(args.User, out var name, out var offset))
        {
            _popup.PopupEntity(Loc.GetString("wf-poi-clue-nothing"), args.User, args.User);
            return;
        }

        ent.Comp.Followed = true;
        _popup.PopupEntity(Loc.GetString("wf-poi-clue-points", ("site", name), ("distance", (int) offset.Length()),
            ("direction", DirectionName(offset))), args.User, args.User);
    }

    /// <summary>Identifies a site on the user's world no one has found yet, at random. False if there is none.</summary>
    public bool TryFollowClue(EntityUid user, out string name, out Vector2 offset)
    {
        name = string.Empty;
        offset = default;
        var map = Transform(user).MapUid;
        var hidden = new List<Entity<WFPoiSignalComponent>>();
        var query = EntityQueryEnumerator<WFPoiSignalComponent>();
        while (query.MoveNext(out var uid, out var signal))
        {
            if (!signal.Revealed && signal.Ground == map && !TerminatingOrDeleted(signal.Ground))
                hidden.Add((uid, signal));
        }

        if (hidden.Count == 0)
            return false;

        var picked = _random.Pick(hidden);
        _pois.Reveal(picked, null);
        name = Loc.GetString(picked.Comp.Name);
        offset = SitePosition(picked.Comp) - _transform.GetWorldPosition(user);
        return true;
    }

    private Vector2 SitePosition(WFPoiSignalComponent signal)
    {
        return Vector2.Transform(new Vector2(signal.Origin.X + 0.5f, signal.Origin.Y + 0.5f), _transform.GetWorldMatrix(signal.Ground));
    }

    private string DirectionName(Vector2 offset)
    {
        return Loc.GetString($"wf-poi-survey-dir-{Angle.FromWorldVec(offset).GetDir().ToString().ToLowerInvariant()}");
    }

    private void OnUseInHand(Entity<WFPoiSurveyScannerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new WFPoiSurveyDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnSurveyed(Entity<WFPoiSurveyScannerComponent> ent, ref WFPoiSurveyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var found = Survey(args.User, ent.Comp.Range, out var nearest);
        if (nearest is not { } offset)
        {
            _popup.PopupEntity(Loc.GetString("wf-poi-survey-nothing", ("range", (int) ent.Comp.Range)), args.User, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("wf-poi-survey-found", ("count", found), ("distance", (int) offset.Length()), ("direction", DirectionName(offset))),
            args.User, args.User);
    }

    /// <summary>
    /// Brings every faint signal within <paramref name="range"/> of <paramref name="user"/> on its world's ground onto the
    /// radars. Returns how many, with the offset to the nearest site still unidentified.
    /// </summary>
    public int Survey(EntityUid user, float range, out Vector2? nearest)
    {
        nearest = null;
        var map = Transform(user).MapUid;
        var at = _transform.GetWorldPosition(user);
        var found = 0;
        var best = float.MaxValue;
        var query = EntityQueryEnumerator<WFPoiSignalComponent>();
        while (query.MoveNext(out var signal))
        {
            if (signal.Revealed || signal.Ground != map || TerminatingOrDeleted(signal.Ground))
                continue;

            var offset = SitePosition(signal) - at;
            var distance = offset.Length();
            if (distance > range)
                continue;

            if (signal.Faint)
            {
                signal.Faint = false;
                found++;
            }

            if (distance < best)
            {
                best = distance;
                nearest = offset;
            }
        }

        return found;
    }
}
