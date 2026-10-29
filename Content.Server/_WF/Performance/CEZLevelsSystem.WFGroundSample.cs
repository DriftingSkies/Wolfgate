using System.Numerics;
using Robust.Shared.Timing;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>Reuses a grid's ground sample until it moves, turns or the sample gets old.</summary>
public sealed partial class CEZLevelsSystem
{
    /// <summary>Distance a grid may drift before its footprint is sampled again.</summary>
    private const float WfSampleMoveTolerance = 0.05f;

    /// <summary>Turn in radians a grid may make before its footprint is sampled again.</summary>
    private const double WfSampleTurnTolerance = 0.002;

    /// <summary>Longest a sample is kept, so hull and terrain tile changes still show up.</summary>
    private static readonly TimeSpan WfSampleMaxAge = TimeSpan.FromSeconds(2);

    private readonly Dictionary<EntityUid, WfGroundSampleEntry> _wfGroundSamples = new();
    private readonly List<EntityUid> _wfStaleSamples = new();
    private GameTick _wfNextSamplePrune;

    private readonly record struct WfGroundSampleEntry(
        EntityUid Parent,
        Vector2 Position,
        Angle Rotation,
        TimeSpan Taken,
        float Grip,
        bool Contact);

    /// <summary>The grid's last sample if it has stayed put since, else a fresh one.</summary>
    private (float Grip, bool Contact) WfGroundSample(EntityUid grid)
    {
        var xform = Transform(grid);
        var now = _timing.CurTime;
        if (_wfGroundSamples.TryGetValue(grid, out var entry) &&
            entry.Parent == xform.ParentUid &&
            now - entry.Taken < WfSampleMaxAge &&
            (xform.LocalPosition - entry.Position).LengthSquared() < WfSampleMoveTolerance * WfSampleMoveTolerance &&
            Math.Abs((xform.LocalRotation - entry.Rotation).Reduced().Theta) < WfSampleTurnTolerance)
        {
            return (entry.Grip, entry.Contact);
        }

        var sample = ComputeGroundSample(grid);
        _wfGroundSamples[grid] = new WfGroundSampleEntry(xform.ParentUid, xform.LocalPosition, xform.LocalRotation, now,
            sample.Grip, sample.Contact);
        return sample;
    }

    /// <summary>Drops samples no grid has refreshed in a while, such as those of deleted grids.</summary>
    private void WfPruneGroundSamples()
    {
        if (_timing.CurTick < _wfNextSamplePrune)
            return;

        _wfNextSamplePrune = new GameTick(_timing.CurTick.Value + 300);
        var now = _timing.CurTime;
        _wfStaleSamples.Clear();
        foreach (var (grid, entry) in _wfGroundSamples)
        {
            if (now - entry.Taken > WfSampleMaxAge * 2)
                _wfStaleSamples.Add(grid);
        }

        foreach (var grid in _wfStaleSamples)
        {
            _wfGroundSamples.Remove(grid);
        }
    }
}
