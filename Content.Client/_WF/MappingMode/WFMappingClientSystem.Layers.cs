using Content.Client._WF.MappingMode.Link;
using Content.Client.Decals;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.MappingMode;
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

    /// <summary>The world tile grid every level shares.</summary>
    WorldGrid,

    /// <summary>Levels below drawn smaller, as on a planet.</summary>
    Perspective,
}

public sealed partial class WFMappingClientSystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private IClientNetManager _netManager = default!;
    [Dependency] private WFMappingLinkSystem _link = default!;

    /// <summary>The link system, for the Layers panel's count of what the Links layer draws.</summary>
    public WFMappingLinkSystem Links => _link;

    public static readonly WFMappingLayer[] Layers =
    {
        WFMappingLayer.Walls,
        WFMappingLayer.Subfloor,
        WFMappingLayer.Decals,
        WFMappingLayer.Markers,
        WFMappingLayer.Lights,
        WFMappingLayer.Links,
        WFMappingLayer.WorldGrid,
        WFMappingLayer.Perspective,
    };

    /// <summary>Whether the Depth perspective layer is on.</summary>
    private bool _perspective;

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
            WFMappingLayer.WorldGrid => _overlays.HasOverlay<WFMappingWorldGridOverlay>(),
            WFMappingLayer.Perspective => _perspective,
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
            case WFMappingLayer.WorldGrid:
                if (!shown)
                    _overlays.RemoveOverlay<WFMappingWorldGridOverlay>();
                else if (!_overlays.HasOverlay<WFMappingWorldGridOverlay>())
                    _overlays.AddOverlay(new WFMappingWorldGridOverlay(this));
                break;
            case WFMappingLayer.Perspective:
                _perspective = shown;
                UpdatePerspective();
                break;
        }
    }

    /// <summary>
    /// Puts every layer back: the mapping defaults (markers, subfloor, world grid, no lighting, no links) or the normal
    /// game's.
    /// </summary>
    private void ResetLayers(bool mapping)
    {
        SetLayer(WFMappingLayer.Walls, true);
        SetLayer(WFMappingLayer.Decals, true);
        SetLayer(WFMappingLayer.Subfloor, mapping);
        SetLayer(WFMappingLayer.Markers, mapping);
        SetLayer(WFMappingLayer.Lights, !mapping);
        SetLayer(WFMappingLayer.Links, false);
        SetLayer(WFMappingLayer.WorldGrid, mapping);
        SetLayer(WFMappingLayer.Perspective, false);
    }

    /// <summary>Puts the perspective marker on every level while the layer is on, and takes it off otherwise.</summary>
    private void UpdatePerspective()
    {
        var query = AllEntityQuery<CEZMapComponent>(); // All: session levels are paused
        while (query.MoveNext(out var uid, out _))
        {
            if (_perspective)
                EnsureComp<WFMappingPerspectiveComponent>(uid);
            else
                RemComp<WFMappingPerspectiveComponent>(uid);
        }
    }
}
