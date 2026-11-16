using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.CombatMusic;

/// <summary>Validation and storage of a character's combat playlist.</summary>
public static class WFCombatMusicRules
{
    /// <summary>Most tracks one playlist may hold.</summary>
    public const int MaxTracks = 64;

    /// <summary>Known tracks only, each once, capped at <see cref="MaxTracks"/>.</summary>
    public static List<ProtoId<WFCombatTrackPrototype>> Validate(IEnumerable<ProtoId<WFCombatTrackPrototype>>? tracks, IPrototypeManager proto)
    {
        var valid = new List<ProtoId<WFCombatTrackPrototype>>();
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
    public static string ToStored(IEnumerable<ProtoId<WFCombatTrackPrototype>> tracks)
    {
        return string.Join(',', tracks.Select(t => t.Id));
    }

    /// <summary>The playlist read back from the database. Unknown ids are dropped later by validation.</summary>
    public static List<ProtoId<WFCombatTrackPrototype>> FromStored(string? stored)
    {
        var tracks = new List<ProtoId<WFCombatTrackPrototype>>();
        if (string.IsNullOrWhiteSpace(stored))
            return tracks;

        foreach (var id in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tracks.Add(new ProtoId<WFCombatTrackPrototype>(id));
        }

        return tracks;
    }

    /// <summary>All tracks in editor order.</summary>
    public static List<WFCombatTrackPrototype> Ordered(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WFCombatTrackPrototype>()
            .OrderBy(t => t.Order)
            .ThenBy(t => t.ID)
            .ToList();
    }
}
