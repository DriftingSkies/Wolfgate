using Content.Client._WF.MappingMode.Link;
using Content.Client.Decals;
using Robust.Shared.Network;
using Content.Client.Decals.Overlays;

namespace Content.Client._WF.MappingMode;

/// <summary>A view layer the mapping screen can show or hide on this client.</summary>
public enum WFMappingLayer : byte
{
    Walls,
    Subfloor,
    Decals,
    Markers,
    Lights,

    /// <summary>Signal links and device lists drawn as lines.</summary>
    Links,
}

public sealed partial class WFMappingClientSystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private IClientNetManager _netManager = default!;
    [Dependency] private WFMappingLinkSystem _link = default!;

    public static readonly WFMappingLayer[] Layers =
    {
        WFMappingLayer.Walls,
        WFMappingLayer.Subfloor,
        WFMappingLayer.Decals,
        WFMappingLayer.Markers,
        WFMappingLayer.Lights,
        WFMappingLayer.Links,
    };

    /// <summary>Whether a layer is shown.</summary>
    public bool GetLayer(WFMappingLayer layer)
    {
        return layer switch
        {
            WFMappingLayer.Walls => !_tools.WallsHidden,
            WFMappingLayer.Subfloor => _subFloor.ShowAll,
            WFMappingLayer.Decals => _overlays.HasOverlay<DecalOverlay>(),
            WFMappingLayer.Markers => _markers.MarkersVisible,
            WFMappingLayer.Links => _link.ShowAll,
            _ => _light.Enabled,
        };
    }

    /// <summary>Shows or hides a layer.</summary>
    public void SetLayer(WFMappingLayer layer, bool shown)
    {
        switch (layer)
        {
            case WFMappingLayer.Walls:
                _tools.SetWallsHidden(!shown);
                break;
            case WFMappingLayer.Subfloor:
                // The setter asks the server too, which logs an error once the client has disconnected.
                if (_subFloor.ShowAll != shown && _netManager.IsConnected)
                    _subFloor.ShowAll = shown;
                break;
            case WFMappingLayer.Decals:
                if (GetLayer(WFMappingLayer.Decals) != shown)
                    _decalSystem.ToggleOverlay();
                break;
            case WFMappingLayer.Markers:
                _markers.MarkersVisible = shown;
                break;
            case WFMappingLayer.Lights:
                _light.Enabled = shown;
                break;
            case WFMappingLayer.Links:
                _link.SetShowAll(shown);
                break;
        }
    }

    /// <summary>
    /// Puts every layer back: the mapping defaults (markers, subfloor, no lighting, no links) or the normal game's.
    /// </summary>
    private void ResetLayers(bool mapping)
    {
        SetLayer(WFMappingLayer.Walls, true);
        SetLayer(WFMappingLayer.Decals, true);
        SetLayer(WFMappingLayer.Subfloor, mapping);
        SetLayer(WFMappingLayer.Markers, mapping);
        SetLayer(WFMappingLayer.Lights, !mapping);
        SetLayer(WFMappingLayer.Links, false);
    }
}
