using System.Numerics;
using Content.Client.Pinpointer.UI;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Outposts;

/// <summary>
/// Top-down map of an outpost for its console: the floor and walls from its nav map, framed on its claim, with the
/// claim's edge drawn round them and the console and trade hub marked.
/// </summary>
public sealed class WFOutpostMapControl : NavMapControl
{
    public static readonly Color ClaimColor = Color.FromHex("#E8A33D");
    public static readonly Color ConsoleColor = Color.FromHex("#7CE85A");
    public static readonly Color HubColor = Color.FromHex("#4FC3F7");

    /// <summary>Spare room left round the claim when framing it.</summary>
    private const float FitMargin = 1.06f;

    private EntityUid? _grid;
    private int _claimMargin;
    private bool _needsFit = true;

    public WFOutpostMapControl()
    {
        WallColor = Color.FromHex("#C9C8D6");
        TileColor = Color.FromHex("#34344A");
        BackgroundColor = Color.FromSrgb(TileColor.WithAlpha(0.6f));
        PostWallDrawingAction += DrawClaim;
    }

    public void SetGrid(EntityUid? grid, int claimMargin)
    {
        _claimMargin = claimMargin;
        if (_grid == grid)
            return;

        _grid = grid;
        MapUid = grid;
        _needsFit = true;
        ForceNavMapUpdate();
    }

    /// <summary>Marks the console and the trade hub; either may be missing.</summary>
    public void SetMarkers(EntityUid? console, EntityUid? hub)
    {
        TrackedCoordinates.Clear();
        if (console != null)
            TrackedCoordinates[new EntityCoordinates(console.Value, Vector2.Zero)] = (true, ConsoleColor);
        if (hub != null)
            TrackedCoordinates[new EntityCoordinates(hub.Value, Vector2.Zero)] = (true, HubColor);
    }

    /// <summary>The claim in the grid's own frame: its floor's bounds grown by the margin.</summary>
    private bool TryGetClaim(out Box2 claim)
    {
        claim = default;
        if (!EntManager.TryGetComponent<MapGridComponent>(_grid, out var grid))
            return false;

        var floor = grid.LocalAABB;
        if (floor.Width <= 0f || floor.Height <= 0f)
            return false;

        claim = floor.Enlarged(_claimMargin * grid.TileSize);
        return true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        TryFit();
        base.Draw(handle);
    }

    /// <summary>Frames the whole claim once the grid has arrived; the player's zoom and panning are theirs after that.</summary>
    private void TryFit()
    {
        if (!_needsFit || !TryGetClaim(out var claim))
            return;

        _needsFit = false;
        var center = claim.Center;
        if (EntManager.TryGetComponent<PhysicsComponent>(_grid, out var body))
            center -= body.LocalCenter;

        Offset = center;
        TargetOffset = center;
        Recentering = false;
        WorldRange = Math.Clamp(MathF.Max(claim.Width, claim.Height) / 2f * FitMargin, WorldMinRange, WorldMaxRange);
        ActualRadarRange = WorldRange;
    }

    private void DrawClaim(DrawingHandleScreen handle)
    {
        if (!TryGetClaim(out var claim))
            return;

        var offset = GetOffset();
        var topLeft = ScalePosition(new Vector2(claim.Left - offset.X, -(claim.Top - offset.Y)));
        var bottomRight = ScalePosition(new Vector2(claim.Right - offset.X, -(claim.Bottom - offset.Y)));
        var box = new UIBox2(topLeft, bottomRight);
        handle.DrawRect(box, ClaimColor.WithAlpha(0.06f));
        handle.DrawRect(box, ClaimColor, filled: false);
    }
}
