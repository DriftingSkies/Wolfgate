namespace Content.Server._WF.ColdWar;

/// <summary>
/// Escalation: TSF/PDV talks break down at a set time into the round. The war level goes HOT and the declarators can
/// no longer call it off.
/// </summary>
[RegisterComponent]
public sealed partial class WFEscalationRuleComponent : Component
{
    /// <summary>How long after the rule starts talks break down.</summary>
    [DataField]
    public TimeSpan BreakDelay = TimeSpan.FromMinutes(30);

    /// <summary>How long before the break each warning goes out, longest first.</summary>
    [DataField]
    public List<TimeSpan> Warnings = new() { TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1) };

    /// <summary>When talks break down. Null before the rule starts and once they have.</summary>
    [DataField]
    public TimeSpan? BreakAt;

    /// <summary>How many of <see cref="Warnings"/> have gone out.</summary>
    [DataField]
    public int WarningsSent;

    /// <summary>Whether talks have broken down.</summary>
    [DataField]
    public bool Broken;
}
