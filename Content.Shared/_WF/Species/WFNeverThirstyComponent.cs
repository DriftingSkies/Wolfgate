namespace Content.Shared._WF.Species;

/// <summary>
/// Keeps a mob's thirst from falling below Okay, whatever drains it, so a species that never needs to drink never gets
/// thirsty or dries out.
/// </summary>
[RegisterComponent]
public sealed partial class WFNeverThirstyComponent : Component;
