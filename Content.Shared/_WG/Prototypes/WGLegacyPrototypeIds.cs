using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;

namespace Content.Shared._WG.Prototypes;

/// <summary>Old ids of renamed Wolfgate prototypes that saved data can still hold, one table per prototype kind.</summary>
/// <remarks>Entity renames go in Resources/migration.yml instead.</remarks>
public static class WGLegacyPrototypeIds
{
    /// <summary>consentToggle ids stored in consent_toggle rows.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConsentToggles = new Dictionary<string, string>
    {
        ["AnatomySurgery"] = "WGAnatomySurgery",
        ["UndergarmentStrip"] = "WGUndergarmentStrip",
    };

    /// <summary>genitalShape ids stored in the profile anatomy JSON and in character exports.</summary>
    public static readonly IReadOnlyDictionary<string, string> GenitalShapes = new Dictionary<string, string>
    {
        ["GenitalShapeBreastsPair"] = "WGGenitalShapeBreastsPair",
        ["GenitalShapeBreastsPairRound"] = "WGGenitalShapeBreastsPairRound",
        ["GenitalShapeBreastsQuad"] = "WGGenitalShapeBreastsQuad",
        ["GenitalShapeBreastsQuadLite"] = "WGGenitalShapeBreastsQuadLite",
        ["GenitalShapeBreastsQuadPlus"] = "WGGenitalShapeBreastsQuadPlus",
        ["GenitalShapeBreastsSextuple"] = "WGGenitalShapeBreastsSextuple",
        ["GenitalShapeBreastsUdders"] = "WGGenitalShapeBreastsUdders",
        ["GenitalShapePenisBarbKnot"] = "WGGenitalShapePenisBarbKnot",
        ["GenitalShapePenisFlared"] = "WGGenitalShapePenisFlared",
        ["GenitalShapePenisHemi"] = "WGGenitalShapePenisHemi",
        ["GenitalShapePenisHemiKnot"] = "WGGenitalShapePenisHemiKnot",
        ["GenitalShapePenisHuman"] = "WGGenitalShapePenisHuman",
        ["GenitalShapePenisKnotted"] = "WGGenitalShapePenisKnotted",
        ["GenitalShapePenisNondescript"] = "WGGenitalShapePenisNondescript",
        ["GenitalShapePenisTapered"] = "WGGenitalShapePenisTapered",
        ["GenitalShapePenisTentacle"] = "WGGenitalShapePenisTentacle",
        ["GenitalShapePenisTipKnotted"] = "WGGenitalShapePenisTipKnotted",
        ["GenitalShapeTesticlesPair"] = "WGGenitalShapeTesticlesPair",
        ["GenitalShapeVaginaDentate"] = "WGGenitalShapeVaginaDentate",
        ["GenitalShapeVaginaFurred"] = "WGGenitalShapeVaginaFurred",
        ["GenitalShapeVaginaHaired"] = "WGGenitalShapeVaginaHaired",
        ["GenitalShapeVaginaHuman"] = "WGGenitalShapeVaginaHuman",
        ["GenitalShapeVaginaOpen"] = "WGGenitalShapeVaginaOpen",
        ["GenitalShapeVaginaSlit"] = "WGGenitalShapeVaginaSlit",
        ["GenitalShapeVaginaSpade"] = "WGGenitalShapeVaginaSpade",
        ["GenitalShapeVaginaTentacle"] = "WGGenitalShapeVaginaTentacle",
    };

    /// <summary>hudTheme and uiTheme ids (they share one) stored in the interface.theme client CVar.</summary>
    public static readonly IReadOnlyDictionary<string, string> HudThemes = new Dictionary<string, string>
    {
        ["WolfgateRetroTheme"] = "WGRetroTheme",
        ["WolfgateTheme"] = "WGTheme",
    };

    /// <summary>loadout ids stored in profile_loadout rows and in character exports.</summary>
    public static readonly IReadOnlyDictionary<string, string> Loadouts = new Dictionary<string, string>
    {
        ["ContractorMosinLoadout"] = "WGContractorMosinLoadout",
    };

    /// <summary>shipAlertCode ids stored in ShipAlertComponent.Code on saved grids.</summary>
    public static readonly IReadOnlyDictionary<string, string> ShipAlertCodes = new Dictionary<string, string>
    {
        ["ShipCodeBlack"] = "WGShipCodeBlack",
        ["ShipCodeGreen"] = "WGShipCodeGreen",
        ["ShipCodeRed"] = "WGShipCodeRed",
        ["ShipCodeYellow"] = "WGShipCodeYellow",
    };

    /// <summary>species ids stored in profile rows and in character exports.</summary>
    public static readonly IReadOnlyDictionary<string, string> Species = new Dictionary<string, string>
    {
        ["Canine"] = "WGCanine",
        ["ProtoArachnid"] = "Protogen",
        ["ProtoAvali"] = "Protogen",
        ["ProtoDawi"] = "Protogen",
        ["ProtoDionae"] = "Protogen",
        ["ProtoFeline"] = "Protogen",
        ["ProtoHumie"] = "Protogen",
        ["ProtoKin"] = "Protogen",
        ["ProtoMoth"] = "Protogen",
        ["ProtoReptile"] = "Protogen",
        ["ProtoResomi"] = "Protogen",
        ["ProtoSlimePerson"] = "Protogen",
        ["ProtoThaven"] = "Protogen",
        ["ProtoVox"] = "Protogen",
        ["ProtoVulp"] = "Protogen",
    };

    // Written apart so Tools/_WG/Prototypes/wg_prefix_rename.py leaves the old prefix alone.
    private const string OldPrefix = "W" + "F";
    private static readonly Regex OldPrefixed = new($"(?<![A-Za-z0-9]){OldPrefix}(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>Moves a profile's species, loadouts and appearance off renamed ids; run before it is validated.</summary>
    public static void ResolveProfile(HumanoidCharacterProfile profile)
    {
        profile.Species = Resolve(Species, profile.Species.Id);
        foreach (var role in profile.Loadouts.Values)
        {
            foreach (var loadouts in role.SelectedLoadouts.Values)
            {
                foreach (var loadout in loadouts)
                    loadout.Prototype = Resolve(Loadouts, loadout.Prototype.Id);
            }
        }

        var appearance = profile.Appearance;
        appearance.HairStyleId = Prefix(appearance.HairStyleId);
        appearance.FacialHairStyleId = Prefix(appearance.FacialHairStyleId);
        for (var i = 0; i < appearance.Markings.Count; i++)
        {
            var marking = appearance.Markings[i];
            var id = Prefix(marking.MarkingId);
            if (id == marking.MarkingId)
                continue;

            appearance.Markings[i] = new Marking(id, marking.MarkingColors) { Visible = marking.Visible, Forced = marking.Forced };
        }
    }

    /// <summary>The current id for an old one from the table or under the old prefix; any other id, or null, is returned unchanged.</summary>
    [return: NotNullIfNotNull(nameof(id))]
    public static string? Resolve(IReadOnlyDictionary<string, string> table, string? id)
    {
        return id != null && table.TryGetValue(id, out var current) ? current : Prefix(id);
    }

    /// <summary>The text with every id under the old Wolfgate prefix moved to the current one (WGCanine).</summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Prefix(string? text)
    {
        return text == null || !text.Contains(OldPrefix) ? text : OldPrefixed.Replace(text, "WG");
    }
}
