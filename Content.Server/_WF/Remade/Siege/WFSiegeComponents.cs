namespace Content.Server._WF.Remade.Siege;

/// <summary>On a Scion that has reached Harvest: when its next wave of flesh ships comes.</summary>
[RegisterComponent]
public sealed partial class WFSiegeComponent : Component
{
    [ViewVariables]
    public TimeSpan NextWave;
}

/// <summary>A flesh ship sent against a reality anchor.</summary>
[RegisterComponent]
public sealed partial class WFNecroticShipComponent : Component
{
    /// <summary>The anchor core it is after.</summary>
    [ViewVariables]
    public EntityUid Core;

    /// <summary>The station the core stands on.</summary>
    [ViewVariables]
    public EntityUid Station;

    /// <summary>The encounter that brought it, which holds its crew's orders.</summary>
    [ViewVariables]
    public EntityUid Encounter;

    /// <summary>When its encounter is ended and the ship goes.</summary>
    [ViewVariables]
    public TimeSpan Expires;

    [ViewVariables]
    public TimeSpan NextBoard;

    [ViewVariables]
    public TimeSpan NextProvoke;

    /// <summary>Remade it has put aboard the station.</summary>
    [ViewVariables]
    public List<EntityUid> Boarders = new();
}
