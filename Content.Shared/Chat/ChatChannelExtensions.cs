namespace Content.Shared.Chat;

public static class ChatChannelExtensions
{
    public static readonly Color SubtleColor = Color.FromHex("#c9a9d6"); // WOLFGATE(Subtle): subtle emote tint

    public static Color TextColor(this ChatChannel channel)
    {
        return channel switch
        {
            ChatChannel.Server => Color.Orange,
            ChatChannel.Radio => Color.LimeGreen,
            ChatChannel.LOOC => Color.MediumTurquoise,
            ChatChannel.OOC => Color.LightSkyBlue,
            ChatChannel.Dead => Color.MediumPurple,
            ChatChannel.Admin => Color.Red,
            ChatChannel.AdminAlert => Color.Red,
            ChatChannel.AdminChat => Color.HotPink,
            ChatChannel.EventChat => Color.Gold, // WOLFGATE(EventStaff)
            ChatChannel.Subtle => SubtleColor, // WOLFGATE(Subtle)
            ChatChannel.Whisper => Color.DarkGray,
            _ => Color.LightGray
        };
    }
}
