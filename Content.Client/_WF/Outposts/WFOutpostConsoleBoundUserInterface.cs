using Content.Shared._WF.Colonies;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.ShipAccess;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Outposts;

/// <summary>Opens the outpost console window and sends the owner's renames, saves, loads, orders, sales and access edits.</summary>
public sealed class WFOutpostConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFOutpostConsoleWindow? _window;

    public WFOutpostConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFOutpostConsoleWindow>();
        _window.OnRename += name => SendMessage(new WFOutpostRenameMessage(name));
        _window.OnSave += slot => SendMessage(new WFOutpostSaveMessage(slot));
        _window.OnLoad += slot => SendMessage(new WFOutpostLoadMessage(slot));
        _window.OnLoadPreset += preset => SendMessage(new WFOutpostLoadPresetMessage(preset));
        _window.OnDelete += slot => SendMessage(new WFOutpostDeleteSaveMessage(slot));
        _window.OnCancelLoad += () => SendMessage(new WFOutpostCancelLoadMessage());
        _window.OnOrder += product => SendMessage(new WFOutpostOrderMessage(product));
        _window.OnSell += () => SendMessage(new WFOutpostSellMessage());
        _window.OnSellAtRoundEnd += enabled => SendMessage(new WFOutpostSellAtRoundEndMessage(enabled));
        _window.OnOffer += (buyer, price) => SendMessage(new WFOutpostOfferMessage(buyer, price));
        _window.OnWithdrawOffer += () => SendMessage(new WFOutpostWithdrawOfferMessage());
        _window.OnRefreshBuyers += () => SendMessage(new WFOutpostRefreshBuyersMessage());

        var development = _window.DevelopmentTab;
        development.OnKeep += (good, keep) => SendMessage(new WFColonyKeepMessage(good, keep));
        development.OnDefence += engage => SendMessage(new WFColonyDefenceMessage(engage));
        development.OnOpen += open => SendMessage(new WFColonyOpenMessage(open));
        development.OnPermit += (holder, granted) => SendMessage(new WFColonyPermitMessage(holder, granted));
        development.OnBerth += (holder, open) => SendMessage(new WFColonyBerthMessage(holder, open));
        development.OnYardKit += () => SendMessage(new WFColonyYardKitMessage());

        var hideout = _window.HideoutTab;
        hideout.OnBuild += module => SendMessage(new WFHideoutBuildMessage(module));
        hideout.OnRegister += () => SendMessage(new WFHideoutRegisterShipMessage());
        hideout.OnUnregister += () => SendMessage(new WFHideoutUnregisterShipMessage());
        hideout.OnRecall += () => SendMessage(new WFHideoutRecallShipMessage());
        hideout.OnInsure += () => SendMessage(new WFHideoutInsureMessage());
        hideout.OnClaim += () => SendMessage(new WFHideoutClaimMessage());
        hideout.OnMoveHome += world => SendMessage(new WFHideoutMoveHomeMessage(world));

        var access = _window.AccessTab;
        access.ModeChanged += mode => SendMessage(new WFShipAccessSetModeMessage(mode));
        access.LockedChanged += locked => SendMessage(new WFShipAccessSetLockedMessage(locked));
        access.AddRequested += target => SendMessage(new WFShipAccessAddPlayerMessage(target));
        access.RemoveRequested += key => SendMessage(new WFShipAccessRemoveMessage(key));
        access.BuilderChanged += (key, builder) => SendMessage(new WFShipAccessSetBuilderMessage(key, builder));
        access.DoorRuleChanged += (door, rule) => SendMessage(new WFShipAccessSetDoorRuleMessage(door, rule));
        access.AllDoorRulesChanged += rule => SendMessage(new WFShipAccessSetAllDoorRulesMessage(rule));
        access.DoorPlayerChanged += (door, key, listed) => SendMessage(new WFShipAccessSetDoorPlayerMessage(door, key, listed));
        access.CodesRequested += () => SendMessage(new WFShipAccessRequestCodesMessage());
        access.ShipCodeChanged += code => SendMessage(new WFShipAccessSetShipCodeMessage(code));
        access.DoorCodeChanged += (door, code) => SendMessage(new WFShipAccessSetDoorCodeMessage(door, code));
        _window.SetConsole(Owner, EntMan.GetComponent<TransformComponent>(Owner).GridUid);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not WFOutpostConsoleState outpost || _window == null)
            return;

        _window.SetConsole(Owner, EntMan.GetComponent<TransformComponent>(Owner).GridUid);
        _window.UpdateState(outpost);
    }
}
