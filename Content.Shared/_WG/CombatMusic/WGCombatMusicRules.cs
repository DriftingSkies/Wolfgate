using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.CombatMusic;

/// <summary>Validation and storage of a character's combat playlist.</summary>
public static class WGCombatMusicRules
{
    /// <summary>Most tracks one playlist may hold.</summary>
    public const int MaxTracks = 64;

    /// <summary>Known tracks only, each once, capped at <see cref="MaxTracks"/>.</summary>
    public static List<ProtoId<WGCombatTrackPrototype>> Validate(IEnumerable<ProtoId<WGCombatTrackPrototype>>? tracks, IPrototypeManager proto)
    {
        var valid = new List<ProtoId<WGCombatTrackPrototype>>();
        if (tracks == null)
            return valid;

        foreach (var track in tracks)
        {
            if (valid.Count >= MaxTracks)
                break;

            if (proto.HasIndex(track) && !valid.Contains(track))
                valid.Add(track);
        }

        return valid;
    }

    /// <summary>The playlist as stored in the database: comma-separated ids.</summary>
    public static string ToStored(IEnumerable<ProtoId<WGCombatTrackPrototype>> tracks)
    {
        return string.Join(',', tracks.Select(t => t.Id));
    }

    /// <summary>The playlist read back from the database. Unknown ids are dropped later by validation.</summary>
    public static List<ProtoId<WGCombatTrackPrototype>> FromStored(string? stored)
    {
        var tracks = new List<ProtoId<WGCombatTrackPrototype>>();
        if (string.IsNullOrWhiteSpace(stored))
            return tracks;

        foreach (var id in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tracks.Add(new ProtoId<WGCombatTrackPrototype>(id));
        }

        return tracks;
    }

    /// <summary>All tracks in editor order.</summary>
    public static List<WGCombatTrackPrototype> Ordered(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WGCombatTrackPrototype>()
            .OrderBy(t => t.Order)
            .ThenBy(t => t.ID)
            .ToList();
    }
}
