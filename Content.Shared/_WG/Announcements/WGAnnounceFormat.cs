using Robust.Shared.Utility;

namespace Content.Shared._WG.Announcements;

/// <summary>Builds an announcement's chat text, so the window's preview matches what players get.</summary>
public static class WGAnnounceFormat
{
    /// <summary>The standard announcement chime, the same one upstream announcements use.</summary>
    public const string DefaultSound = "/Audio/Announcements/announce.ogg";

    public static readonly Color InfoColor = Color.FromHex("#40b0ff");
    public static readonly Color AdvisoryColor = Color.FromHex("#ffa020");
    public static readonly Color WarningColor = Color.FromHex("#ff4040");

    /// <summary>The chip's colour for its level.</summary>
    public static Color ChipColor(WGAnnounceChipLevel level, Color accent)
    {
        return level switch
        {
            WGAnnounceChipLevel.Info => InfoColor,
            WGAnnounceChipLevel.Advisory => AdvisoryColor,
            WGAnnounceChipLevel.Warning => WarningColor,
            _ => accent,
        };
    }

    /// <summary>The body text colour: the accent washed out so long text stays readable.</summary>
    public static Color BodyColor(Color accent)
    {
        return Color.InterpolateBetween(accent, Color.White, 0.7f);
    }

    /// <summary>The sender name, or the default announcer when it is blank.</summary>
    public static string SenderOrDefault(string sender)
    {
        sender = sender.Trim();
        return sender.Length > 0 ? sender : Loc.GetString("wg-announce-default-sender");
    }

    /// <summary>The body as markup: kept as written when markup is allowed and parses, escaped otherwise.</summary>
    public static string BodyMarkup(string body, bool allowMarkup)
    {
        body = body.Trim();
        if (allowMarkup && FormattedMessage.TryFromMarkup(body, out _))
            return body;

        return FormattedMessage.EscapeText(body);
    }

    /// <summary>The full chat message: title, sender line and body.</summary>
    public static string ChatMarkup(WGAnnounceRequest request)
    {
        var accent = request.Accent.ToHexNoAlpha();
        var sender = FormattedMessage.EscapeText(SenderOrDefault(request.Sender));
        var body = BodyMarkup(request.Body, request.AllowMarkup);
        var title = request.Title.Trim();

        var senderLine = Loc.GetString("wg-announce-chat-sender", ("accent", accent), ("sender", sender));
        if (title.Length == 0)
            return $"{senderLine}\n{body}";

        var titleLine = Loc.GetString("wg-announce-chat-title", ("accent", accent), ("title", FormattedMessage.EscapeText(title)));
        return $"{titleLine}\n{senderLine}\n{body}";
    }

    /// <summary>Plain text for logs and the chat history's raw message.</summary>
    public static string PlainText(WGAnnounceRequest request)
    {
        var title = request.Title.Trim();
        var body = request.Body.Trim();
        return title.Length > 0 ? $"{title}: {body}" : body;
    }
}
