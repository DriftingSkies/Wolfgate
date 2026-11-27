using Content.Server._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.BoardingAction;

public sealed partial class WGHackHarpoonSystem
{
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private TagSystem _tags = default!;

    /// <summary>Share of a ship's heat capacity from which a hacking dart slips through its field.</summary>
    public const float ShieldPierceHeat = 0.5f;

    private static readonly ProtoId<TagPrototype> DartTag = "WGHackDart";

    private void InitializeShields()
    {
        SubscribeLocalEvent<WGShipShieldInterceptAttemptEvent>(OnShieldIntercept);
    }

    /// <summary>Lets a hacking dart through a field whose ship is at least half full of heat.</summary>
    private void OnShieldIntercept(ref WGShipShieldInterceptAttemptEvent args)
    {
        if (args.Cancelled || !_tags.HasTag(args.Shot, DartTag))
            return;

        var (heat, capacity) = _shields.GetWolfgateShipHeat(args.Grid);
        if (capacity > 0f && heat >= capacity * ShieldPierceHeat)
            args.Cancelled = true;
    }
}
