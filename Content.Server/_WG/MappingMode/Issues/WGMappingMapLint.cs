using System.IO;
using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.MappingMode.Issues;

/// <summary>
/// The prototype rules of the CI map checker (<c>.github/mapchecker/config.py</c>, keep them in step): what a map file
/// may not hold, some of it allowed on a point of interest, which is what a grid export is taken for.
/// </summary>
public static class WGMappingMapLint
{
    private const string ProtoLine = "- proto: ";

    private static readonly string[] Illegal =
    {
        "DO NOT MAP", "DoNotMap", "DEBUG", "Admeme", "EncryptionKeyCommand", "SurveillanceCameraWireless",
        "APCHighCapacity", "APCSuperCapacity", "APCHyperCapacity", "PDA", "SpawnPointPassenger",
        "SalvageShuttleMarker", "FTLPoint",
    };

    private static readonly string[] LegalOverrides = { "ButtonFrameCautionSecurity", "PosterLegitPDAAd", "ShowcaseRobot" };

    private static readonly string[] PointOfInterestOnly =
    {
        "WallPlastitaniumIndestructible", "WallPlastitaniumDiagonalIndestructible",
        "PlastititaniumWindowIndestructible", "PlastititaniumWindowDiagonalIndestructible",
        "ClosetMaintenanceFilledRandom", "ClosetWallMaintenanceFilledRandom",
    };

    /// <summary>Whether CI refuses a prototype in a map file, a point of interest's or any other.</summary>
    public static bool IsIllegal(EntityPrototype proto, bool pointOfInterest)
    {
        if (LegalOverrides.Any(match => Named(proto, match)))
            return false;

        return Illegal.Any(match => Matches(proto, match)) ||
               !pointOfInterest && PointOfInterestOnly.Any(match => Matches(proto, match));
    }

    /// <summary>The prototype ids a saved map file lists.</summary>
    public static HashSet<string> ProtoIds(byte[] file)
    {
        var ids = new HashSet<string>();
        using var reader = new StreamReader(new MemoryStream(file));
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith(ProtoLine, StringComparison.Ordinal))
                continue;

            var id = line[ProtoLine.Length..].Trim().Trim('"', '\'');
            if (id.Length > 0)
                ids.Add(id);
        }

        return ids;
    }

    private static bool Named(EntityPrototype proto, string match)
    {
        return proto.ID.Contains(match, StringComparison.OrdinalIgnoreCase) ||
               proto.Name.Contains(match, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(EntityPrototype proto, string match)
    {
        return Named(proto, match) ||
               proto.Categories.Any(category => category.ID.Equals(match, StringComparison.OrdinalIgnoreCase)) ||
               proto.EditorSuffix?.Split(", ").Any(suffix => suffix.Equals(match, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
