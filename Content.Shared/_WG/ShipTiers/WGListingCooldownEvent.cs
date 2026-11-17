namespace Content.Shared._WG.ShipTiers;

/// <summary>Raised on the buyer of a listing that each player may buy only once per cooldown, such as a free T0 voucher.</summary>
[DataDefinition]
public sealed partial class WGListingCooldownEvent : EntityEventArgs
{
    /// <summary>What the cooldown is tracked under; listings sharing a key share it.</summary>
    [DataField(required: true)]
    public string Key = string.Empty;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(15);
}
