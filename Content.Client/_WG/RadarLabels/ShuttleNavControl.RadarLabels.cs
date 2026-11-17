using System.Numerics;
using Content.Client._WG.RadarLabels;
using Content.Shared._WG.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Collects every tag the scope draws, then lays them out together. Quiet by default: only the tags that matter in a
/// fight show their name everywhere, the rest are blips, with a count where several crowd together. Round the cursor a
/// lens shows every tag, stacking any that would overlap into a list with lines to their contacts
/// (<see cref="WGRadarTagLayout"/>), and the tag nearest the cursor shows its detail lines. Tags fade in and out.
/// With the smart tags option off, every tag sits whole by its contact. Icons only hides every name and count until the
/// cursor rests on a contact; hover reveal off turns the cursor's lens off.
/// </summary>
public partial class ShuttleNavControl
{
    private static readonly Color WgStackMoreColor = Color.FromHex("#9aa4ad");
    private static readonly Color WgLensColor = Color.White.WithAlpha(0.06f);

    /// <summary>Radius round the cursor, in virtual pixels, inside which every tag shows.</summary>
    private const float WgLensRadius = 32f;

    /// <summary>How close, in virtual pixels, untagged blips are to count as one crowd.</summary>
    private const float WgCrowdRadius = 24f;

    /// <summary>Metres inside which a ship keeps its name anywhere on the scope: close enough to be in a fight with you.</summary>
    private const float WgTagCloseRange = 750f;

    /// <summary>Seconds a tag takes to fade in or out.</summary>
    private const float WgTagFade = 0.15f;

    private readonly WGRadarTagLayout _wgTagLayout = new();
    private readonly List<WGRadarTagLayout.Tag> _wgTags = new();
    private readonly List<WgTagDraw> _wgTagDraws = new();
    private readonly List<WgTagText> _wgTagLines = new();
    private readonly List<WgTagText> _wgTagAbove = new();
    private readonly Dictionary<string, int> _wgTagKeyCounts = new();

    private readonly List<WGRadarTagLayout.Tag> _wgShownTags = new();
    private readonly List<int> _wgShownIndex = new();
    private readonly List<int> _wgQuietIndex = new();
    private readonly Dictionary<int, float> _wgTagAlpha = new();
    private readonly HashSet<int> _wgTagSeen = new();
    private readonly List<int> _wgTagGone = new();
    private TimeSpan _wgLastTagFrame;

    private readonly record struct WgTagText(string Text, float Scale, Color Color, Vector2 Size);

    /// <summary>How a tag draws: its lines, anything drawn after its first line, and anything drawn only when it is alone.</summary>
    private struct WgTagDraw
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
    /// while it has room. Both are in pixels. Add its lines with <see cref="WgTagLine"/>, the first being its name.
    /// An <paramref name="always"/> tag shows its name anywhere on the scope; the rest show only under the cursor.
    /// <paramref name="key"/> names the contact from frame to frame, an entity id say; without one, the tag's name does.
    /// </summary>
    private void WgBeginTag(Vector2 anchor, Vector2 position, int priority = 0, bool always = false, int? key = null)
    {
        _wgTags.Add(new WGRadarTagLayout.Tag { Anchor = anchor, Position = position, Priority = priority, Key = HashCode.Combine(1, key ?? 0) });
        _wgTagDraws.Add(new WgTagDraw { FirstLine = _wgTagLines.Count, FirstAbove = _wgTagAbove.Count, Always = always, Keyed = key != null, Full = true, Alpha = 1f });
    }

    /// <summary>Adds a line over the current tag's first line, each one higher than the last; shown only with its details.</summary>
    private void WgTagAbove(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wgTagAbove.Add(new WgTagText(text, scale, color, size));
        var index = _wgTags.Count - 1;
        var tag = _wgTags[index];
        tag.Rise += size.Y;
        tag.Size.X = MathF.Max(tag.Size.X, size.X);
        _wgTags[index] = tag;
        var draw = _wgTagDraws[index];
        draw.AboveCount++;
        _wgTagDraws[index] = draw;
    }

    /// <summary>Adds a line under the current tag's others; the first line is its name, the rest are details.</summary>
    private void WgTagLine(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wgTagLines.Add(new WgTagText(text, scale, color, size));
        var index = _wgTags.Count - 1;
        var tag = _wgTags[index];
        var draw = _wgTagDraws[index];
        if (draw.LineCount == 0)
        {
            draw.HeadTextWidth = size.X;
            tag.HeadSize += size;
        }

        draw.LineCount++;
        tag.Size = new Vector2(MathF.Max(tag.Size.X, size.X), tag.Size.Y + size.Y);
        _wgTags[index] = tag;
        _wgTagDraws[index] = draw;
    }

    /// <summary>Reserves <paramref name="width"/> pixels after the current tag's first line, drawn by <paramref name="draw"/> from that line's top-right.</summary>
    private void WgTagHeadExtra(float width, Action<DrawingHandleScreen, Vector2> draw)
    {
        var index = _wgTags.Count - 1;
        var tag = _wgTags[index];
        tag.HeadSize.X += width;
        tag.Size.X = MathF.Max(tag.Size.X, tag.HeadSize.X);
        _wgTags[index] = tag;
        var info = _wgTagDraws[index];
        info.DrawHeadExtra = draw;
        _wgTagDraws[index] = info;
    }

    /// <summary>Adds drawing the current tag does only while it stands alone, given its position.</summary>
    private void WgTagAlone(Action<DrawingHandleScreen, Vector2> draw)
    {
        var index = _wgTags.Count - 1;
        var info = _wgTagDraws[index];
        info.DrawAlone += draw;
        _wgTagDraws[index] = info;
    }

    /// <summary>Lays out and draws the tags collected this frame, over everything else on the scope.</summary>
    private void WgDrawTags(DrawingHandleScreen handle)
    {
        var now = IoCManager.Resolve<IGameTiming>().RealTime;
        var step = Math.Clamp((float) (now - _wgLastTagFrame).TotalSeconds, 0f, 0.1f) / WgTagFade;
        _wgLastTagFrame = now;

        WgDropTagsOffScope();
        if (_wgTags.Count == 0)
        {
            WgClearTags();
            return;
        }

        var iconsOnly = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarIconsOnly);
        if (!iconsOnly && !_wgSettingsCfg.GetCVar(WolfgateCVars.RadarSmartTags))
        {
            // Smart tags off: every tag whole by its contact, overlapping or not.
            for (var i = 0; i < _wgTags.Count; i++)
                DrawTagAlone(handle, i);

            WgClearTags();
            return;
        }

        WgKeyTags();
        var allNames = _wgAllNames;

        // The lens: every tag inside it shows, and the one nearest the cursor shows its details. With icons only, just
        // that nearest one shows.
        var hover = _isMouseInside && WgHoverReveal;
        Vector2? cursor = hover ? (UserInterfaceManager.MousePositionScaled.Position - GlobalPosition) * UIScale : null;
        var lens = WgLensRadius * UIScale;
        var focus = -1;
        var nearest = lens * lens;
        if (cursor is { } point)
        {
            if (!iconsOnly)
                handle.DrawCircle(point, lens, WgLensColor, false);

            for (var i = 0; i < _wgTags.Count; i++)
            {
                var distance = (_wgTags[i].Anchor - point).LengthSquared();
                if (distance > nearest)
                    continue;

                nearest = distance;
                focus = i;
            }
        }

        _wgShownTags.Clear();
        _wgShownIndex.Clear();
        _wgQuietIndex.Clear();
        _wgTagSeen.Clear();
        for (var i = 0; i < _wgTags.Count; i++)
        {
            var tag = _wgTags[i];
            var draw = _wgTagDraws[i];
            var shown = allNames || i == focus
                || !iconsOnly && (draw.Always || cursor is { } at && (tag.Anchor - at).LengthSquared() <= lens * lens);
            draw.Full = i == focus;
            if (!draw.Full)
            {
                // Name only: the detail lines and anything over the name wait for the cursor.
                tag.Size = tag.HeadSize;
                tag.Rise = 0f;
                _wgTags[i] = tag;
            }

            var alpha = _wgTagAlpha.GetValueOrDefault(tag.Key);
            alpha = shown ? MathF.Min(1f, alpha + step) : MathF.Max(0f, alpha - step);
            _wgTagAlpha[tag.Key] = alpha;
            _wgTagSeen.Add(tag.Key);
            draw.Alpha = alpha;
            _wgTagDraws[i] = draw;

            if (shown)
            {
                _wgShownTags.Add(tag);
                _wgShownIndex.Add(i);
            }
            else
            {
                _wgQuietIndex.Add(i);
            }
        }

        // Forget tags whose contacts are gone.
        _wgTagGone.Clear();
        foreach (var key in _wgTagAlpha.Keys)
        {
            if (!_wgTagSeen.Contains(key))
                _wgTagGone.Add(key);
        }

        foreach (var key in _wgTagGone)
            _wgTagAlpha.Remove(key);

        WgDrawQuietTags(handle, !iconsOnly);
        WgDrawShownTags(handle);
        WgClearTags();
    }

    /// <summary>
    /// Gives each tag without a contact key one it keeps from frame to frame: its name, numbered when names repeat.
    /// </summary>
    private void WgKeyTags()
    {
        _wgTagKeyCounts.Clear();
        for (var i = 0; i < _wgTags.Count; i++)
        {
            var draw = _wgTagDraws[i];
            if (draw.Keyed)
                continue;

            var name = draw.LineCount > 0 ? _wgTagLines[draw.FirstLine].Text : string.Empty;
            var seen = _wgTagKeyCounts.GetValueOrDefault(name);
            _wgTagKeyCounts[name] = seen + 1;
            var tag = _wgTags[i];
            tag.Key = HashCode.Combine(name, seen);
            _wgTags[i] = tag;
        }
    }

    /// <summary>
    /// Quiet tags out of the lens: one still fading out keeps its name a moment; blips crowded together get a count
    /// unless <paramref name="counts"/> is off.
    /// </summary>
    private void WgDrawQuietTags(DrawingHandleScreen handle, bool counts)
    {
        foreach (var index in _wgQuietIndex)
        {
            if (_wgTagDraws[index].Alpha > 0f)
                DrawTagAlone(handle, index);
        }

        if (!counts)
            return;

        var crowd = WgCrowdRadius * UIScale;
        var scale = UIScale * 0.7f;
        for (var i = 0; i < _wgQuietIndex.Count; i++)
        {
            var seed = _wgQuietIndex[i];
            if (seed < 0)
                continue;

            var sum = _wgTags[seed].Anchor;
            var count = 1;
            for (var j = i + 1; j < _wgQuietIndex.Count; j++)
            {
                var other = _wgQuietIndex[j];
                if (other < 0 || (_wgTags[other].Anchor - _wgTags[seed].Anchor).LengthSquared() > crowd * crowd)
                    continue;

                sum += _wgTags[other].Anchor;
                count++;
                _wgQuietIndex[j] = -1;
            }

            if (count < 2)
                continue;

            var middle = sum / count;
            handle.DrawString(Font, middle + new Vector2(6f, -14f) * UIScale, Loc.GetString("wg-radar-tags-crowd", ("count", count)),
                scale, WgStackMoreColor);
        }
    }

    /// <summary>The tags that show: alone by their contacts where there is room, stacked where they would overlap.</summary>
    private void WgDrawShownTags(DrawingHandleScreen handle)
    {
        if (_wgShownTags.Count == 0)
            return;

        var moreScale = UIScale * 0.7f;
        _wgTagLayout.Gap = 3f * UIScale;
        _wgTagLayout.StickyGap = 10f * UIScale;
        _wgTagLayout.SwapMargin = 8f * UIScale;
        _wgTagLayout.MoreHeight = handle.GetDimensions(Font, "+", moreScale).Y;
        _wgTagLayout.Solve(_wgShownTags, PixelSize);

        foreach (var group in _wgTagLayout.Groups)
        {
            if (!group.Stacked)
            {
                DrawTagAlone(handle, _wgShownIndex[group.All[0]]);
                continue;
            }

            var box = group.Box;
            var backing = 0f;
            foreach (var row in group.Rows)
                backing = MathF.Max(backing, _wgTagDraws[_wgShownIndex[row]].Alpha);

            // A plain list, no backing or lines: the rows read top to bottom in the order of their contacts.
            var y = box.Bottom;
            foreach (var row in group.Rows)
            {
                var index = _wgShownIndex[row];
                var tag = _wgTags[index];
                var draw = _wgTagDraws[index];
                var line = _wgTagLines[draw.FirstLine];
                var at = new Vector2(box.Left, y);
                handle.DrawString(Font, at, line.Text, line.Scale, Fade(line.Color, draw.Alpha));
                draw.DrawHeadExtra?.Invoke(handle, at + new Vector2(draw.HeadTextWidth, 0f));
                y += tag.HeadSize.Y;
            }

            if (group.Hidden > 0)
            {
                handle.DrawString(Font, new Vector2(box.Left, y), Loc.GetString("wg-radar-tags-more", ("count", group.Hidden)),
                    moreScale, Fade(WgStackMoreColor, backing));
            }
        }
    }

    /// <summary>Whether hovering a contact reveals its tag.</summary>
    private bool WgHoverReveal => _wgSettingsCfg.GetCVar(WolfgateCVars.RadarHoverReveal);

    private static Color Fade(Color color, float alpha) => color.WithAlpha(color.A * alpha);

    private void WgClearTags()
    {
        _wgTags.Clear();
        _wgTagDraws.Clear();
        _wgTagLines.Clear();
        _wgTagAbove.Clear();
    }

    /// <summary>Whether a point, in pixels, lies on the scope: inside the ellipse it draws within, not out at its rim or corners.</summary>
    private bool WgOnScope(Vector2 point)
    {
        var half = new Vector2(PixelSize.X, PixelSize.Y) / 2f;
        if (half.X <= 0f || half.Y <= 0f)
            return false;

        var offset = (point - half) / half;
        return offset.LengthSquared() <= 1f;
    }

    /// <summary>Drops the tags of contacts off the scope, so a stack lists only what the scope shows.</summary>
    private void WgDropTagsOffScope()
    {
        var kept = 0;
        for (var i = 0; i < _wgTags.Count; i++)
        {
            if (!WgOnScope(_wgTags[i].Anchor))
                continue;

            _wgTags[kept] = _wgTags[i];
            _wgTagDraws[kept] = _wgTagDraws[i];
            kept++;
        }

        _wgTags.RemoveRange(kept, _wgTags.Count - kept);
        _wgTagDraws.RemoveRange(kept, _wgTagDraws.Count - kept);
    }

    /// <summary>Draws a tag by its contact: whole, or its name alone, faded as it comes and goes.</summary>
    private void DrawTagAlone(DrawingHandleScreen handle, int index)
    {
        var tag = _wgTags[index];
        var draw = _wgTagDraws[index];
        var at = tag.Position;
        var lines = draw.Full ? draw.LineCount : Math.Min(1, draw.LineCount);
        for (var i = 0; i < lines; i++)
        {
            var line = _wgTagLines[draw.FirstLine + i];
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
                var line = _wgTagAbove[draw.FirstAbove + i];
                above.Y -= line.Size.Y;
                handle.DrawString(Font, above, line.Text, line.Scale, Fade(line.Color, draw.Alpha));
            }
        }

        draw.DrawAlone?.Invoke(handle, tag.Position);
    }
}
