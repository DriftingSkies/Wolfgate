using Robust.Shared.Serialization;

namespace Content.Shared._WG.RoundSetup;

/// <summary>Optional starting rosters whose openings remain individually editable.</summary>
[Serializable, NetSerializable]
public enum WGCrewRoster : byte { Medical, Salvage, Service }

/// <summary>Ordinary ship professions; station command and antagonist jobs remain outside crew recruitment.</summary>
public static class WGShipCrewJobs
{
    public static readonly string[] Jobs =
    {
        "Contractor", "Pilot", "Mercenary", "StationEngineer", "AtmosphericTechnician",
        "MedicalDoctor", "MdMedic", "Chemist", "Paramedic", "SalvageSpecialist",
        "Scientist", "CargoTechnician", "Botanist", "Chef", "Bartender", "Janitor",
        "Borg",
    };

    /// <summary>Supplies localized role labels, real jobs and capacities without assigning any player.</summary>
    public static (string Name, string Job, int Capacity)[] Roster(WGCrewRoster roster) => roster switch
    {
        WGCrewRoster.Medical => new[] { ("captain", "Contractor", 1), ("responders", "MdMedic", 3), ("chemist", "Chemist", 1), ("engineer", "StationEngineer", 1) },
        WGCrewRoster.Salvage => new[] { ("captain", "Contractor", 1), ("salvagers", "SalvageSpecialist", 4), ("engineer", "StationEngineer", 1) },
        WGCrewRoster.Service => new[] { ("captain", "Contractor", 1), ("chef", "Chef", 2), ("bartender", "Bartender", 2), ("botanist", "Botanist", 1) },
        _ => Array.Empty<(string, string, int)>(),
    };
}
