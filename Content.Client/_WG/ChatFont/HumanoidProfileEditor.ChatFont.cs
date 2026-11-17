using Content.Shared._WG.ChatFont;
using Content.Client._WG.ChatFont;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

/// <summary>The Chat font dropdown in the Body card, with a line of sample speech in the picked font.</summary>
public sealed partial class HumanoidProfileEditor
{
    private List<WGChatFontPrototype> _wgChatFonts = new();

    private void InitializeChatFont()
    {
        _wgChatFonts = WGChatFontRules.Ordered(_prototypeManager);
        for (var i = 0; i < _wgChatFonts.Count; i++)
        {
            ChatFontButton.AddItem(Loc.GetString(_wgChatFonts[i].Name), i);
        }

        ChatFontButton.OnItemSelected += args =>
        {
            ChatFontButton.SelectId(args.Id);
            SetChatFont(_wgChatFonts[args.Id]);
        };
    }

    /// <summary>Shows the profile's chat font, the default when it names none that exists.</summary>
    private void UpdateChatFont()
    {
        if (Profile == null)
            return;

        var index = _wgChatFonts.FindIndex(f => f.ID == Profile.ChatFont.Id);
        if (index < 0)
            index = _wgChatFonts.FindIndex(f => f.ID == WGChatFontRules.Default.Id);

        if (index < 0)
            return;

        ChatFontButton.SelectId(index);
        UpdateChatFontPreview(_wgChatFonts[index]);
    }

    private void SetChatFont(WGChatFontPrototype font)
    {
        UpdateChatFontPreview(font);
        if (Profile == null || Profile.ChatFont == font.ID)
            return;

        Profile = Profile.WithChatFont(font.ID);
        SetDirty();
    }

    private void UpdateChatFontPreview(WGChatFontPrototype font)
    {
        var sample = FormattedMessage.EscapeText(Loc.GetString("wg-chat-font-preview"));
        // The default tag set leaves out font, so the sample would draw in the label's own.
        // Sized as speech is: the speech verb's 12, scaled for the font.
        var size = Math.Max(1, (int) MathF.Round(12 * font.SizeScale));
        ChatFontPreview.SetMessage(FormattedMessage.FromMarkupOrThrow($"[font=\"{font.Font}\" size={size}]{sample}[/font]"),
            WGChatFontTags.Allowed);
    }
}
