namespace Content.Server._WG.BoardingAction.Components;

/// <summary>A hull whose engines a disruptor has knocked out.</summary>
[RegisterComponent]
public sealed partial class WGEngineDisruptedComponent : Component
{
    /// <summary>When the engines come back.</summary>
    [ViewVariables]
    public TimeSpan Until;
}
