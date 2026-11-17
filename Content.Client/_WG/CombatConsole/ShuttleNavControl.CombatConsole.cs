using System.Numerics;
using Content.Client._WG.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using ConsoleTheme = Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private bool _wgInstrumentRadar;

    /// <summary>Retains the radar's original mode controls above the instrument glass.</summary>
    public void WgAttachRadarControls(LayoutContainer parent)
    {
        _wgInstrumentRadar = true;
        EnsureRadarModeButtons();
        if (_radarModeButtons == null)
            return;

        _radarModeButtons.Name = "WgRadarModes";
        _radarModeButtons.SeparationOverride = 4;
        _radarModeButtons.MinSize = _radarModeButtons.MaxSize = new Vector2(212, 32);
        WgStyleMode(_radarAzimuthButton!, "WgRadarAzimuth", () => _azimuthMode != RadarAzimuthMode.Off);
        WgStyleMode(_radarRotationButton!, "WgRadarRotation", () => _angleFollow);
        WgStyleMode(_radarAnchorButton!, "WgRadarAnchor", () => _relativePanning);
        WgStyleMode(_radarResetButton!, "WgRadarReset", () => false);
        if (_wgZoomToMouseButton != null)
            WgStyleMode(_wgZoomToMouseButton, "WgRadarZoomToMouse", () => _wgZoomToMouse);
        if (_wgAllNamesButton != null)
            WgStyleMode(_wgAllNamesButton, "WgRadarAllNames", () => _wgAllNames);
        Move(_radarModeButtons, LayoutContainer.LayoutPreset.BottomLeft);
        if (_wgTerrainButton != null)
        {
            _wgTerrainButton.Name = "WgRadarTerrain";
            _wgTerrainButton.AddStyleClass("WgCompact");
            _wgTerrainButton.MinSize = _wgTerrainButton.MaxSize = new Vector2(120, 32);
            ConsoleTheme.Switch(_wgTerrainButton);
            Move(_wgTerrainButton, LayoutContainer.LayoutPreset.TopRight);
        }
        WgAttachWaypointControls(parent);

        void Move(Control control, LayoutContainer.LayoutPreset preset)
        {
            if (control.Parent != parent)
                parent.AddChild(ConsoleTheme.Detach(control));
            control.SetPositionLast();
            LayoutContainer.SetAnchorAndMarginPreset(control, preset, margin: 8);
        }
    }

    /// <summary>Moves overlay controls with a radar borrowed by the cockpit.</summary>
    protected override void Parented(Control newParent)
    {
        base.Parented(newParent);
        if (_wgInstrumentRadar && newParent is LayoutContainer layers)
            WgAttachRadarControls(layers);
    }

#pragma warning disable CS0672, CS0618 // Dispose is still how the engine tears a control down, and this cleanup depends on it.
    protected override void Dispose(bool disposing)
    {
        if (disposing && _wgInstrumentRadar)
        {
            _radarModeButtons?.Dispose();
            _wgTerrainButton?.Dispose();
            WgDisposeWaypointControls();
        }
        base.Dispose(disposing);
    }
#pragma warning restore CS0672, CS0618

    private static void WgStyleMode(RadarModeButton button, string name, Func<bool> active)
    {
        button.Name = name;
        button.WgInstrument = true;
        button.WgActive = active;
        button.MinSize = button.MaxSize = new Vector2(32);
        button.ModulateSelfOverride = Color.White;
        button.MuteSounds = true;
        button.OnPressed -= WGConsoleAudio.Press;
        button.OnPressed += WGConsoleAudio.Press;
    }

    public sealed partial class RadarModeButton
    {
        internal bool WgInstrument;
        internal Func<bool>? WgActive;
        private readonly WGConsoleStyleBox _wgUp = new("button_up", false, false, compact: true);
        private readonly WGConsoleStyleBox _wgHover = new("button_hover", false, false, compact: true);
        private readonly WGConsoleStyleBox _wgDown = new("button_down", false, true, compact: true);
        private DrawVertexUV2DColor[] _wgVertices = Array.Empty<DrawVertexUV2DColor>();

        /// <summary>Draws the original radar functions as legible controls in either instrument palette.</summary>
        private bool WgDrawInstrument(DrawingHandleScreen handle)
        {
            if (!WgInstrument)
                return false;
            var face = DrawMode == DrawModeEnum.Pressed ? _wgDown : DrawMode == DrawModeEnum.Hover ? _wgHover : _wgUp;
            face.Draw(handle, UIBox2.FromDimensions(Vector2.Zero, PixelSize), UIScale);
            var active = WgActive?.Invoke() == true;
            var ink = Disabled ? ConsoleTheme.Muted : active ? ConsoleTheme.Accent : ConsoleTheme.Cream;
            var center = new Vector2(PixelWidth / 2f, PixelHeight / 2f);
            var radius = 8 * UIScale;
            var stroke = Math.Max(1, UIScale);
            void Line(float x1, float y1, float x2, float y2) =>
                handle.DrawLine(center + new Vector2(x1, y1) * radius, center + new Vector2(x2, y2) * radius, ink);
            void Arc(Vector2 origin, float size, float start, float end) =>
                WGConsoleDigital.Arc(handle, origin, size, stroke, start, end, ink, ref _wgVertices);
            switch (_icon)
            {
                case RadarModeButtonIcon.Azimuth:
                    Arc(center, radius * 0.8f, 0, MathF.Tau);
                    Line(-1, 0, 1, 0);
                    Line(0, -1, 0, 1);
                    break;
                case RadarModeButtonIcon.Rotation:
                    Arc(center, radius, -MathF.PI * 0.8f, MathF.PI * 0.8f);
                    Line(-0.8f, 0.6f, -0.3f, 0.7f);
                    Line(-0.8f, 0.6f, -0.7f, 0.1f);
                    break;
                case RadarModeButtonIcon.Anchor:
                    Arc(center + new Vector2(0, -radius * 0.6f), radius * 0.25f, 0, MathF.Tau);
                    Line(0, -0.35f, 0, 0.9f);
                    Line(-0.45f, -0.1f, 0.45f, -0.1f);
                    Line(0, 0.9f, -0.8f, 0.3f);
                    Line(0, 0.9f, 0.8f, 0.3f);
                    Line(-0.8f, 0.3f, -0.8f, 0.65f);
                    Line(0.8f, 0.3f, 0.8f, 0.65f);
                    break;
                case RadarModeButtonIcon.Reset:
                    Arc(center, radius * 0.9f, -MathF.PI * 0.15f, MathF.PI * 1.45f);
                    Line(-0.15f, -0.9f, -0.55f, -0.5f);
                    Line(-0.15f, -0.9f, 0.25f, -0.5f);
                    WGConsoleDigital.Dot(handle, center, 1.5f * UIScale, ink, ref _wgVertices);
                    break;
                case RadarModeButtonIcon.WgZoomToMouse:
                    WgDrawZoomToMouse(handle, center, radius, ink);
                    break;
                case RadarModeButtonIcon.WgAllNames:
                    WgDrawAllNames(handle, center, radius, ink);
                    break;
            }
            if (active)
                handle.DrawLine(new Vector2(7, 28) * UIScale, new Vector2(25, 28) * UIScale, ink);
            return true;
        }
    }
}
