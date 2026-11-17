using Content.Server._WG.ShipCommand;

namespace Content.Server.Cargo.Systems;

/// <summary>Something a commanded ship's service sold below its list price resells for no more than was paid.</summary>
public sealed partial class PricingSystem
{
    /// <summary>Set while pricing as if nothing had been bought at a bargain.</summary>
    private bool _wgIgnoreBargains;

    /// <summary>An entity's own price less the bargain it was bought at, never below zero.</summary>
    private double WgBargainPrice(EntityUid uid, double price)
    {
        if (_wgIgnoreBargains || !TryComp<WGBargainComponent>(uid, out var bargain))
            return price;

        return Math.Max(0, price - bargain.Discount);
    }

    /// <summary>
    /// An entity's price as if nothing in it had been bought at a bargain: what a repair or a value cap goes by, where
    /// <see cref="GetPrice"/> is what it resells for.
    /// </summary>
    public double GetUndiscountedPrice(EntityUid uid, bool includeContents = true)
    {
        var outer = _wgIgnoreBargains;
        _wgIgnoreBargains = true;
        try
        {
            return GetPrice(uid, includeContents);
        }
        finally
        {
            _wgIgnoreBargains = outer;
        }
    }
}
