using Content.Server._WG.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.BritishFood;

namespace Content.Server._WG.BritishFood;

/// <summary>Applies <see cref="WGStrainHeart"/> to a wound host's heart organ through Wolfmed's organ damage.</summary>
public sealed partial class WGStrainHeartSystem : EntitySystem
{
    [Dependency] private WolfmedElectricalBurnSystem _organs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, WGStrainHeartEvent>(OnStrainHeart);
    }

    private void OnStrainHeart(Entity<WoundHostComponent> ent, ref WGStrainHeartEvent args)
    {
        _organs.TryShockOrgan(ent, WolfmedElectricalBurnSystem.HeartSlot, args.Amount);
    }
}
