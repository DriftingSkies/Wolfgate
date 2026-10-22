#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.MappingTools;

[TestFixture]
public sealed class MappingToolsPipesTest
{
    private static Vector2i V(int x, int y) => new(x, y);

    private static List<Vector2i> Path(params Vector2i[] cells)
    {
        var path = new List<Vector2i>();
        foreach (var cell in cells)
        {
            Assert.That(MappingToolsPipes.Extend(path, cell, 4096), Is.True);
        }

        return path;
    }

    private static MappingPipePlan Plan(List<Vector2i> path, Dictionary<Vector2i, MappingExistingPipe> existing,
        bool autoJoin = true, bool replace = true)
    {
        return MappingToolsPipes.Plan(path, c => existing.TryGetValue(c, out var pipe) ? pipe : null, autoJoin, replace);
    }

    private static PipeDirection PlacedAt(MappingPipePlan plan, Vector2i cell)
    {
        return plan.Placed.Single(p => p.Cell == cell).Directions;
    }

    [Test]
    public void PathFollowsTheCursor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path(V(0, 0), V(1, 0), V(1, 0), V(2, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0) }),
                "A repeated cell adds nothing.");
            Assert.That(Path(V(0, 0), V(3, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0) }),
                "A jump along an axis is filled in.");
        });
    }

    [Test]
    public void DiagonalJumpsAreBridgedWithAnL()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path(V(0, 0), V(1, 1)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(1, 1) }),
                "From one cell the L goes along X first.");
            Assert.That(Path(V(0, 0), V(0, 1), V(1, 2)), Is.EqualTo(new[] { V(0, 0), V(0, 1), V(0, 2), V(1, 2) }),
                "After a vertical step the L keeps going up first.");
            Assert.That(Path(V(0, 0), V(1, 0), V(3, -2)),
                Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(3, -1), V(3, -2) }));
        });

        var path = Path(V(0, 0), V(4, 3), V(-2, 5));
        Assert.That(path, Is.Unique);
        for (var i = 1; i < path.Count; i++)
        {
            Assert.That(MappingToolsPipes.Side(path[i - 1], path[i]), Is.Not.EqualTo(PipeDirection.None),
                $"Step {i} goes to a side neighbour.");
        }
    }

    [Test]
    public void ComingBackCutsThePath()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path(V(0, 0), V(1, 0), V(2, 0), V(1, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0) }));
            Assert.That(Path(V(0, 0), V(2, 0), V(2, 2), V(0, 2), V(0, 0)), Is.EqualTo(new[] { V(0, 0) }),
                "Closing a loop cuts it back to the start.");
        });
    }

    [Test]
    public void PathStopsAtTheCap()
    {
        var path = new List<Vector2i>();
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.Extend(path, V(0, 0), 3), Is.True);
            Assert.That(MappingToolsPipes.Extend(path, V(5, 0), 3), Is.False);
            Assert.That(path, Has.Count.EqualTo(3));
            Assert.That(MappingToolsPipes.Normalise(new[] { V(0, 0), V(9, 0) }, 4), Is.Null);
            Assert.That(MappingToolsPipes.Normalise(new[] { V(0, 0), V(int.MaxValue / 2, 0) }, 4096), Is.Null,
                "A huge jump is refused before it is walked.");
            Assert.That(MappingToolsPipes.Normalise(new[] { V(0, 0), V(1, 1) }, 4096),
                Is.EqualTo(new[] { V(0, 0), V(1, 0), V(1, 1) }));
        });
    }

    [Test]
    public void EverySetOfSidesHasAShape()
    {
        var expected = new Dictionary<PipeDirection, (PipeShape Shape, int Degrees)>
        {
            [PipeDirection.South] = (PipeShape.Half, 0),
            [PipeDirection.East] = (PipeShape.Half, 90),
            [PipeDirection.North] = (PipeShape.Half, 180),
            [PipeDirection.West] = (PipeShape.Half, 270),
            [PipeDirection.Longitudinal] = (PipeShape.Straight, 0),
            [PipeDirection.Lateral] = (PipeShape.Straight, 90),
            [PipeDirection.SWBend] = (PipeShape.Bend, 0),
            [PipeDirection.SEBend] = (PipeShape.Bend, 90),
            [PipeDirection.NEBend] = (PipeShape.Bend, 180),
            [PipeDirection.NWBend] = (PipeShape.Bend, 270),
            [PipeDirection.TSouth] = (PipeShape.TJunction, 0),
            [PipeDirection.TEast] = (PipeShape.TJunction, 90),
            [PipeDirection.TNorth] = (PipeShape.TJunction, 180),
            [PipeDirection.TWest] = (PipeShape.TJunction, 270),
            [PipeDirection.Fourway] = (PipeShape.Fourway, 0),
        };

        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.TryShape(PipeDirection.None, out _, out _), Is.False);
            for (var bits = 1; bits < 16; bits++)
            {
                var sides = (PipeDirection) bits;
                Assert.That(MappingToolsPipes.TryShape(sides, out var shape, out var rotation), Is.True, $"{sides}");
                Assert.That((shape, (int) Math.Round(rotation.Degrees)), Is.EqualTo(expected[sides]), $"{sides}");
                Assert.That(shape.ToBaseDirection().RotatePipeDirection(rotation.Theta), Is.EqualTo(sides),
                    $"{sides}: the turned pipe opens to exactly those sides.");
            }
        });
    }

    [Test]
    public void ShapesHavePrototypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Half), Is.EqualTo("GasPipeHalf"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Straight), Is.EqualTo("GasPipeStraight"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Bend), Is.EqualTo("GasPipeBend"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.TJunction), Is.EqualTo("GasPipeTJunction"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Fourway), Is.EqualTo("GasPipeFourway"));
        });
    }

    [Test]
    public void StraightRunEndsInHalves()
    {
        var plan = Plan(Path(V(0, 0), V(3, 0)), new());
        Assert.Multiple(() =>
        {
            Assert.That(plan.Placed, Has.Count.EqualTo(4));
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East));
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(PlacedAt(plan, V(3, 0)), Is.EqualTo(PipeDirection.West));
            Assert.That(plan.Upgraded, Is.Empty);
            Assert.That(plan.Skipped, Is.Zero);
            Assert.That(plan.Blocked, Is.Zero);
        });
    }

    [Test]
    public void TurnsAreBends()
    {
        var plan = Plan(Path(V(0, 0), V(1, 0), V(1, 1)), new());
        Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.NWBend));
    }

    [Test]
    public void LoneCellIsAHalf()
    {
        // A click with nothing around used to plan nothing at all, which the server reported as "already like that".
        var plan = Plan(Path(V(0, 0)), new());
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.North), "With no facing a lone half opens north.");
            Assert.That(plan.Skipped + plan.Blocked, Is.Zero);
        });

        plan = MappingToolsPipes.Plan(Path(V(0, 0)), _ => null, true, true, PipeDirection.East);
        Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East), "It opens to the facing.");

        var vent = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 1)] = new(PipeDirection.South, false) };
        plan = Plan(Path(V(0, 0)), vent);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.North), "A click under a device facing it joins it.");
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(0, 1) }));
        });

        var beside = new Dictionary<Vector2i, MappingExistingPipe>
        {
            [V(-1, 0)] = new(PipeDirection.Longitudinal, true),
            [V(0, -1)] = new(PipeDirection.Lateral, false),
        };
        plan = MappingToolsPipes.Plan(Path(V(0, 0)), c => beside.TryGetValue(c, out var p) ? p : null, true, true,
            PipeDirection.South);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.West),
                "A plain pipe beside it is joined; the device below can't gain a side.");
            Assert.That(plan.Upgraded.Single(), Is.EqualTo(new MappingPipeCell(V(-1, 0), PipeDirection.TEast)));
        });

        plan = MappingToolsPipes.Plan(Path(V(0, 0)), c => beside.TryGetValue(c, out var p) ? p : null, false, true,
            PipeDirection.South);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.South), "Without auto-join it keeps the facing.");
            Assert.That(plan.Upgraded, Is.Empty);
        });
    }

    [Test]
    public void ClickOnAPipe()
    {
        var straight = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 0)] = new(PipeDirection.Lateral, true) };
        var plan = Plan(Path(V(0, 0)), straight);
        Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.Lateral),
            "Replace on: the pipe is planned as it is, which the server reports as nothing to change.");

        plan = Plan(Path(V(0, 0)), straight, replace: false);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Placed, Is.Empty);
            Assert.That(plan.Skipped, Is.EqualTo(1), "Replace off: the cell is counted as skipped.");
        });

        var device = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 0)] = new(PipeDirection.South, false) };
        plan = Plan(Path(V(0, 0)), device);
        Assert.That(plan.Blocked, Is.EqualTo(1));
    }

    [Test]
    public void ReportsNameTheReason()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.ReportKey(3, 0, 0), Is.Null, "A clean draw says nothing.");
            Assert.That(MappingToolsPipes.ReportKey(0, 0, 0), Is.EqualTo("wf-mapping-tools-pipes-nothing"));
            Assert.That(MappingToolsPipes.ReportKey(0, 2, 0), Is.EqualTo("wf-mapping-tools-pipes-all-skipped"));
            Assert.That(MappingToolsPipes.ReportKey(0, 0, 1), Is.EqualTo("wf-mapping-tools-pipes-all-blocked"));
            Assert.That(MappingToolsPipes.ReportKey(0, 1, 1), Is.EqualTo("wf-mapping-tools-pipes-all-skipped-blocked"));
            Assert.That(MappingToolsPipes.ReportKey(5, 2, 0), Is.EqualTo("wf-mapping-tools-pipes-laid-skipped"));
            Assert.That(MappingToolsPipes.ReportKey(5, 0, 2), Is.EqualTo("wf-mapping-tools-pipes-laid-blocked"));
            Assert.That(MappingToolsPipes.ReportKey(5, 1, 2), Is.EqualTo("wf-mapping-tools-pipes-laid-skipped-blocked"));
        });
    }

    [Test]
    public void FacingTurns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.TurnsToFace(PipeDirection.South), Is.Zero);
            Assert.That(MappingToolsPipes.TurnsToFace(PipeDirection.East), Is.EqualTo(1));
            Assert.That(MappingToolsPipes.TurnsToFace(PipeDirection.North), Is.EqualTo(2));
            Assert.That(MappingToolsPipes.TurnsToFace(PipeDirection.West), Is.EqualTo(3));
            Assert.That(MappingToolsPipes.TurnsToFace(PipeDirection.None), Is.EqualTo(2), "No facing is north.");
            foreach (var side in MappingToolsPipes.Sides)
            {
                Assert.That(MappingToolsPipes.Turn(PipeDirection.South, MappingToolsPipes.TurnsToFace(side)),
                    Is.EqualTo(side));
            }
        });
    }

    [Test]
    public void DevicesTurnToMeetPipes()
    {
        const PipeDirection none = PipeDirection.None;
        Assert.Multiple(() =>
        {
            // One port, south at rest.
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.South, PipeDirection.West, none, 0), Is.EqualTo(3),
                "A vent faces the one pipe pointing at it.");
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.South, none, PipeDirection.North, 0), Is.EqualTo(2),
                "With nothing pointing at it, it faces a plain pipe that can gain the side.");
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.South, PipeDirection.East, PipeDirection.North, 0),
                Is.EqualTo(1), "A pipe pointing at it beats one that would have to be rebuilt.");
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.South, none, none, 3), Is.EqualTo(3),
                "Nothing around: the requested turn.");

            // Two ports in a line, north and south at rest.
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.Longitudinal, PipeDirection.Lateral, none, 0),
                Is.EqualTo(1), "A pump between two east-west pipes turns across.");
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.Longitudinal, PipeDirection.Lateral, none, 3),
                Is.EqualTo(3), "Either way along works; the nearer to the request wins, keeping the flow direction.");
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.Longitudinal, PipeDirection.Longitudinal, none, 1),
                Is.EqualTo(0).Or.EqualTo(2), "Already in line with a north-south run.");

            // Three ports: a filter, north, west and south at rest.
            Assert.That(MappingToolsPipes.DeviceTurns(PipeDirection.TWest, PipeDirection.TSouth, none, 0),
                Is.EqualTo(1), "A filter meets three pipes on east, south and west.");
        });
    }

    [Test]
    public void PipesPointingAtThePathJoinIt()
    {
        var existing = new Dictionary<Vector2i, MappingExistingPipe>
        {
            [V(1, 1)] = new(PipeDirection.South, true),
            [V(2, -1)] = new(PipeDirection.Lateral, true),
        };

        var plan = Plan(Path(V(0, 0), V(3, 0)), existing);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.TNorth), "The half above points at it.");
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.Lateral), "A pipe running alongside stays apart.");
            Assert.That(plan.Upgraded, Is.Empty, "Only the ends upgrade pipes.");
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(1, 1) }));
        });

        plan = Plan(Path(V(0, 0), V(3, 0)), existing, autoJoin: false);
        Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.Lateral), "Without auto-join nothing joins.");
    }

    [Test]
    public void EndsUpgradePlainPipesPastThem()
    {
        var existing = new Dictionary<Vector2i, MappingExistingPipe>
        {
            [V(-1, 0)] = new(PipeDirection.Longitudinal, true),
            [V(4, 0)] = new(PipeDirection.TEast, true),
            [V(1, -1)] = new(PipeDirection.TWest, true),
        };

        var plan = Plan(Path(V(0, 0), V(3, 0)), existing);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(PlacedAt(plan, V(3, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(plan.Upgraded, Is.EquivalentTo(new[]
            {
                new MappingPipeCell(V(-1, 0), PipeDirection.TEast),
                new MappingPipeCell(V(4, 0), PipeDirection.Fourway),
            }), "Straight to T, T to four-way.");
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.TSouth), "The T below opens up into the run.");
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(-1, 0), V(1, -1), V(4, 0) }));
        });

        var bend = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 3)] = new(PipeDirection.NEBend, true) };
        plan = Plan(Path(V(0, 0), V(0, 2)), bend);
        Assert.That(plan.Upgraded.Single(), Is.EqualTo(new MappingPipeCell(V(0, 3), PipeDirection.TEast)),
            "A bend becomes a T.");

        var open = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 3)] = new(PipeDirection.SEBend, true) };
        plan = Plan(Path(V(0, 0), V(0, 2)), open);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Upgraded, Is.Empty, "A pipe already open towards the end joins as it is.");
            Assert.That(PlacedAt(plan, V(0, 2)), Is.EqualTo(PipeDirection.Longitudinal));
        });

        var device = new Dictionary<Vector2i, MappingExistingPipe> { [V(-1, 0)] = new(PipeDirection.Longitudinal, false) };
        plan = Plan(Path(V(0, 0), V(3, 0)), device);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Upgraded, Is.Empty, "A device isn't rebuilt.");
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East));
        });

        plan = Plan(Path(V(0, 0), V(3, 0)), existing, autoJoin: false);
        Assert.That(plan.Upgraded, Is.Empty, "Without auto-join nothing upgrades.");
    }

    [Test]
    public void PipesOnThePathMergeOrStay()
    {
        var existing = new Dictionary<Vector2i, MappingExistingPipe> { [V(1, 0)] = new(PipeDirection.Longitudinal, true) };

        var plan = Plan(Path(V(0, 0), V(2, 0)), existing);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.Fourway), "Replacing adds the old sides.");
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East));
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(1, 0) }));
        });

        // Before the fix the cells either side of a kept pipe had no sides and were dropped, so this drag laid nothing.
        plan = Plan(Path(V(0, 0), V(2, 0)), existing, replace: false);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East),
                "A cell cut off by a kept pipe still gets a half, towards it.");
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.West));
            Assert.That(plan.Skipped, Is.EqualTo(1));
            Assert.That(plan.Blocked, Is.Zero);
        });

        var facing = new Dictionary<Vector2i, MappingExistingPipe> { [V(1, 0)] = new(PipeDirection.Lateral, true) };
        plan = Plan(Path(V(0, 0), V(2, 0)), facing, replace: false);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East), "A kept pipe that faces the path joins it.");
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.West));
            Assert.That(plan.Skipped, Is.EqualTo(1));
        });

        var device = new Dictionary<Vector2i, MappingExistingPipe> { [V(1, 0)] = new(PipeDirection.Lateral, false) };
        plan = Plan(Path(V(0, 0), V(2, 0)), device);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Placed.Select(p => p.Cell), Is.EqualTo(new[] { V(0, 0), V(2, 0) }),
                "A device on the path is never rebuilt, even when replacing.");
            Assert.That(plan.Blocked, Is.EqualTo(1));
        });
    }
}
