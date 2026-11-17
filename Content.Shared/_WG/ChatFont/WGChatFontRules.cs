using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.ChatFont;

/// <summary>Validation, storage and lookup of a profile's chat font.</summary>
public static class WGChatFontRules
{
    /// <summary>The font every profile starts with: the speech verb's own.</summary>
    public static readonly ProtoId<WGChatFontPrototype> Default = "WGChatFontDefault";

    /// <summary>The font if it exists, otherwise <see cref="Default"/>.</summary>
    public static ProtoId<WGChatFontPrototype> Validate(ProtoId<WGChatFontPrototype> font, IPrototypeManager proto)
    {
        return proto.HasIndex(font) ? font : Default;
    }

    /// <summary>The font as stored in the database.</summary>
    public static string ToStored(ProtoId<WGChatFontPrototype> font)
    {
        return font.Id;
    }

    /// <summary>The font read back from the database; an empty value is the default.</summary>
    public static ProtoId<WGChatFontPrototype> FromStored(string? stored)
    {
        return string.IsNullOrEmpty(stored) ? Default : new ProtoId<WGChatFontPrototype>(stored);
    }

    /// <summary>The client font a picked chat font draws speech in, or null for the speech verb's own.</summary>
    public static string? ClientFont(ProtoId<WGChatFontPrototype> font, IPrototypeManager proto)
    {
        if (font == Default || !proto.TryIndex(font, out var prototype))
            return null;

        return prototype.Font;
    }

    /// <summary>A speech size scaled for the speaker's picked font, unchanged when none is picked.</summary>
    public static int ScaleSize(int size, WGChatFontComponent? font)
    {
        return font == null ? size : Math.Max(1, (int) MathF.Round(size * font.SizeScale));
    }

    /// <summary>Every chat font in dropdown order.</summary>
    public static List<WGChatFontPrototype> Ordered(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WGChatFontPrototype>()
            .OrderBy(p => p.Order)
            .ThenBy(p => p.ID)
            .ToList();
    }
}
