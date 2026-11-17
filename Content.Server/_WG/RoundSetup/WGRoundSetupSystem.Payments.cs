using System.Linq;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.RoundSetup;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.RoundSetup;

public sealed partial class WGRoundSetupSystem
{
    private int VesselPrice(Lobby lobby) => lobby.HangarLicence == null && lobby.Vessel != null && _prototypes.TryIndex<VesselPrototype>(lobby.Vessel, out var vessel) ? vessel.Price : 0;

    private string? EligiblePurchaseJob(Lobby lobby, ICommonSession host) =>
        lobby.Members[host.UserId].Job is { } job && IsJobAllowed(host, job) ? job : null;

    private bool CanBuyVessel(Lobby lobby, ICommonSession host, string? vessel) =>
        _ships.IsVesselAvailable(vessel, lobby.Faction, EligiblePurchaseJob(lobby, host));

    /// <summary>New vessels and successor leaders start with host payment and require fresh approvals.</summary>
    private void ResetContributions(Lobby lobby)
    {
        foreach (var (user, member) in lobby.Members)
            member.Contribution = user == lobby.Host ? VesselPrice(lobby) : 0;
        UnreadyMembers(lobby);
    }

    /// <summary>Only the leader may allocate bounded shares before launch; any edit revokes all approvals.</summary>
    private bool SetContribution(Lobby lobby, WGRoundSetupRequest request)
    {
        if (lobby.Started || !WGRoundSetupPolicy.IsShip(lobby.Scenario) || lobby.Vessel == null ||
            request.PaymentRevision != lobby.PaymentRevision || request.Target is not { } target ||
            !lobby.Members.TryGetValue(target, out var member) || request.Contribution < 0 || request.Contribution > VesselPrice(lobby))
            return false;
        if (member.Contribution == request.Contribution)
            return true;
        member.Contribution = request.Contribution;
        UnreadyMembers(lobby);
        return true;
    }

    /// <summary>Rejects approval of an outdated on-screen allocation without charging anyone.</summary>
    private bool ValidatePaymentQuote(Lobby lobby, ICommonSession session, int? revision, out string error)
    {
        error = "wg-round-setup-payment-changed";
        if (lobby.Started || !WGRoundSetupPolicy.IsShip(lobby.Scenario) || lobby.HangarLicence != null)
            return true;
        // A zero-share passenger never authorizes a withdrawal.
        return (lobby.Members[session.UserId].Contribution == 0 && lobby.Host != session.UserId || revision == lobby.PaymentRevision) &&
            ValidatePaymentFunds(lobby, session, out error);
    }

    private bool ValidatePaymentFunds(Lobby lobby, ICommonSession session, out string error)
    {
        error = string.Empty;
        // A hangar launch is paid by the company when the ship spawns.
        if (lobby.HangarLicence != null)
            return true;
        error = "wg-round-setup-payment-total-error";
        var price = VesselPrice(lobby);
        if (price <= 0 || lobby.Members.Values.Sum(member => (long) member.Contribution) != price)
            return false;
        var amount = lobby.Members[session.UserId].Contribution;
        error = "wg-round-setup-ship-insufficient";
        return amount == 0 || _ships.GetBalance(session) is { } balance && balance >= amount;
    }

    /// <summary>Rejects unavailable funds before loading any ship grids.</summary>
    private bool CanAffordCrew(Lobby lobby, out string error)
    {
        error = "wg-round-setup-payment-waiting";
        foreach (var (user, member) in lobby.Members)
        {
            if (member.Contribution == 0 && user != lobby.Host)
                continue;
            if (!_players.TryGetSessionById(user, out var player) || !ValidatePaymentFunds(lobby, player, out error))
                return false;
        }
        return true;
    }

    private bool AllPaymentsApproved(Lobby lobby)
    {
        if (lobby.Started || !WGRoundSetupPolicy.IsShip(lobby.Scenario))
            return true;
        if (lobby.HangarLicence != null)
            return _players.TryGetSessionById(lobby.Host, out var hangarHost) && CanEnter(hangarHost) &&
                   HasShipPurchaseConsent(lobby, hangarHost) && CanSpawnRole(lobby, hangarHost);
        return VesselPrice(lobby) > 0 && lobby.Members.Values.Sum(member => (long) member.Contribution) == VesselPrice(lobby) &&
            lobby.Members.Where(pair => pair.Key == lobby.Host || pair.Value.Contribution > 0).All(pair =>
                _players.TryGetSessionById(pair.Key, out var player) && CanEnter(player) &&
                HasShipPurchaseConsent(lobby, player) && CanSpawnRole(lobby, player));
    }

    private WGRoundSetupPayment? PaymentFor(Lobby lobby, NetUserId user) => lobby.Started || !WGRoundSetupPolicy.IsShip(lobby.Scenario) || lobby.HangarLicence != null
        ? null : new WGRoundSetupPayment(lobby.PaymentRevision, VesselPrice(lobby), lobby.Members[user].Contribution,
            lobby.Members.Values.Sum(member => (long) member.Contribution), AllPaymentsApproved(lobby));

    /// <summary>Revalidates connected contributors and their original character immediately before payment.</summary>
    private bool ChargeCrew(Lobby lobby, ICommonSession host, out string error)
    {
        error = "wg-round-setup-payment-waiting";
        if (!AllPaymentsApproved(lobby))
            return false;
        if (lobby.HangarLicence != null)
            return ChargeHangar(lobby, host, out error);
        var contributions = lobby.Members.Where(pair => pair.Value.Contribution > 0).Select(pair =>
            new WGShipScenarioSystem.Contribution(_players.GetSessionById(pair.Key), pair.Value.PurchaseCharacterSlot!.Value,
                pair.Value.Contribution)).ToArray();
        return _ships.TryCharge(lobby.Id, host, contributions, out error);
    }
}
