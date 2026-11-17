using Robust.Shared.Configuration;

namespace Content.Shared._WG.Companies;

/// <summary>Server settings for creating companies.</summary>
[CVarDefs]
public sealed class WGCompanyCVars
{
    /// <summary>Hours a player must have played in total before they can create a company.</summary>
    public static readonly CVarDef<int> CreateHours =
        CVarDef.Create("wg.companies.create_hours", 25, CVar.SERVERONLY);

    /// <summary>Thalers taken from the founder's bank to create a company; the fee leaves the economy.</summary>
    public static readonly CVarDef<int> CreateFee =
        CVarDef.Create("wg.companies.create_fee", 100_000, CVar.SERVERONLY);
}
