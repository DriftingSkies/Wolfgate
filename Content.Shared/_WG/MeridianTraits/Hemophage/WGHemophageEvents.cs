using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.MeridianTraits.Hemophage;

/// <summary>Puts the tumor to sleep or wakes it.</summary>
public sealed partial class WGHemophageDormantActionEvent : InstantActionEvent;

/// <summary>Toggles Hemokinetic Regeneration.</summary>
public sealed partial class WGHemophageRegenActionEvent : InstantActionEvent;

/// <summary>Clots the worst bleeding wound for blood.</summary>
public sealed partial class WGHemophageClotActionEvent : InstantActionEvent;

/// <summary>Toggles Master of the House.</summary>
public sealed partial class WGHemophageMasterActionEvent : InstantActionEvent;

/// <summary>Drinks from the living body being pulled.</summary>
public sealed partial class WGHemophageDrainActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class WGHemophageDormantDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WGHemophageDrainDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Clicking the blood gauge shows the exact amount.</summary>
public sealed partial class WGHemophageBloodAlertEvent : BaseAlertEvent;

/// <summary>Raised on a body by the Taste Suppressor reagent to dull its taste for a while.</summary>
[ByRefEvent]
public readonly record struct WGSuppressTasteEvent(TimeSpan Duration);

/// <summary>Raised on whoever just drank a sip, with what went down.</summary>
[ByRefEvent]
public readonly record struct WGDrankEvent(EntityUid Drink, Solution Drunk);
