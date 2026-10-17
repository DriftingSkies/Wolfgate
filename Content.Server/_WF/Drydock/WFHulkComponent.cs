namespace Content.Server._WF.Drydock;

/// <summary>A hull a drydock has broken part of; encounters leave it in place for a while after.</summary>
[RegisterComponent]
public sealed partial class WFHulkComponent : Component
{
    /// <summary>Last time a crane broke part of the hull.</summary>
    [ViewVariables]
    public TimeSpan LastWork;
}
