using System.Linq;
using System.Numerics;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleDockControl
{
    private static readonly TimeSpan WfClampFlashTime = TimeSpan.FromSeconds(3);
    private static readonly Color WfClampHeldColor = Color.FromHex("#ff6040");
    private static readonly Color WfClampHoldingColor = Color.FromHex("#ffb040");

    private TimeSpan _wfClampFlashUntil;

    /// <summary>Whether another ship's docking clamps hold this one, held by whom, or which ships this one's clamps hold.</summary>
    private (bool Held, string Ships)? WfClampReading()
    {
        if (DockState == null || GridEntity == null)
            return null;

        var us = EntManager.GetNetEntity(GridEntity.Value);
        if (!DockState.Docks.TryGetValue(us, out var ports))
            return null;

        NetEntity? holder = null;
        var held = new List<string>();
        foreach (var port in ports)
        {
            if (port.WfClampedBy is not { } by)
                continue;

            if (by != us)
                holder = by;
            else if (port.GridDockedWith is { } prey)
                held.Add(WfShipName(prey));
        }

        if (holder != null)
            return (true, WfShipName(holder.Value));

        return held.Count > 0 ? (false, string.Join(", ", held.Distinct())) : null;
    }

    private string WfShipName(NetEntity grid)
    {
        var uid = EntManager.GetEntity(grid);
        return (EntManager.EntityExists(uid) ? _shuttles.GetIFFLabel(uid) : null) ?? Loc.GetString("shuttle-console-unknown");
    }

    /// <summary>Whether another ship's docking clamps hold this one.</summary>
    public bool WfClampsHoldUs() => WfClampReading() is { Held: true };

    /// <summary>Flashes the clamp banner with the refusal, after an undock the clamps will not allow.</summary>
    public void WfClampFlash()
    {
        _wfClampFlashUntil = _timing.RealTime + WfClampFlashTime;
    }

    /// <summary>Marks a clamped port's undock action, which flashes the refusal when pressed.</summary>
    private void WfMarkClamped(DockingPortState dock, Button action)
    {
        if (dock.WfClampedBy is not { } by || by == EntManager.GetNetEntity(GridEntity))
            return;

        action.Text = Loc.GetString("wf-clamps-screen-button");
        action.ModulateSelfOverride = WfClampHeldColor;
        action.OnPressed += _ => WfClampFlash();
    }

    /// <summary>A pulsing band across the top of the plot while clamps hold this ship or it holds another.</summary>
    private void WfDrawClampBanner(DrawingHandleScreen handle)
    {
        if (WfClampReading() is not { } reading)
            return;

        var now = (float) _timing.RealTime.TotalSeconds;
        var refused = reading.Held && _timing.RealTime < _wfClampFlashUntil;
        var pulse = refused ? (MathF.Sin(now * 14f) > 0f ? 1f : 0.45f) : 0.7f + 0.3f * MathF.Sin(now * 3f);
        var color = reading.Held ? WfClampHeldColor : WfClampHoldingColor;
        var title = Loc.GetString(reading.Held ? "wf-clamps-screen-held" : "wf-clamps-screen-holding");
        var detail = refused
            ? Loc.GetString("wf-clamps-screen-refused")
            : Loc.GetString(reading.Held ? "wf-clamps-screen-held-by" : "wf-clamps-screen-holding-ships", ("ships", reading.Ships));

        var titleScale = UIScale * 1.4f;
        var margin = 6f * UIScale;
        var titleSize = handle.GetDimensions(Font, title, titleScale);
        var detailSize = handle.GetDimensions(Font, detail, UIScale);
        var band = new UIBox2(0f, 0f, PixelWidth, titleSize.Y + detailSize.Y + margin * 3f);

        handle.DrawRect(band, Color.Black.WithAlpha(0.6f));
        handle.DrawRect(band, color.WithAlpha(0.3f * pulse));
        handle.DrawRect(band, color.WithAlpha(pulse), filled: false);
        handle.DrawString(Font, new Vector2((PixelWidth - titleSize.X) / 2f, margin), title, titleScale, color.WithAlpha(0.6f + 0.4f * pulse));
        handle.DrawString(Font, new Vector2((PixelWidth - detailSize.X) / 2f, margin * 2f + titleSize.Y), detail, UIScale, Color.White);
    }
}
