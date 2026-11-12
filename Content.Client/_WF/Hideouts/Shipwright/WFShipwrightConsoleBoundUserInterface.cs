using Content.Client._WF.Hideouts.Blueprint;
using Content.Shared._WF.Hideouts.Blueprint;
using Content.Shared._WF.Hideouts.Shipwright;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Hideouts.Shipwright;

/// <summary>Opens the shipwright console window and passes its orders to the server.</summary>
public sealed class WFShipwrightConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WFShipwrightConsoleWindow? _window;

    /// <summary>The plan of the hull's interior, while it is open.</summary>
    private DefaultWindow? _interior;
    private WFBlueprintPanel? _plan;
    private WFShipwrightConsoleState? _state;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFShipwrightConsoleWindow>();
        _window.OnBuildFrame += frame => SendMessage(new WFShipwrightBuildFrameMessage(frame));
        _window.OnRegister += () => SendMessage(new WFShipwrightRegisterMessage());
        _window.OnCertify += () => SendMessage(new WFShipwrightCertifyMessage());
        _window.OnUpgrade += refit => SendMessage(new WFShipwrightUpgradeMessage(refit));
        _window.OnRefit += (mounts, systems, hardpoints, placements) =>
            SendMessage(new WFShipwrightRefitMessage(mounts, systems, hardpoints, placements));
        _window.OnInterior += OpenInterior;
    }

    /// <summary>Opens the blueprint over the hull in the bay: floors, walls, furniture, decorations and decals.</summary>
    private void OpenInterior()
    {
        if (_interior is { IsOpen: true })
        {
            _interior.MoveToFront();
            return;
        }

        _plan = new WFBlueprintPanel { HorizontalExpand = true, VerticalExpand = true };
        _plan.SetShipMode();
        _plan.OnRequestCanvas += () => SendMessage(new WFBlueprintCanvasRequestMessage());
        _plan.OnApply += strokes => SendMessage(new WFBlueprintApplyMessage(strokes));
        _interior = new DefaultWindow
        {
            Title = Loc.GetString("wf-shipwright-ui-interior-title"),
            MinSize = new System.Numerics.Vector2(980, 640),
        };
        _interior.Contents.AddChild(_plan);
        _interior.OnClose += () =>
        {
            _interior = null;
            _plan = null;
        };
        _interior.OpenCentered();
        UpdateInterior();
    }

    /// <summary>Points the interior plan at the hull in the bay; the plan can be built while the cranes are idle.</summary>
    private void UpdateInterior()
    {
        if (_plan == null)
            return;

        var hull = _state?.Hull is { } view ? EntMan.GetEntity(view.Grid) : (EntityUid?) null;
        _plan.SetOutpost(hull is { Valid: true } ? hull : null, 0, _state is { Working: false });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _interior?.Close();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not WFShipwrightConsoleState shipwright)
            return;

        _state = shipwright;
        _window?.UpdateState(shipwright);
        UpdateInterior();
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFShipwrightCatalogueMessage catalogue)
            _window?.SetCatalogue(catalogue.Guns, catalogue.Mounts);
        else if (message is WFBlueprintCanvasMessage canvas)
            _plan?.SetCanvas(canvas);
    }
}
