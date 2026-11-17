using System.Numerics;
using Content.Shared._WG.Pool;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Pool;

/// <summary>A side's balls in a row: the seven of its group and then the 8, each an empty socket once pocketed.</summary>
public sealed class PoolBallRow : Control
{
    private const float BallRadius = 7f;
    private const float Spacing = 17f;
    private const int Slots = 8;

    private static readonly Color Socket = Color.Black.WithAlpha(0.35f);

    public PoolGroup Group;
    public ushort Down;

    /// <summary>Fills the row from the right, for the side on the right of the window.</summary>
    public bool Mirrored;

    public PoolBallRow()
    {
        MinSize = new Vector2(Slots * Spacing + 4f, BallRadius * 2f + 4f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (Group == PoolGroup.None)
            return;

        var first = Group == PoolGroup.Solids ? 1 : 9;
        for (var slot = 0; slot < Slots; slot++)
        {
            // The 8 sits a little apart at the end.
            var ball = slot < 7 ? first + slot : PoolRules.EightBall;
            var offset = (slot + 0.5f) * Spacing + (slot < 7 ? 0f : 4f);
            var x = Mirrored ? PixelWidth - offset * UIScale : offset * UIScale;
            var centre = new Vector2(x, PixelHeight * 0.5f);

            if (PoolRules.IsDown(Down, ball))
                handle.DrawCircle(centre, BallRadius * 0.6f * UIScale, Socket);
            else
                PoolBallArt.Draw(handle, ball, centre, BallRadius * UIScale, PoolBallArt.FaceUp, PoolBallArt.FaceSide);
        }
    }
}
