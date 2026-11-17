using Content.Shared._WG.ScratchTickets;
using Robust.Client.UserInterface;

namespace Content.Client._WG.ScratchTickets;

/// <summary>Opens a scratch ticket and tells the server once its foil is off.</summary>
public sealed class WGScratchTicketBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WGScratchTicketWindow? _window;

    public WGScratchTicketBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent(Owner, out WGScratchTicketComponent? ticket))
            return;

        var foil = EntMan.EnsureComponent<WGScratchFoilComponent>(Owner);
        if (foil.Alpha.Length != WGScratchFoil.Resolution * WGScratchFoil.Resolution)
        {
            foil.Alpha = new byte[WGScratchFoil.Resolution * WGScratchFoil.Resolution];
            Array.Fill(foil.Alpha, byte.MaxValue);
        }

        _window = this.CreateWindow<WGScratchTicketWindow>();
        _window.SetTicket(Owner, ticket, foil.Alpha);
        _window.OnRevealed += () => SendMessage(new WGScratchTicketRevealedMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WGScratchTicketState ticket)
            _window?.UpdateState(ticket);
    }
}
