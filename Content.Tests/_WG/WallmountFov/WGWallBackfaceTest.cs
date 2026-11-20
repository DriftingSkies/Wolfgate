using System.Numerics;
using Content.Client._WG.WallmountFov;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.WallmountFov;

[TestFixture]
[TestOf(typeof(WGWallBackfaceSystem))]
public sealed class WGWallBackfaceTest
{
    /// <summary>The offset the wall light bases draw their sprite at: one tile up, onto the wall.</summary>
    private static readonly Vector2 WallOffset = new(0f, 1f);

    /// <summary>
    /// An unturned mount's wall is to its north: eyes in the room see it, eyes past the wall face don't.
    /// </summary>
    [Test]
    public void NorthWall()
    {
        var mount = new Vector2(10.5f, 4.5f);

        Assert.Multiple(() =>
        {
            Assert.That(Behind(mount, Angle.Zero, mount + new Vector2(0f, -3f)), Is.False, "Across the room.");
            Assert.That(Behind(mount, Angle.Zero, mount + new Vector2(0.3f, 0.4f)), Is.False, "Under the lamp.");
            Assert.That(Behind(mount, Angle.Zero, mount + new Vector2(0f, WGWallBackfaceSystem.FaceDistance)), Is.False, "On the face.");
            Assert.That(Behind(mount, Angle.Zero, mount + new Vector2(0f, 2f)), Is.True, "Right behind the wall.");
            Assert.That(Behind(mount, Angle.Zero, mount + new Vector2(-6f, 1.6f)), Is.True, "Behind the wall, off to the side.");
        });
    }

    /// <summary>
    /// The wall turns with the mount, so the same offset faces east, south or west on a turned mount or grid.
    /// </summary>
    [Test]
    public void TurnedMounts()
    {
        var mount = Vector2.Zero;

        Assert.Multiple(() =>
        {
            // A quarter turn anticlockwise points the offset west.
            Assert.That(Behind(mount, Angle.FromDegrees(90), new Vector2(-2f, 0f)), Is.True);
            Assert.That(Behind(mount, Angle.FromDegrees(90), new Vector2(2f, 0f)), Is.False);
            Assert.That(Behind(mount, Angle.FromDegrees(90), new Vector2(0f, 2f)), Is.False);

            Assert.That(Behind(mount, Angle.FromDegrees(180), new Vector2(0f, -2f)), Is.True);
            Assert.That(Behind(mount, Angle.FromDegrees(180), new Vector2(0f, 2f)), Is.False);

            Assert.That(Behind(mount, Angle.FromDegrees(-90), new Vector2(2f, 0f)), Is.True);
            Assert.That(Behind(mount, Angle.FromDegrees(-90), new Vector2(-2f, 0f)), Is.False);

            // A grid turned off the cardinals turns the wall with it.
            var slanted = Angle.FromDegrees(30);
            Assert.That(Behind(mount, slanted, slanted.RotateVec(new Vector2(0f, 2f))), Is.True);
            Assert.That(Behind(mount, slanted, new Vector2(0f, -2f)), Is.False);
        });
    }

    /// <summary>
    /// A sprite drawn on its own tile, such as a free-standing lamp that inherits the marker, is never hidden.
    /// </summary>
    [Test]
    public void OwnTileSprites()
    {
        var eye = new Vector2(0f, 5f);

        Assert.Multiple(() =>
        {
            Assert.That(WGWallBackfaceSystem.IsBehindMount(Vector2.Zero, Angle.Zero, Vector2.Zero, eye), Is.False);
            Assert.That(WGWallBackfaceSystem.IsBehindMount(Vector2.Zero, Angle.Zero, new Vector2(0f, 0.25f), eye), Is.False);
            Assert.That(WGWallBackfaceSystem.IsBehindMount(Vector2.Zero, Angle.Zero, new Vector2(0f, -0.5f), -eye), Is.False);
        });
    }

    private static bool Behind(Vector2 mount, Angle rotation, Vector2 eye)
    {
        return WGWallBackfaceSystem.IsBehindMount(mount, rotation, WallOffset, eye);
    }
}
