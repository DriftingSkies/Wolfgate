using System.Numerics;
using Content.Client._WF.RadarLabels;
using Content.Shared._WF.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Collects every tag the scope draws, then lays them out together. Quiet by default: only the tags that matter in a
/// fight show their name everywhere, the rest are blips, with a count where several crowd together. Round the cursor a
/// lens shows every tag, stacking any that would overlap into a list with lines to their contacts
/// (<see cref="WFRadarTagLayout"/>), and the tag nearest the cursor shows its detail lines. Tags fade in and out.
/// With the smart tags option off, every tag sits whole by its contact. Icons only hides every name and count until the
/// cursor rests on a contact; hover reveal off turns the cursor's lens off.
/// </summary>
public partial class ShuttleNavControl
{
    private static readonly Color WfStackMoreColor = Color.FromHex("#9aa4ad");
    private static readonly Color WfLensColor = Color.White.WithAlpha(0.06f);

    /// <summary>Radius round the cursor, in virtual pixels, inside which every tag shows.</summary>
    private const float WfLensRadius = 32f;

    /// <summary>How close, in virtual pixels, untagged blips are to count as one crowd.</summary>
    private const float WfCrowdRadius = 24f;

    /// <summary>Metres inside which a ship keeps its name anywhere on the scope: close enough to be in a fight with you.</summary>
    private const float WfTagCloseRange = 750f;

    /// <summary>Seconds a tag takes to fade in or out.</summary>
    private const float WfTagFade = 0.15f;

    private readonly WFRadarTagLayout _wfTagLayout = new();
    private readonly List<WFRadarTagLayout.Tag> _wfTags = new();
    private readonly List<WfTagDraw> _wfTagDraws = new();
    private readonly List<WfTagText> _wfTagLines = new();
    private readonly List<WfTagText> _wfTagAbove = new();
    private readonly Dictionary<string, int> _wfTagKeyCounts = new();

    private readonly List<WFRadarTagLayout.Tag> _wfShownTags = new();
    private readonly List<int> _wfShownIndex = new();
    private readonly List<int> _wfQuietIndex = new();
    private readonly Dictionary<int, float> _wfTagAlpha = new();
    private readonly HashSet<int> _wfTagSeen = new();
    private readonly List<int> _wfTagGone = new();
    private TimeSpan _wfLastTagFrame;

    private readonly record struct WfTagText(string Text, float Scale, Color Color, Vector2 Size);

    /// <summary>How a tag draws: its lines, anything drawn after its first line, and anything drawn only when it is alone.</summary>
    private struct WfTagDraw
    {
        public int FirstLine;
        public int LineCount;
        public int FirstAbove;
        public int AboveCount;
        public float HeadTextWidth;
        public bool Always;
        public bool Keyed;
        public bool Full;
        public float Alpha;
        public Action<DrawingHandleScreen, Vector2>? DrawHeadExtra;
        public Action<DrawingHandleScreen, Vector2>? DrawAlone;
    }

    /// <summary>
    /// Starts a tag for a contact at <paramref name="anchor"/>, sitting with its top-left at <paramref name="position"/>
    /// while it has room. Both are in pixels. Add its lines with <see cref="WfTagLine"/>, the first being its name.
    /// An <paramref name="always"/> tag shows its name anywhere on the scope; the rest show only under the cursor.
    /// <paramref name="key"/> names the contact from frame to frame, an entity id say; without one, the tag's name does.
    /// </summary>
    private void WfBeginTag(Vector2 anchor, Vector2 position, int priority = 0, bool always = false, int? key = null)
    {
        _wfTags.Add(new WFRadarTagLayout.Tag { Anchor = anchor, Position = position, Priority = priority, Key = HashCode.Combine(1, key ?? 0) });
        _wfTagDraws.Add(new WfTagDraw { FirstLine = _wfTagLines.Count, FirstAbove = _wfTagAbove.Count, Always = always, Keyed = key != null, Full = true, Alpha = 1f });
    }

    /// <summary>Adds a line over the current tag's first line, each one higher than the last; shown only with its details.</summary>
    private void WfTagAbove(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wfTagAbove.Add(new WfTagText(text, scale, color, size));
        var index = _wfTags.Count - 1;
        var tag = _wfTags[index];
        tag.Rise += size.Y;
        tag.Size.X = MathF.Max(tag.Size.X, size.X);
        _wfTags[index] = tag;
        var draw = _wfTagDraws[index];
        draw.AboveCount++;
        _wfTagDraws[index] = draw;
    }

    /// <summary>Adds a line under the current tag's others; the first line is its name, the rest are details.</summary>
    private void WfTagLine(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wfTagLines.Add(new WfTagText(text, scale, color, size));
        var index = _wfTags.Count - 1;
        var tag = _wfTags[index];
        var draw = _wfTagDraws[index];
        if (draw.LineCount == 0)
        {
            draw.HeadTextWidth = size.X;
            tag.HeadSize += size;
        }

        draw.LineCount++;
        tag.Size = new Vector2(MathF.Max(tag.Size.X, size.X), tag.Size.Y + size.Y);
        _wfTags[index] = tag;
        _wfTagDraws[index] = draw;
    }

    /// <summary>Reserves <paramref name="width"/> pixels after the current tag's first line, drawn by <paramref name="draw"/> from that line's top-right.</summary>
    private void WfTagHeadExtra(float width, Action<DrawingHandleScreen, Vector2> draw)
    {
        var index = _wfTags.Count - 1;
        var tag = _wfTags[index];
        tag.HeadSize.X += width;
        tag.Size.X = MathF.Max(tag.Size.X, tag.HeadSize.X);
        _wfTags[index] = tag;
        var info = _wfTagDraws[index];
        info.DrawHeadExtra = draw;
        _wfTagDraws[index] = info;
    }

    /// <summary>Adds drawing the current tag does only while it stands alone, given its position.</summary>
    private void WfTagAlone(Action<DrawingHandleScreen, Vector2> draw)
    {
        var index = _wfTags.Count - 1;
        var info = _wfTagDraws[index];
        info.DrawAlone += draw;
        _wfTagDraws[index] = info;
    }

    /// <summary>Lays out and draws the tags collected this frame, over everything else on the scope.</summary>
    private void WfDrawTags(DrawingHandleScreen handle)
    {
        var now = IoCManager.Resolve<IGameTiming>().RealTime;
        var step = Math.Clamp((float) (now - _wfLastTagFrame).TotalSeconds, 0f, 0.1f) / WfTagFade;
        _wfLastTagFrame = now;

        WfDropTagsOffScope();
        if (_wfTags.Count == 0)
        {
            WfClearTags();
            return;
        }

        var iconsOnly = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarIconsOnly);
        if (!iconsOnly && !_wfSettingsCfg.GetCVar(WolfgateCVars.RadarSmartTags))
        {
            // Smart tags off: every tag whole by its contact, overlapping or not.
            for (var i = 0; i < _wfTags.Count; i++)
                DrawTagAlone(handle, i);

            WfClearTags();
            return;
        }

        WfKeyTags();
        var allNames = _wfAllNames;

        // The lens: every tag inside it shows, and the one nearest the cursor shows its details. With icons only, just
        // that nearest one shows.
        var hover = _isMouseInside && WfHoverReveal;
        Vector2? cursor = hover ? (UserInterfaceManager.MousePositionScaled.Position - GlobalPosition) * UIScale : null;
        var lens = WfLensRadius * UIScale;
        var focus = -1;
        var nearest = lens * lens;
        if (cursor is { } point)
        {
            if (!iconsOnly)
                handle.DrawCircle(point, lens, WfLensColor, false);

            for (var i = 0; i < _wfTags.Count; i++)
            {
                var distance = (_wfTags[i].Anchor - point).LengthSquared();
                if (distance > nearest)
                    continue;

                nearest = distance;
                focus = i;
            }
        }

        _wfShownTags.Clear();
        _wfShownIndex.Clear();
        _wfQuietIndex.Clear();
        _wfTagSeen.Clear();
        for (var i = 0; i < _wfTags.Count; i++)
        {
            var tag = _wfTags[i];
            var draw = _wfTagDraws[i];
            var shown = allNames || i == focus
                || !iconsOnly && (draw.Always || cursor is { } at && (tag.Anchor - at).LengthSquared() <= lens * lens);
            draw.Full = i == focus;
            if (!draw.Full)
            {
                // Name only: the detail lines and anything over the name wait for the cursor.
                tag.Size = tag.HeadSize;
                tag.Rise = 0f;
                _wfTags[i] = tag;
            }

            var alpha = _wfTagAlpha.GetValueOrDefault(tag.Key);
            alpha = shown ? MathF.Min(1f, alpha + step) : MathF.Max(0f, alpha - step);
            _wfTagAlpha[tag.Key] = alpha;
            _wfTagSeen.Add(tag.Key);
            draw.Alpha = alpha;
            _wfTagDraws[i] = draw;

            if (shown)
            {
                _wfShownTags.Add(tag);
                _wfShownIndex.Add(i);
            }
            else
            {
                _wfQuietIndex.Add(i);
            }
        }

        // Forget tags whose contacts are gone.
        _wfTagGone.Clear();
        foreach (var key in _wfTagAlpha.Keys)
        {
            if (!_wfTagSeen.Contains(key))
                _wfTagGone.Add(key);
        }

        foreach (var key in _wfTagGone)
            _wfTagAlpha.Remove(key);

        WfDrawQuietTags(handle, !iconsOnly);
        WfDrawShownTags(handle);
        WfClearTags();
    }

    /// <summary>
    /// Gives each tag without a contact key one it keeps from frame to frame: its name, numbered when names repeat.
    /// </summary>
    private void WfKeyTags()
    {
        _wfTagKeyCounts.Clear();
        for (var i = 0; i < _wfTags.Count; i++)
        {
            var draw = _wfTagDraws[i];
            if (draw.Keyed)
                continue;

            var name = draw.LineCount > 0 ? _wfTagLines[draw.FirstLine].Text : string.Empty;
            var seen = _wfTagKeyCounts.GetValueOrDefault(name);
            _wfTagKeyCounts[name] = seen + 1;
            var tag = _wfTags[i];
            tag.Key = HashCode.Combine(name, seen);
            _wfTags[i] = tag;
        }
    }

    /// <summary>
    /// Quiet tags out of the lens: one still fading out keeps its name a moment; blips crowded together get a count
    /// unless <paramref name="counts"/> is off.
    /// </summary>
    private void WfDrawQuietTags(DrawingHandleScreen handle, bool counts)
    {
        foreach (var index in _wfQuietIndex)
        {
            if (_wfTagDraws[index].Alpha > 0f)
                DrawTagAlone(handle, index);
        }

        if (!counts)
            return;

        var crowd = WfCrowdRadius * UIScale;
        var scale = UIScale * 0.7f;
        for (var i = 0; i < _wfQuietIndex.Count; i++)
        {
            var seed = _wfQuietIndex[i];
            if (seed < 0)
                continue;

            var sum = _wfTags[seed].Anchor;
            var count = 1;
            for (var j = i + 1; j < _wfQuietIndex.Count; j++)
            {
                var other = _wfQuietIndex[j];
                if (other < 0 || (_wfTags[other].Anchor - _wfTags[seed].Anchor).LengthSquared() > crowd * crowd)
                    continue;

                sum += _wfTags[other].Anchor;
                count++;
                _wfQuietIndex[j] = -1;
            }

            if (count < 2)
                continue;

            var middle = sum / count;
            handle.DrawString(Font, middle + new Vector2(6f, -14f) * UIScale, Loc.GetString("wf-radar-tags-crowd", ("count", count)),
                scale, WfStackMoreColor);
        }
    }

    /// <summary>The tags that show: alone by their contacts where there is room, stacked where they would overlap.</summary>
    private void WfDrawShownTags(DrawingHandleScreen handle)
    {
        if (_wfShownTags.Count == 0)
            return;

        var moreScale = UIScale * 0.7f;
        _wfTagLayout.Gap = 3f * UIScale;
        _wfTagLayout.StickyGap = 10f * UIScale;
        _wfTagLayout.SwapMargin = 8f * UIScale;
        _wfTagLayout.MoreHeight = handle.GetDimensions(Font, "+", moreScale).Y;
        _wfTagLayout.Solve(_wfShownTags, PixelSize);

        foreach (var group in _wfTagLayout.Groups)
        {
            if (!group.Stacked)
            {
                DrawTagAlone(handle, _wfShownIndex[group.All[0]]);
                continue;
            }

            var box = group.Box;
            var backing = 0f;
            foreach (var row in group.Rows)
                backing = MathF.Max(backing, _wfTagDraws[_wfShownIndex[row]].Alpha);

            // A plain list, no backing or lines: the rows read top to bottom in the order of their contacts.
            var y = box.Bottom;
            foreach (var row in group.Rows)
            {
                var index = _wfShownIndex[row];
                var tag = _wfTags[index];
                var draw = _wfTagDraws[index];
                var line = _wfTagLines[draw.FirstLine];
                var at = new Vector2(box.Left, y);
                handle.DrawString(Font, at, line.Text, line.Scale, Fade(line.Color, draw.Alpha));
                draw.DrawHeadExtra?.Invoke(handle, at + new Vector2(draw.HeadTextWidth, 0f));
                y += tag.HeadSize.Y;
            }

            if (group.Hidden > 0)
            {
                handle.DrawString(Font, new Vector2(box.Left, y), Loc.GetString("wf-radar-tags-more", ("count", group.Hidden)),
                    moreScale, Fade(WfStackMoreColor, backing));
            }
        }
    }

    /// <summary>Whether hovering a contact reveals its tag.</summary>
    private bool WfHoverReveal => _wfSettingsCfg.GetCVar(WolfgateCVars.RadarHoverReveal);

    private static Color Fade(Color color, float alpha) => color.WithAlpha(color.A * alpha);

    private void WfClearTags()
    {
        _wfTags.Clear();
        _wfTagDraws.Clear();
        _wfTagLines.Clear();
        _wfTagAbove.Clear();
    }

    /// <summary>Whether a point, in pixels, lies on the scope: inside the ellipse it draws within, not out at its rim or corners.</summary>
    private bool WfOnScope(Vector2 point)
    {
        var half = new Vector2(PixelSize.X, PixelSize.Y) / 2f;
        if (half.X <= 0f || half.Y <= 0f)
            return false;

        var offset = (point - half) / half;
        return offset.LengthSquared() <= 1f;
    }

    /// <summary>Drops the tags of contacts off the scope, so a stack lists only what the scope shows.</summary>
    private void WfDropTagsOffScope()
    {
        var kept = 0;
        for (var i = 0; i < _wfTags.Count; i++)
        {
            if (!WfOnScope(_wfTags[i].Anchor))
                continue;

            _wfTags[kept] = _wfTags[i];
            _wfTagDraws[kept] = _wfTagDraws[i];
            kept++;
        }

        _wfTags.RemoveRange(kept, _wfTags.Count - kept);
        _wfTagDraws.RemoveRange(kept, _wfTagDraws.Count - kept);
    }

    /// <summary>Draws a tag by its contact: whole, or its name alone, faded as it comes and goes.</summary>
    private void DrawTagAlone(DrawingHandleScreen handle, int index)
    {
        var tag = _wfTags[index];
        var draw = _wfTagDraws[index];
        var at = tag.Position;
        var lines = draw.Full ? draw.LineCount : Math.Min(1, draw.LineCount);
        for (var i = 0; i < lines; i++)
        {
            var line = _wfTagLines[draw.FirstLine + i];
            handle.DrawString(Font, at, line.Text, line.Scale, Fade(line.Color, draw.Alpha));
            if (i == 0)
                draw.DrawHeadExtra?.Invoke(handle, at + new Vector2(draw.HeadTextWidth, 0f));

            at.Y += line.Size.Y;
        }

        if (draw.Full)
        {
            var above = tag.Position;
            for (var i = 0; i < draw.AboveCount; i++)
            {
                var line = _wfTagAbove[draw.FirstAbove + i];
                above.Y -= line.Size.Y;
                handle.DrawString(Font, above, line.Text, line.Scale, Fade(line.Color, draw.Alpha));
            }
        }

        draw.DrawAlone?.Invoke(handle, tag.Position);
    }
}
