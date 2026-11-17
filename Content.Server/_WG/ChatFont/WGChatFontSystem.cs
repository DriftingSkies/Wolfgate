using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WG.ChatFont;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ChatFont;

/// <summary>Gives a character the chat font picked in their profile; chat, radio and telephone wraps read it.</summary>
public sealed partial class WGChatFontSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpeechComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
    }

    private void OnProfileLoadFinished(Entity<SpeechComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (args.Profile == null)
            return;

        var scale = _proto.TryIndex(args.Profile.ChatFont, out var picked) ? picked.SizeScale : 1f;
        SetFont(ent, WGChatFontRules.ClientFont(args.Profile.ChatFont, _proto), scale);
    }

    /// <summary>Draws the entity's speech in the client font at a scaled size, or in the speech verb's own when null.</summary>
    public void SetFont(EntityUid uid, string? font, float sizeScale = 1f)
    {
        if (font == null)
        {
            RemComp<WGChatFontComponent>(uid);
            return;
        }

        var comp = EnsureComp<WGChatFontComponent>(uid);
        comp.Font = font;
        comp.SizeScale = sizeScale;
        Dirty(uid, comp);
    }

    /// <summary>The font the entity's speech is drawn in, or null for the speech verb's own.</summary>
    public string? GetFont(EntityUid uid)
    {
        return CompOrNull<WGChatFontComponent>(uid)?.Font;
    }
}
