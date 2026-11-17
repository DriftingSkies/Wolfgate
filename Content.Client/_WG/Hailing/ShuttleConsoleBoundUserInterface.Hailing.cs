using Content.Client._WG.Hailing;
using Content.Shared._WG.Hailing;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WgHailOpen()
    {
        if (_window == null)
            return;

        _window.WgHailAttach(this.CreateDisposableControl<HailWindow>());

        _window.HailTargetsRequested += () => SendMessage(new HailTargetsRequestMessage());
        _window.HailRequested += ship => SendMessage(new HailRequestMessage(ship));
        _window.HailAnswerRequested += accept => SendMessage(new HailAnswerMessage(accept));
        _window.HailEndRequested += () => SendMessage(new HailEndMessage());
        _window.HailSayRequested += text => SendMessage(new HailSayMessage(text));
        _window.HailChooseRequested += id => SendMessage(new HailChooseMessage(id));
        _window.HailOfferRequested += () => SendMessage(new HailOfferServiceMessage());
    }

    private void WgHailReceiveMessage(BoundUserInterfaceMessage message)
    {
        switch (message)
        {
            case HailTargetsMessage targets:
                _window?.WgHailTargets(targets.Targets);
                break;

            case HailEndedMessage ended:
                _window?.WgHailEnded(ended.Reason, ended.Ship);
                break;
        }
    }
}
