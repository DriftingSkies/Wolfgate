using Content.Server.Nutrition.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Server._WF.Husbandry;

public sealed partial class WFHusbandrySystem
{
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private ThirstSystem _thirst = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>
    /// Feeds a pen's hungry livestock from its feeders' storage: one food item fills an animal to Okay, with water
    /// that comes with the trough. Penned livestock never look for food themselves.
    /// </summary>
    public int FeedPen(WFPen pen)
    {
        var fed = 0;
        foreach (var animal in pen.Livestock)
        {
            if (!TryComp<HungerComponent>(animal, out var hunger))
                continue;

            var okay = hunger.Thresholds[HungerThreshold.Okay];
            if (_hunger.GetHunger(hunger) >= okay || TakeFeed(pen) is not { } food)
                continue;

            QueueDel(food);
            _hunger.SetHunger(animal, okay, hunger);
            if (TryComp<ThirstComponent>(animal, out var thirst))
                _thirst.SetThirst(animal, thirst, thirst.ThirstThresholds[ThirstThreshold.Okay]);
            fed++;
        }

        return fed;
    }

    /// <summary>The first food item in any of the pen's feeders, taken out of it.</summary>
    private EntityUid? TakeFeed(WFPen pen)
    {
        foreach (var feeder in pen.Feeders)
        {
            if (!TryComp<StorageComponent>(feeder, out var storage))
                continue;

            foreach (var item in storage.Container.ContainedEntities)
            {
                if (!HasComp<FoodComponent>(item) || TerminatingOrDeleted(item))
                    continue;

                _containers.Remove(item, storage.Container);
                return item;
            }
        }

        return null;
    }
}
