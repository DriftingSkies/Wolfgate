namespace Content.Shared._WG.IdReissue;

/// <summary>A trader who reissues lost IDs through the <c>ReissueId</c> dialogue option.</summary>
[RegisterComponent]
public sealed partial class WGTraderIdReissueComponent : Component
{
    /// <summary>What a reissue costs: cash on the counter first, else the customer's bank.</summary>
    [DataField]
    public int Fee = 500;

    /// <summary>How far, in metres, a ship may lie from the trader's grid and still have its deed moved; docked ships always count.</summary>
    [DataField]
    public float Range = 150f;

    /// <summary>How long a character waits after one reissue before the next.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    /// <summary>The customer asked to confirm the quote, until they answer.</summary>
    [ViewVariables]
    public EntityUid? Pending;
}
