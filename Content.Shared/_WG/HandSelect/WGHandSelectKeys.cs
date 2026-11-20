using Robust.Shared.Input;

namespace Content.Shared._WG.HandSelect;

/// <summary>Keys that make a given hand the active one.</summary>
[KeyFunctions]
public static class WGHandSelectKeys
{
    /// <summary>Makes the first left hand active.</summary>
    public static readonly BoundKeyFunction WGSelectLeftHand = "WGSelectLeftHand";

    /// <summary>Makes the first right hand active.</summary>
    public static readonly BoundKeyFunction WGSelectRightHand = "WGSelectRightHand";
}
