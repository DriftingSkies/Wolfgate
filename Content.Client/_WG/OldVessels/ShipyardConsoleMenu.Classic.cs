using System.Linq;
using Content.Client._WG.OldVessels;
using Content.Shared._NF.Shipyard.Prototypes;

namespace Content.Client._NF.Shipyard.UI;

/// <summary>
/// The shipyard menu's classic ships filter (all, current only, classic only) and the Classic tag on its rows.
/// </summary>
public sealed partial class ShipyardConsoleMenu
{
    private enum ClassicFilterMode : byte
    {
        All,
        Current,
        Classic,
    }

    private ClassicFilterMode _classicFilter = ClassicFilterMode.All;

    private void InitClassicFilter()
    {
        ClassicFilter.AddItem(Loc.GetString("wg-shipyard-classic-filter-all"), (int) ClassicFilterMode.All);
        ClassicFilter.AddItem(Loc.GetString("wg-shipyard-classic-filter-current"), (int) ClassicFilterMode.Current);
        ClassicFilter.AddItem(Loc.GetString("wg-shipyard-classic-filter-classic"), (int) ClassicFilterMode.Classic);
        ClassicFilter.Visible = false;
        ClassicFilter.OnItemSelected += args =>
        {
            ClassicFilter.SelectId(args.Id);
            _classicFilter = (ClassicFilterMode) args.Id;
            PopulateProducts(_lastAvailableProtos, _lastUnavailableProtos, _freeListings, _validId);
        };
    }

    /// <summary>
    /// Shows the filter only when the listing has a classic ship; otherwise it resets to all ships.
    /// </summary>
    public void PopulateClassicFilter(List<string> availablePrototypes, List<string> unavailablePrototypes)
    {
        var anyClassic = availablePrototypes.Concat(unavailablePrototypes)
            .Any(id => _protoManager.TryIndex<VesselPrototype>(id, out var vessel) && ClassicVessels.IsClassic(vessel));
        ClassicFilter.Visible = anyClassic;

        if (!anyClassic && _classicFilter != ClassicFilterMode.All)
        {
            _classicFilter = ClassicFilterMode.All;
            PopulateProducts(_lastAvailableProtos, _lastUnavailableProtos, _freeListings, _validId);
        }

        ClassicFilter.SelectId((int) _classicFilter);
    }

    private bool PassesClassicFilter(VesselPrototype vessel)
    {
        return _classicFilter switch
        {
            ClassicFilterMode.Current => !ClassicVessels.IsClassic(vessel),
            ClassicFilterMode.Classic => ClassicVessels.IsClassic(vessel),
            _ => true,
        };
    }

    /// <summary>
    /// Leads a classic ship's description with the Classic tag; the header row has no width to spare.
    /// </summary>
    private static void MarkClassic(VesselRow row)
    {
        if (row.Vessel == null || !ClassicVessels.IsClassic(row.Vessel))
            return;

        row.VesselDescription.Text = Loc.GetString("wg-shipyard-classic-description",
            ("description", row.VesselDescription.Text ?? string.Empty));
    }
}
