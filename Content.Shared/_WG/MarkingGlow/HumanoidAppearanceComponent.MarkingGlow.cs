namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// What of this body glows: marking IDs and the eye and hair keys of
    /// <see cref="Content.Shared._WG.MarkingGlow.MarkingGlowRules"/>. The client draws those layers unshaded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> MarkingGlow = new();
}
