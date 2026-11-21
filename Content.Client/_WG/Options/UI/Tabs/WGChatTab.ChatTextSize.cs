using Content.Client.Options.UI;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._WG.ChatTextSize;

namespace Content.Client._WG.Options.UI.Tabs;

/// <summary>The chat text size and line spacing sliders on the Wolfgate Chat tab.</summary>
public sealed partial class WGChatTab
{
    private void WgAddChatTextSizeOptions()
    {
        Control.AddOptionSlider(
            ChatTextSizeCVars.Size,
            ChatTextSizeSlider,
            0,
            24,
            (_, value) => value == 0
                ? Loc.GetString("ui-options-wg-chat-text-size-default")
                : Loc.GetString("ui-options-wg-chat-text-size-value", ("size", Math.Max(value, ChatBox.MinTextSize))));
        Control.AddOptionSlider(
            ChatTextSizeCVars.LineSpacing,
            ChatLineSpacingSlider,
            0,
            12,
            (_, value) => Loc.GetString("ui-options-wg-chat-line-spacing-value", ("spacing", value)));
    }
}
