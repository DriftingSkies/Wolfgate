using Content.Server.Construction;
using Content.Server.Power.Components;

namespace Content.Server._WG.Power;

/// <summary>Sets an upgradeable network battery's output and headroom from its installed parts.</summary>
public sealed class WGNetworkBatteryUpgradeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGNetworkBatteryUpgradeComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<WGNetworkBatteryUpgradeComponent, UpgradeExamineEvent>(OnUpgradeExamine);
    }

    private void OnRefreshParts(Entity<WGNetworkBatteryUpgradeComponent> ent, ref RefreshPartsEvent args)
    {
        var rating = args.PartRatings.GetValueOrDefault(ent.Comp.Part.Id, 1f);
        Apply(ent, Scalar(ent.Comp.PerRating, rating));
    }

    private void OnUpgradeExamine(Entity<WGNetworkBatteryUpgradeComponent> ent, ref UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("upgrade-power-supply", ent.Comp.Scalar);
    }

    /// <summary>The output multiplier an average part rating gives: linear from 1 at rating 1.</summary>
    public static float Scalar(float perRating, float rating)
    {
        return 1f + perRating * Math.Max(rating - 1f, 0f);
    }

    private void Apply(Entity<WGNetworkBatteryUpgradeComponent> ent, float scalar)
    {
        ent.Comp.Scalar = scalar;
        if (!TryComp<PowerNetworkBatteryComponent>(ent, out var battery))
            return;

        battery.MaxSupply = ent.Comp.BaseSupply * scalar;
        battery.SupplyRampTolerance = ent.Comp.BaseRampTolerance * scalar;
    }
}
