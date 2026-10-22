using Content.Shared._WF.MappingMode;

namespace Content.Client._WF.MappingMode;

public sealed partial class WFMappingClientSystem
{
    /// <summary>The grids on the mapper's level as the server last listed them, largest first.</summary>
    public List<WFMappingGridInfo> Grids { get; private set; } = new();

    public event Action? GridsUpdated;

    /// <summary>The grid the last Next grid went to, so the next press moves on from it.</summary>
    private NetEntity? _lastGrid;

    private void InitializeGrids()
    {
        SubscribeNetworkEvent<WFMappingGridListEvent>(OnGridList);
    }

    private void OnGridList(WFMappingGridListEvent ev)
    {
        Grids = ev.Grids;
        GridsUpdated?.Invoke();
    }

    public void RequestGrids()
    {
        RaiseNetworkEvent(new WFMappingGridListRequest());
    }

    public void GoToGrid(NetEntity grid)
    {
        _lastGrid = grid;
        RaiseNetworkEvent(new WFMappingGoToGridRequest(grid));
    }

    /// <summary>Goes to the grid after the last one visited, largest first and round again; asks for the list if empty.</summary>
    public void GoToNextGrid()
    {
        if (Grids.Count == 0)
        {
            RequestGrids();
            ReportLocal("wf-mapping-ship-menu-grids-none", false);
            return;
        }

        var index = _lastGrid is { } last ? Grids.FindIndex(g => g.Grid == last) + 1 : 0;
        GoToGrid(Grids[index % Grids.Count].Grid);
    }
}
