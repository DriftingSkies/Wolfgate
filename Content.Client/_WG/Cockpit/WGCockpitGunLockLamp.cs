#nullable enable

using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WG.Cockpit;

/// <summary>A flashing lamp over the cockpit view while the ship's guns can't fire: locked by a shield collapse, silent for a vent, or held by heat.</summary>
public sealed partial class WGCockpitGunLockLamp : Control
{
    /// <summary>The size the cockpit gives the lamp when there is room.</summary>
    public static readonly Vector2 Size = new(240, 44);

    [Dependency] private IGameTiming _timing = default!;
    private readonly Func<(int Seconds, bool Held, int Vent)> _read;
    private (int Seconds, bool Held, int Vent) _shown;
    private string _caption = string.Empty;
    private string? _fontSkin;
    private Font _font = default!;

    /// <summary>Whole seconds of gun lock left, from the helm's shield telemetry.</summary>
    public int Seconds => _read().Seconds;

    /// <summary>Whether the ship is too hot to fire through its raised field.</summary>
    public bool Held => _read().Held;

    /// <summary>Whether the ship is venting.</summary>
    public bool Venting => _read().Vent != 0;

    /// <summary>Creates a lamp that never takes the mouse, so aiming and firing pass through it.</summary>
    public WGCockpitGunLockLamp(Func<(int Seconds, bool Held, int Vent)> read)
    {
        IoCManager.InjectDependencies(this);
        _read = read;
        Name = "CockpitGunLock";
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Ignore;
        Refresh();
    }

    /// <summary>Shows the lamp only while the guns can't fire. The cockpit calls this, as a hidden control gets no frame updates.</summary>
    public void Refresh()
    {
        var warning = _read();
        Visible = warning.Seconds > 0 || warning.Held || warning.Vent != 0;
        if (warning == _shown)
            return;
        _shown = warning;
        if (warning.Seconds > 0)
            _caption = Loc.GetString("wg-shield-gun-lock", ("seconds", warning.Seconds));
        else if (warning.Vent != 0)
            _caption = Content.Client._WG.ShipShields.WGShipShieldShuntScreen.VentCaption(warning.Vent);
        else if (warning.Held)
            _caption = Loc.GetString("wg-shield-gun-hold");
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (PixelWidth < 1 || PixelHeight < 1)
            return;
        var skin = WGInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            _font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 14);
            _fontSkin = skin.Id;
        }
        var lit = _timing.RealTime.TotalSeconds % 0.5 < 0.3;
        // A lock is a fault and shows red; a vent or a heat hold clears itself and shows amber.
        var lamp = _shown.Seconds > 0 ? skin.Danger : skin.Caution;
        var inner = WGConsoleAnnunciator.Face(handle, PixelSizeBox, UIScale, lamp, lit);
        var color = lit ? WGInstrumentTheme.Digital ? lamp : skin.Ink : skin.Text;
        var scale = WGInstrumentText.FitScale(handle, _font, _caption, UIScale, inner.Width - 8 * UIScale, inner.Height - 4 * UIScale);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(_font, _caption, scale).X;
        handle.DrawString(_font, inner.Center - new Vector2(width / 2, _font.GetHeight(scale) / 2f), _caption, scale, color);
    }
}
