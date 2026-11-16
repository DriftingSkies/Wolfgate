using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared.Chat;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WF.ChatTabs;

/// <summary>
///     Generates formatted chat log files from the chat history.
/// </summary>
public static class ChatLogSaver
{
    /// <summary>
    ///     Saves the chat log for the given tab as an HTML file.
    /// </summary>
    public static async Task SaveChatLogAsync(Stream stream, ChatUIController controller, ChatTab tab)
    {
        var channels = tab.Channels;
        var sb = new StringBuilder();

        sb.Append(
            "<!DOCTYPE html>\n" +
            "<html lang=\"en\">\n" +
            "<head>\n" +
            "  <meta charset=\"utf-8\">\n" +
            $"  <title>{Loc.GetString("wf-chat-log")} \u2013 {HtmlEncode(tab.Name)}</title>\n" +
            "  <style>\n" +
            "    body { font-family: 'Consolas', 'Monaco', monospace; background: #2B2B2B; color: #d4d4d4; padding: 16px; }\n" +
            "    .header { color: #888; margin-bottom: 8px; }\n" +
            "    .line { margin: 2px 0; }\n" +
            "    .msg { font-size: 12px; white-space: pre-wrap; word-wrap: break-word; }\n" +
            "    a { color: #9cdcfe; }\n" +
            "  </style>\n" +
            "</head>\n" +
            "<body>\n" +
            $"  <div class=\"header\">{Loc.GetString("wf-chat-log")} \u2013 {HtmlEncode(tab.Name)}</div>\n");

        foreach (var (_, msg) in controller.History)
        {
            if ((msg.Channel & channels) == 0)
                continue;

            var color = msg.MessageColorOverride ?? msg.Channel.TextColor();
            var colorHex = color.ToHex();

            sb.Append($"  <div class=\"line\"><span class=\"msg\" style=\"color:{colorHex}\">");

            // Parse the wrapped message and convert to HTML.
            // If strict parsing fails, fall back to the permissive parser.
            var message = msg.WrappedMessage;
            var success = FormattedMessage.TryFromMarkup(message, out var formattedMsg);
            IReadOnlyList<MarkupNode>? nodes = null;

            if (success && formattedMsg != null)
            {
                nodes = formattedMsg.Nodes;
            }
            else
            {
                // Permissive parser: parses invalid markup as text.
                var permissiveError = (string?) null;
                nodes = FormattedMessage.ParsePermissive(message, out permissiveError);
                success = true;
            }

            if (success && nodes != null)
            {
                sb.Append(ConvertNodesToHtml(nodes));
            }
            else
            {
                sb.Append(HtmlEncode(message));
            }

            sb.Append("</span></div>\n");
        }

        sb.Append("</body>\n</html>\n");

        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(sb.ToString());
    }

    /// <summary>
    ///     Converts a list of <see cref="MarkupNode"/> to HTML using a flat pass with a tag stack.
    /// </summary>
    private static string ConvertNodesToHtml(IReadOnlyList<MarkupNode> nodes)
    {
        var sb = new StringBuilder();
        var tagStack = new Stack<string>();

        foreach (var node in nodes)
        {
            if (node.IsPlainText)
            {
                // Text node: HTML-encode the content.
                sb.Append(HtmlEncode(node.Value.StringValue ?? ""));
            }
            else if (node.Closing)
            {
                // Closing tag: pop the matching HTML closing tag from the stack.
                if (tagStack.Count > 0)
                    sb.Append(tagStack.Pop());
            }
            else
            {
                // Opening tag: emit opening HTML tag and push its closing tag onto the stack.
                var htmlTag = MarkupTagToHtml(node);
                sb.Append(htmlTag.opening);
                tagStack.Push(htmlTag.closing);

                // Bullet tag outputs a " · " prefix before its content.
                if (node.Name == "bullet")
                    sb.Append(" \u00b7 ");
            }
        }

        while (tagStack.Count > 0)
            sb.Append(tagStack.Pop());
        return sb.ToString();
    }

    /// <summary>
    ///     Maps a Wolfgate markup tag to an HTML opening tag with attributes.
    /// </summary>
    private static (string opening, string closing) MarkupTagToHtml(MarkupNode node)
    {
        var name = node.Name!;
        var param = node.Value;
        var attrs = node.Attributes;

        return name switch
        {
            // [color=red] — value is a Color, not a string.
            "color" => (
                $"<span style=\"color:{ResolveColor(param)}\">",
                "</span>"
            ),
            // [bold]
            "bold" => ("<b>", "</b>"),
            // [italic]
            "italic" => ("<i>", "</i>"),
            // [bolditalic]
            "bolditalic" => ("<b><i>", "</i></b>"),
            // [bullet] — outputs a bullet prefix before the text.
            "bullet" => ("", ""),
            // [head=1] … [head=3] — heading levels.
            "head" => ResolveHead(param, attrs),
            // [font=...] size=...
            "font" => ResolveFont(param, attrs),
            // [cmdlink=...] keep event chat link formatting
            "cmdlink" => (
                $"<span style=\"color:#9cdcfe\">",
                "</span>"
            ),
            // Chat-specific tags.
            "Name" => (
                $"<span style=\"color:{ResolveColor(param)}\">",
                "</span>"
            ),
            // Ghost orbit links from event staff.
            "WFGhostOrbitLink" => (
                $"<span style=\"color:#9cdcfe\">",
                "</span>"
            ),
            // Unknown tag: render as a span with no special styling.
            _ => ("<span>", "</span>")
        };
    }

    /// <summary>
    ///     Resolves a MarkupParameter to a CSS color string.
    /// </summary>
    private static string ResolveColor(MarkupParameter param)
    {
        if (param.TryGetColor(out var color))
            return color.Value.ToHex();

        if (param.TryGetString(out var str) && !string.IsNullOrEmpty(str))
            return EscapeAttr(str);

        return "inherit";
    }

    /// <summary>
    ///     Resolves a [head] tag to an <h1>–<h3> opening tag.
    /// </summary>
    private static (string opening, string closing) ResolveHead(MarkupParameter param, Dictionary<string, MarkupParameter> attrs)
    {
        var level = 1;
        if (param.TryGetLong(out var longVal))
            level = Math.Clamp((int)longVal, 1, 3);
        else if (attrs.TryGetValue("size", out var sizeParam) && sizeParam.TryGetLong(out var sizeVal))
            level = Math.Clamp((int)sizeVal, 1, 3);

        return ($"<h{level}>", $"</h{level}>");
    }

    /// <summary>
    ///     Resolves a [font=...] tag to a <span> with font-family and optional font-size.
    /// </summary>
    private static (string opening, string closing) ResolveFont(MarkupParameter param, Dictionary<string, MarkupParameter> attrs)
    {
        var fontFamily = EscapeAttr(param.StringValue ?? "inherit");
        var style = $"font-family:{fontFamily}";

        if (attrs.TryGetValue("size", out var sizeParam) && sizeParam.TryGetLong(out var sizeVal))
            style += $";font-size:{sizeVal}px";

        return ($"<span style=\"{style}\">", "</span>");
    }

    /// <summary>
    ///     HTML-encodes a string for safe inclusion in text content.
    /// </summary>
    private static string HtmlEncode(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => c
            });
        }
        return sb.ToString();
    }

    /// <summary>
    ///     HTML-encodes a string for safe inclusion in an attribute value.
    /// </summary>
    private static string EscapeAttr(string value)
        => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");


}
