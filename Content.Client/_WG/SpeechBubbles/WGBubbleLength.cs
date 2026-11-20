using Content.Shared._WG.SpeechBubbles;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._WG.SpeechBubbles;

/// <summary>Cuts speech bubble text to the player's maximum length without breaking its markup.</summary>
public static class WGBubbleLength
{
    /// <summary>Returns <paramref name="msg"/> cut to the configured length, or unchanged if it fits.</summary>
    public static FormattedMessage Truncate(FormattedMessage msg)
    {
        var max = IoCManager.Resolve<IConfigurationManager>().GetCVar(SpeechBubbleCVars.MaxLength);
        return max > 0 ? Truncate(msg, Math.Max(max, MinLength)) : msg;
    }

    /// <summary>The shortest a bubble is cut to, so a low setting still leaves something readable.</summary>
    public const int MinLength = 20;

    /// <summary>Keeps the first <paramref name="max"/> characters of text, adds an ellipsis and closes open tags.</summary>
    public static FormattedMessage Truncate(FormattedMessage msg, int max)
    {
        var length = 0;
        foreach (var node in msg.Nodes)
        {
            if (node.Name == null && node.Value.TryGetString(out var text))
                length += text.Length;
        }

        if (length <= max)
            return msg;

        var result = new FormattedMessage();
        var open = 0;
        var left = max;
        foreach (var node in msg.Nodes)
        {
            if (node.Name == null)
            {
                if (!node.Value.TryGetString(out var text))
                    continue;

                if (text.Length < left)
                {
                    result.AddText(text);
                    left -= text.Length;
                    continue;
                }

                result.AddText(text[..left].TrimEnd() + "…");
                break;
            }

            if (node.Closing)
            {
                if (open == 0)
                    continue;

                result.Pop();
                open--;
                continue;
            }

            result.PushTag(node);
            open++;
        }

        for (; open > 0; open--)
        {
            result.Pop();
        }

        return result;
    }
}
