namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// Loaded from a profile with Mismatched parts on: its markings draw even where the species has no layer for them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool MismatchedParts;
}
