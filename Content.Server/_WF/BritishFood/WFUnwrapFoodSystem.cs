using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared._WF.BritishFood;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.BritishFood;

/// <summary>Takes packaged food out of its container when used in hand; eating from the pack still works.</summary>
public sealed partial class WFUnwrapFoodSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private WFPipingHotSystem _pipingHot = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFUnwrapFoodComponent, UseInHandEvent>(OnUseInHand, before: [typeof(FoodSystem)]);
        SubscribeLocalEvent<WFUnwrapFoodComponent, ExaminedEvent>(OnExamined);
    }

    private void OnUseInHand(Entity<WFUnwrapFoodComponent> ent, ref UseInHandEvent args)
    {
        if (!args.Handled && Unwrap(ent, args.User) != null)
            args.Handled = true;
    }

    private void OnExamined(Entity<WFUnwrapFoodComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-unwrap-food-examine"));
    }

    /// <summary>
    /// Swaps the pack for the bare food and the empty container. The food takes everything left in the pack, with room
    /// for at least as much as the pack held, and its heat. Returns the food, or null with the pack untouched.
    /// </summary>
    public EntityUid? Unwrap(Entity<WFUnwrapFoodComponent> ent, EntityUid user)
    {
        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.Solution, out var packSoln, out var left))
            return null;

        var coords = Transform(user).Coordinates;
        var food = SpawnAtPosition(ent.Comp.Food, coords);
        if (!_solution.TryGetSolution(food, ent.Comp.Solution, out var bareSoln, out var bare))
        {
            Del(food);
            return null;
        }

        // Frying a pack grows its solution past the bare food's, and a solution that won't fit is refused whole.
        _solution.RemoveAllSolution(bareSoln.Value);
        _solution.SetCapacity(bareSoln.Value, FixedPoint2.Max(bare.MaxVolume, packSoln.Value.Comp.Solution.MaxVolume));
        if (!_solution.TryAddSolution(bareSoln.Value, left.Clone()))
        {
            Del(food);
            return null;
        }

        _pipingHot.PassHeat(ent.Owner, food);

        var containers = new List<EntityUid>();
        if (TryComp<FoodComponent>(ent, out var pack))
        {
            foreach (var trash in pack.Trash)
            {
                containers.Add(SpawnAtPosition(trash, coords));
            }
        }

        _audio.PlayPvs(ent.Comp.Sound, coords);
        _popup.PopupEntity(Loc.GetString("wf-unwrap-food-done", ("food", food)), user, user);

        // The pack goes to nullspace first so its hand is free for the food.
        _transform.DetachEntity(ent, Transform(ent));
        QueueDel(ent);

        _hands.PickupOrDrop(user, food);
        foreach (var container in containers)
        {
            _hands.PickupOrDrop(user, container);
        }

        return food;
    }
}
