using Content.Server._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;

namespace Content.Server._WG.ShipShields;

/// <summary>Applies beam impacts after shared shield interception.</summary>
public sealed partial class WGShipShieldHitscanDamageSystem : EntitySystem
{
    [Dependency] private ShipShieldsSystem _shields = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipShieldVisualsComponent, WGShipShieldHitscanImpactEvent>(OnImpact);
    }

    private void OnImpact(Entity<WGShipShieldVisualsComponent> ent, ref WGShipShieldHitscanImpactEvent args)
    {
        _shields.ApplyWolfgateHitscanImpact(ent.Owner, args.Hitscan, args.Position, args.Strength, args.Gun, args.Shooter);
    }
}
