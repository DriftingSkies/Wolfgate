using Content.Shared._WF.MappingMode;

namespace Content.Client._WF.MappingMode;

public sealed partial class WFMappingClientSystem
{
    /// <summary>The grids on every level of the session as the server last listed them.</summary>
    public List<WFMappingGridInfo> Grids { get; private set; } = new();

    public event Action? GridsUpdated;

    /// <summary>The grid shown in red while its deletion waits for confirmation.</summary>
    public WFMappingGridInfo? PendingDelete { get; private set; }

    public event Action? PendingDeleteChanged;

    /// <summary>The grid the last Next grid went to, so the next press moves on from it.</summary>
    private NetEntity? _lastGrid;

    private void InitializeGrids()
    {
        SubscribeNetworkEvent<WFMappingGridListEvent>(OnGridList);
    }

    private void OnGridList(WFMappingGridListEvent ev)
    {
        Grids = ev.Grids;
        if (PendingDelete is { } pending)
        {
            var index = Grids.FindIndex(g => g.Grid == pending.Grid);
            SetPendingDelete(index < 0 ? null : Grids[index]);
        }

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

    /// <summary>Goes to the grid after the last one visited, in list order and round again; asks for the list if empty.</summary>
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

    /// <summary>Goes to a grid and shows it in red until the deletion is confirmed or cancelled.</summary>
    public void PreviewDelete(NetEntity grid)
    {
        var index = Grids.FindIndex(g => g.Grid == grid);
        SetPendingDelete(index < 0 ? new WFMappingGridInfo(grid, string.Empty, 0, 0) : Grids[index]);
        GoToGrid(grid);
    }

    public void ConfirmDelete()
    {
        if (PendingDelete is not { } pending)
            return;

        SetPendingDelete(null);
        RaiseNetworkEvent(new WFMappingDeleteGridRequest(pending.Grid));
    }

    public void CancelDelete()
    {
        SetPendingDelete(null);
    }

    private void SetPendingDelete(WFMappingGridInfo? grid)
    {
        if (PendingDelete == grid)
            return;

        PendingDelete = grid;
        PendingDeleteChanged?.Invoke();
    }
}
