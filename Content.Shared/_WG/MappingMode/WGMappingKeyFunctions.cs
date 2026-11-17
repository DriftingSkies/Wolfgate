using Robust.Shared.Input;

namespace Content.Shared._WG.MappingMode;

/// <summary>Keys of the mapping screen: build modes, the mode's mouse buttons, template nudges and the link tool.</summary>
[KeyFunctions]
public static class WGMappingKeyFunctions
{
    public static readonly BoundKeyFunction WGMappingMode1 = "WGMappingMode1";
    public static readonly BoundKeyFunction WGMappingMode2 = "WGMappingMode2";
    public static readonly BoundKeyFunction WGMappingMode3 = "WGMappingMode3";
    public static readonly BoundKeyFunction WGMappingMode4 = "WGMappingMode4";
    public static readonly BoundKeyFunction WGMappingMode5 = "WGMappingMode5";
    public static readonly BoundKeyFunction WGMappingMode6 = "WGMappingMode6";
    public static readonly BoundKeyFunction WGMappingMode7 = "WGMappingMode7";
    public static readonly BoundKeyFunction WGMappingMode8 = "WGMappingMode8";
    public static readonly BoundKeyFunction WGMappingMode9 = "WGMappingMode9";
    public static readonly BoundKeyFunction WGMappingMode10 = "WGMappingMode10";
    public static readonly BoundKeyFunction WGMappingModePrimary = "WGMappingModePrimary";
    public static readonly BoundKeyFunction WGMappingModeSecondary = "WGMappingModeSecondary";
    public static readonly BoundKeyFunction WGMappingModeMiddle = "WGMappingModeMiddle";
    public static readonly BoundKeyFunction WGMappingModeCancel = "WGMappingModeCancel";
    public static readonly BoundKeyFunction WGMappingTemplateNudgeUp = "WGMappingTemplateNudgeUp";
    public static readonly BoundKeyFunction WGMappingTemplateNudgeDown = "WGMappingTemplateNudgeDown";
    public static readonly BoundKeyFunction WGMappingTemplateNudgeLeft = "WGMappingTemplateNudgeLeft";
    public static readonly BoundKeyFunction WGMappingTemplateNudgeRight = "WGMappingTemplateNudgeRight";

    /// <summary>Tab in Link mode: the next sub-tool.</summary>
    public static readonly BoundKeyFunction WGMappingLinkCycle = "WGMappingLinkCycle";

    /// <summary>Ctrl+S: saves the session to the player's library under its name, as the menu bar's Quicksave.</summary>
    public static readonly BoundKeyFunction WGMappingQuicksave = "WGMappingQuicksave";

    /// <summary>Leaves a live test for the mapping observer, as the test banner's button; bound to nothing by default.</summary>
    public static readonly BoundKeyFunction WGMappingTestReturn = "WGMappingTestReturn";

    /// <summary>The mode keys, Place first.</summary>
    public static readonly BoundKeyFunction[] Modes =
    {
        WGMappingMode1,
        WGMappingMode2,
        WGMappingMode3,
        WGMappingMode4,
        WGMappingMode5,
        WGMappingMode6,
        WGMappingMode7,
        WGMappingMode8,
        WGMappingMode9,
        WGMappingMode10,
    };

    /// <summary>The template's arrow keys, added while Template mode is on.</summary>
    public static readonly BoundKeyFunction[] Nudges =
    {
        WGMappingTemplateNudgeUp,
        WGMappingTemplateNudgeDown,
        WGMappingTemplateNudgeLeft,
        WGMappingTemplateNudgeRight,
    };
}
