using Content.Shared._WG.ChatFont;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>The font this character's speech is drawn in; validation resets an unknown one to the default.</summary>
    [DataField]
    public ProtoId<WGChatFontPrototype> ChatFont { get; set; } = WGChatFontRules.Default;

    /// <summary>Copy of this profile with the given chat font.</summary>
    public HumanoidCharacterProfile WithChatFont(ProtoId<WGChatFontPrototype> font)
    {
        return new(this) { ChatFont = font };
    }
}
