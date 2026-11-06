using System.Linq;
using System.Numerics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Storage;
using Content.Shared.Throwing;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns the <see cref="FaunaDeathSpawnComponent"/> entities when the mob dies, and gibs, deletes or empties the mob.
/// </summary>
public sealed partial class FaunaDeathSpawnSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedProjectileSystem _projectile = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    // Spawns and gibs wait for the next tick, since a death can happen inside another system's component loop.
    private readonly List<EntityUid> _pendingMobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaDeathSpawnComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingMobs.Count == 0)
            return;

        var mobs = _pendingMobs.ToArray();
        _pendingMobs.Clear();
        foreach (var mob in mobs)
        {
            if (TerminatingOrDeleted(mob) || !TryComp<FaunaDeathSpawnComponent>(mob, out var comp))
                continue;

            if (comp.ThrowContents > 0)
                ThrowContents((mob, comp));

            if (!comp.Gib)
                continue;

            if (HasComp<BodyComponent>(mob))
                _body.GibBody(mob);
            else
                QueueDel(mob);
        }
    }

    private void OnMobStateChanged(Entity<FaunaDeathSpawnComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (ent.Comp.VanishChance > 0 && _random.Prob(ent.Comp.VanishChance))
        {
            QueueDel(ent);
            return;
        }

        var coords = _transform.GetMapCoordinates(ent);
        foreach (var proto in EntitySpawnCollection.GetSpawns(ent.Comp.Spawns, _random))
        {
            var offset = new Vector2(_random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f)) * ent.Comp.Scatter;
            _helper.QueueSpawn(proto, coords.Offset(offset));
        }

        if (ent.Comp.Gib || ent.Comp.ThrowContents > 0)
            _pendingMobs.Add(ent);

        if (ent.Comp.Popup is { } popup)
            _popup.PopupCoordinates(Loc.GetString(popup, ("user", ent.Owner)), Transform(ent).Coordinates, PopupType.MediumCaution);
    }

    /// <summary>
    /// Drops what the mob holds and pulls out what is stuck in it, then throws each away in a random direction.
    /// </summary>
    private void ThrowContents(Entity<FaunaDeathSpawnComponent> ent)
    {
        var items = new List<EntityUid>();
        if (TryComp<EmbeddedContainerComponent>(ent, out var embedded))
        {
            foreach (var item in embedded.EmbeddedObjects.ToArray())
            {
                if (!TryComp<EmbeddableProjectileComponent>(item, out var embeddable))
                    continue;

                _projectile.EmbedDetach(item, embeddable);
                items.Add(item);
            }
        }

        foreach (var item in _hands.EnumerateHeld(ent).ToArray())
        {
            if (_hands.TryDrop(ent, item, checkActionBlocker: false))
                items.Add(item);
        }

        foreach (var item in items)
        {
            if (TerminatingOrDeleted(item))
                continue;

            var direction = _random.NextAngle().ToVec() * ent.Comp.ThrowContents;
            _throwing.TryThrow(item, direction, 8f, ent, pushbackRatio: 0f);
        }
    }
}
