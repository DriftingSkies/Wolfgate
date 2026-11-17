using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WG.RMCCore.Dialog;

[Serializable, NetSerializable]
public struct DialogOption(string text, object? ev = null, SpriteSpecifier? icon = null, string description = "")
{
    public string Text = text;
    public object? Event = ev;
    public SpriteSpecifier? Icon = icon;
    public string Description = description;
}
