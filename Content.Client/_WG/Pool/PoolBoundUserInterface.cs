using Content.Shared._WG.Pool;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Pool;

/// <summary>Opens the pool table's window and sends what the player does at it; the server plays every shot.</summary>
public sealed class PoolBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PoolWindow? _window;

    public PoolBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PoolWindow>();
        _window.OnShoot += (angle, power) => SendMessage(new PoolShootMessage(angle, power));
        _window.OnPlace += at => SendMessage(new PoolPlaceCueMessage(at));
        _window.OnSeat += seat => SendMessage(new PoolSeatMessage(seat));
        _window.OnRerack += () => SendMessage(new PoolRerackMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PoolUiState pool)
            _window?.UpdateState(pool);
    }
}
