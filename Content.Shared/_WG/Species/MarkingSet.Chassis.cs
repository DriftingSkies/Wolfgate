namespace Content.Shared.Humanoid.Markings;

public sealed partial class MarkingSet
{
    /// <summary>
    /// For a species with a chassis budget, keeps one chassis part on each body part: drops any extra on a part, then
    /// gives every part the budget's defaults name and nothing covers its default.
    /// </summary>
    public void EnsureChassis(Color? skinColor = null, Color? eyeColor = null, MarkingManager? markingManager = null)
    {
        if (!Points.TryGetValue(MarkingCategories.Chassis, out var points))
            return;

        IoCManager.Resolve(ref markingManager);

        var covered = new HashSet<HumanoidVisualLayers>();
        if (Markings.TryGetValue(MarkingCategories.Chassis, out var worn))
        {
            for (var i = 0; i < worn.Count; i++)
            {
                if (markingManager.TryGetMarking(worn[i], out var prototype) && covered.Add(prototype.BodyPart))
                    continue;

                Remove(MarkingCategories.Chassis, i);
                i--;
            }
        }

        foreach (var id in points.DefaultMarkings)
        {
            if (!markingManager.Markings.TryGetValue(id, out var prototype) || !covered.Add(prototype.BodyPart))
                continue;

            var colors = MarkingColoring.GetMarkingLayerColors(prototype, skinColor, eyeColor, this);
            AddBack(MarkingCategories.Chassis, new Marking(id, colors) { Forced = points.Points <= 0 });
        }
    }
}
