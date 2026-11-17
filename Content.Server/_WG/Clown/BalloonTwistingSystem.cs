using Content.Server.Popups;
using Content.Shared._WG.Clown;
using Content.Shared._WG.Traits;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Clown;

/// <summary>Lets a clown twist an empty balloon into a balloon animal from the right-click menu.</summary>
public sealed partial class BalloonTwistingSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private static readonly VerbCategory TwistCategory = new("balloon-twist-verb-category", null);

    private static readonly (string Label, EntProtoId Proto)[] Shapes =
    [
        ("balloon-twist-shape-dog",    new EntProtoId("BalloonAnimalDog")),
        ("balloon-twist-shape-clown",  new EntProtoId("BalloonAnimalClown")),
        ("balloon-twist-shape-banana", new EntProtoId("BalloonAnimalBanana")),
        ("balloon-twist-shape-cat",    new EntProtoId("BalloonAnimalCat")),
        ("balloon-twist-shape-moth",   new EntProtoId("BalloonAnimalMoth")),
    ];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BalloonEmptyComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<BalloonEmptyComponent, BalloonTwistDoAfterEvent>(OnDoAfter);
    }

    private void OnGetVerbs(Entity<BalloonEmptyComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!HasComp<ClownComponent>(args.User))
            return;

        var user = args.User;
        var uid = ent.Owner;

        foreach (var (label, proto) in Shapes)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(label),
                Category = TwistCategory,
                Act = () => StartTwist(uid, user, proto),
                Priority = 1,
            });
        }
    }

    private void StartTwist(EntityUid uid, EntityUid user, EntProtoId proto)
    {
        var ev = new BalloonTwistDoAfterEvent { TargetPrototype = proto };
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(3), ev, uid, used: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnDoAfter(Entity<BalloonEmptyComponent> ent, ref BalloonTwistDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;

        // Spawn at the user, because spawning at a held item would attach the new balloon to them.
        var coords = Transform(user).Coordinates;

        var newItem = Spawn(args.TargetPrototype, coords);

        // Deletion is delayed, so free the hand first or the pickup below fails.
        if (_containers.TryGetContainingContainer(ent.Owner, out var container))
            _containers.Remove(ent.Owner, container, force: true);
        QueueDel(ent);

        _hands.TryPickupAnyHand(user, newItem);

        _popup.PopupEntity(Loc.GetString("balloon-twist-success"), user);
        args.Handled = true;
    }
}
