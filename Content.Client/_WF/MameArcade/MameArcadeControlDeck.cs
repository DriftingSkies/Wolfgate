using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// The cabinet's control deck under the screen: a joystick, the board's buttons (the Neo Geo's A B C D, or CPS2's
/// three punches and three kicks), Start and Coin for each player, lit as they are pressed. The local player's
/// station shows which key works each control.
/// </summary>
public sealed partial class MameArcadeControlDeck : Control
{
    // Layout of one station at scale 1, in virtual pixels.
    private const float StationWidth = 380f;
    private const float DeckHeight = 116f;
    private const float StickRadius = 30f;
    private const float BallRadius = 12f;
    private const float StickTravel = 14f;
    private const float ButtonRadius = 15f;
    private const float SixButtonRadius = 13f;
    private const float SmallButtonRadius = 8f;

    private static readonly Color Deck = Color.FromHex("#17171d");
    private static readonly Color DeckEdge = Color.FromHex("#34343f");
    private static readonly Color Well = Color.FromHex("#0d0d11");
    private static readonly Color Ball = Color.FromHex("#d8302f");
    private static readonly Color Hint = Color.FromHex("#8a8a99");
    private static readonly Color[] SeatColors = { Color.FromHex("#e04848"), Color.FromHex("#4a86e8") };

    // The Neo Geo's four buttons as MAME.NET's driver reads them, in a shallow arc and their deck colours.
    private static readonly ButtonSpot[] NeoGeoButtons =
    {
        new(MameControls.Button1, "mame-arcade-deck-a", "mame-arcade-deck-key-a", Color.FromHex("#e23b3b"), 112, 52, ButtonRadius),
        new(MameControls.Button2, "mame-arcade-deck-b", "mame-arcade-deck-key-b", Color.FromHex("#f2c318"), 152, 58, ButtonRadius),
        new(MameControls.Button4, "mame-arcade-deck-c", "mame-arcade-deck-key-c", Color.FromHex("#3fb950"), 192, 60, ButtonRadius),
        new(MameControls.Button5, "mame-arcade-deck-d", "mame-arcade-deck-key-d", Color.FromHex("#3b7ff2"), 232, 58, ButtonRadius),
    };

    // CPS2's punches over its kicks, light to heavy, as Capcom's six-button decks colour them.
    private static readonly ButtonSpot[] Cps2Buttons =
    {
        new(MameControls.Button1, "mame-arcade-deck-lp", "mame-arcade-deck-key-lp", Color.FromHex("#3b7ff2"), 112, 38, SixButtonRadius),
        new(MameControls.Button2, "mame-arcade-deck-mp", "mame-arcade-deck-key-mp", Color.FromHex("#f2c318"), 148, 34, SixButtonRadius),
        new(MameControls.Button3, "mame-arcade-deck-hp", "mame-arcade-deck-key-hp", Color.FromHex("#e23b3b"), 184, 34, SixButtonRadius),
        new(MameControls.Button4, "mame-arcade-deck-lk", "mame-arcade-deck-key-lk", Color.FromHex("#3b7ff2"), 112, 78, SixButtonRadius),
        new(MameControls.Button5, "mame-arcade-deck-mk", "mame-arcade-deck-key-mk", Color.FromHex("#f2c318"), 148, 74, SixButtonRadius),
        new(MameControls.Button6, "mame-arcade-deck-hk", "mame-arcade-deck-key-hk", Color.FromHex("#e23b3b"), 184, 74, SixButtonRadius),
    };

    [Dependency] private IResourceCache _cache = default!;

    private readonly Seat[] _seats = { new(), new() };
    private ButtonSpot[] _buttons = NeoGeoButtons;
    private readonly Font _label;
    private readonly Font _small;

    public MameArcadeControlDeck()
    {
        IoCManager.InjectDependencies(this);
        _label = _cache.NotoStack(variation: "Bold", size: 11);
        _small = _cache.NotoStack(size: 9);
        MinSize = new Vector2(0, DeckHeight);
        HorizontalExpand = true;
    }

    /// <summary>The board whose buttons the deck shows.</summary>
    public MameBoard Board
    {
        set => _buttons = value == MameBoard.Cps2 ? Cps2Buttons : NeoGeoButtons;
    }

    /// <summary>What one station shows. An idle station is dimmed, for a seat nobody plays.</summary>
    public void SetSeat(int seat, MameControls held, string name, bool local, bool active)
    {
        var s = _seats[seat];
        s.Held = held;
        s.Name = name;
        s.Local = local;
        s.Active = active;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var half = PixelWidth / 2f;
        var scale = MathF.Min(UIScale, MathF.Min(half / StationWidth, PixelHeight / DeckHeight));

        handle.DrawRect(PixelSizeBox, Deck);
        handle.DrawRect(new UIBox2(0, 0, PixelWidth, 2 * UIScale), DeckEdge);
        handle.DrawRect(new UIBox2(half - UIScale, 6 * UIScale, half + UIScale, PixelHeight - 6 * UIScale), DeckEdge);

        for (var i = 0; i < _seats.Length; i++)
        {
            // Each station centred in its half, so a wide window doesn't stretch it.
            var origin = new Vector2(i * half + (half - StationWidth * scale) / 2f, (PixelHeight - DeckHeight * scale) / 2f);
            DrawStation(handle, i, _seats[i], origin, scale);
        }
    }

    private void DrawStation(DrawingHandleScreen handle, int index, Seat seat, Vector2 origin, float scale)
    {
        var dim = seat.Active ? 1f : 0.35f;
        Vector2 At(float x, float y) => origin + new Vector2(x, y) * scale;

        var badge = Loc.GetString("mame-arcade-deck-seat", ("seat", index + 1));
        handle.DrawString(_label, At(8, 4), badge, scale, Fade(SeatColors[index], dim));
        var name = seat.Active
            ? seat.Local ? Loc.GetString("mame-arcade-deck-you") : seat.Name
            : Loc.GetString("mame-arcade-deck-idle");
        handle.DrawString(_small, At(34, 6), name, scale, Fade(Color.White, dim * 0.8f));

        // Joystick: a well, the shaft and the ball, leaning the way it's held.
        var stick = At(48, 60);
        handle.DrawCircle(stick, (StickRadius + 3) * scale, Fade(DeckEdge, dim));
        handle.DrawCircle(stick, StickRadius * scale, Fade(Well, dim));
        var lean = Direction(seat.Held) * StickTravel * scale;
        handle.DrawCircle(stick, 4 * scale, Fade(Color.FromHex("#555560"), dim));
        handle.DrawLine(stick, stick + lean, Fade(Color.FromHex("#888892"), dim));
        handle.DrawCircle(stick + lean + new Vector2(0, 2) * scale, BallRadius * scale, Fade(Color.Black, dim * 0.5f));
        handle.DrawCircle(stick + lean, BallRadius * scale, Fade(Ball, dim));
        handle.DrawCircle(stick + lean - new Vector2(4, 4) * scale, 3.5f * scale, Fade(Color.White, dim * 0.45f));
        if (seat.Local)
            DrawCentred(handle, _small, Loc.GetString("mame-arcade-deck-key-stick"), At(48, 98), scale, Hint);

        foreach (var spot in _buttons)
        {
            var centre = At(spot.X, spot.Y);
            DrawButton(handle, centre, spot.Radius * scale, spot.Color, (seat.Held & spot.Control) != 0, dim);
            DrawCentred(handle, _label, Loc.GetString(spot.Label), centre - new Vector2(0, 7 * scale), scale, Fade(Color.Black, dim * 0.75f));
            if (seat.Local)
                DrawCentred(handle, _small, Loc.GetString(spot.Key), centre + new Vector2(0, (spot.Radius + 3) * scale), scale, Hint);
        }

        DrawSmall(handle, At(286, 44), Color.FromHex("#e6e6ea"), seat, MameControls.Start,
            "mame-arcade-deck-start", "mame-arcade-deck-key-start", scale, dim);
        DrawSmall(handle, At(286, 80), Color.FromHex("#c8a040"), seat, MameControls.Coin,
            "mame-arcade-deck-coin", "mame-arcade-deck-key-coin", scale, dim);
    }

    private void DrawButton(DrawingHandleScreen handle, Vector2 centre, float radius, Color color, bool pressed, float dim)
    {
        handle.DrawCircle(centre + new Vector2(0, radius * 0.15f), radius + 2 * UIScale, Fade(Color.Black, dim * 0.6f));
        handle.DrawCircle(centre, radius + 2 * UIScale, Fade(DeckEdge, dim));
        if (pressed)
        {
            // Pushed in and lit.
            handle.DrawCircle(centre, radius + 5 * UIScale, Fade(color, dim * 0.25f));
            handle.DrawCircle(centre, radius * 0.92f, Fade(Lighten(color), dim));
        }
        else
        {
            handle.DrawCircle(centre, radius, Fade(Darken(color), dim));
            handle.DrawCircle(centre - new Vector2(0, radius * 0.12f), radius * 0.82f, Fade(color, dim));
        }
    }

    private void DrawSmall(
        DrawingHandleScreen handle,
        Vector2 centre,
        Color color,
        Seat seat,
        MameControls control,
        string label,
        string key,
        float scale,
        float dim)
    {
        DrawButton(handle, centre, SmallButtonRadius * scale, color, (seat.Held & control) != 0, dim);
        var text = seat.Local
            ? Loc.GetString("mame-arcade-deck-small", ("label", Loc.GetString(label)), ("key", Loc.GetString(key)))
            : Loc.GetString(label);
        handle.DrawString(_small, centre + new Vector2(13, -7) * scale, text, scale, Fade(seat.Local ? Hint : Color.Gray, dim));
    }

    private static void DrawCentred(DrawingHandleScreen handle, Font font, string text, Vector2 centre, float scale, Color color)
    {
        var size = handle.GetDimensions(font, text, scale);
        handle.DrawString(font, centre - new Vector2(size.X / 2f, 0), text, scale, color);
    }

    private static Vector2 Direction(MameControls held)
    {
        var x = ((held & MameControls.Right) != 0 ? 1 : 0) - ((held & MameControls.Left) != 0 ? 1 : 0);
        var y = ((held & MameControls.Down) != 0 ? 1 : 0) - ((held & MameControls.Up) != 0 ? 1 : 0);
        var direction = new Vector2(x, y);
        return direction == Vector2.Zero ? direction : Vector2.Normalize(direction);
    }

    private static Color Fade(Color color, float alpha) => color.WithAlpha(color.A * alpha);

    private static Color Darken(Color color) => new(color.R * 0.55f, color.G * 0.55f, color.B * 0.55f, color.A);

    private static Color Lighten(Color color) =>
        new(MathF.Min(color.R * 1.25f + 0.1f, 1f), MathF.Min(color.G * 1.25f + 0.1f, 1f), MathF.Min(color.B * 1.25f + 0.1f, 1f), color.A);

    private readonly record struct ButtonSpot(MameControls Control, string Label, string Key, Color Color, float X, float Y, float Radius);

    private sealed class Seat
    {
        public MameControls Held;
        public string Name = string.Empty;
        public bool Local;
        public bool Active;
    }
}
