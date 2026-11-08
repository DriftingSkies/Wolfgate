using System.Linq;
using Content.Shared.Preferences;

namespace Content.Server._WF.RoundSetup;

/// <summary>
/// Crew shares for a purchase that isn't a ship, such as an Outpost crew's outpost: each contributor's character is
/// charged with a receipt, then the receipts are kept once it arrives or refunded, through the same retry queue, if not.
/// </summary>
public sealed partial class WFShipScenarioSystem
{
    /// <summary>Receipts of charged shares by lobby, until kept or refunded.</summary>
    private readonly Dictionary<int, List<PendingRefund>> _shares = new();

    /// <summary>Refunds every lobby's charged shares, as the round ends before what they paid for arrived.</summary>
    private void RefundAllShares()
    {
        foreach (var lobby in _shares.Keys.ToList())
        {
            RefundShares(lobby);
        }
    }

    /// <summary>
    /// Checks every contributor, then withdraws each share from their approved character; any failure refunds what was
    /// taken. The shares must add up to <paramref name="price"/>.
    /// </summary>
    public bool TryChargeShares(int lobbyId, IReadOnlyCollection<Contribution> contributions, int price, out string reason)
    {
        reason = "wf-round-setup-payment-total-error";
        if (_shares.ContainsKey(lobbyId) || contributions.Count == 0 || contributions.Any(part => part.Amount <= 0) ||
            contributions.Select(part => part.Player.UserId).Distinct().Count() != contributions.Count ||
            contributions.Sum(part => (long) part.Amount) != price)
            return false;

        foreach (var part in contributions)
        {
            reason = "wf-round-setup-ship-bank-unavailable";
            var preferences = _preferences.GetPreferencesOrNull(part.Player.UserId);
            if (!_refundUserDb.IsLoadComplete(part.Player) || preferences?.SelectedCharacterIndex != part.Slot ||
                preferences.SelectedCharacter is not HumanoidCharacterProfile profile || profile.TraitPreferences.Contains("Ironman"))
                return false;
            reason = "wf-round-setup-payment-funds-error";
            if (profile.BankBalance < part.Amount)
                return false;
        }

        var receipts = new List<PendingRefund>();
        _shares[lobbyId] = receipts;
        foreach (var part in contributions)
        {
            var preferences = _preferences.GetPreferences(part.Player.UserId);
            if (preferences.SelectedCharacterIndex != part.Slot || preferences.SelectedCharacter is not HumanoidCharacterProfile profile ||
                !_bank.TryBankWithdraw(part.Player, preferences, profile, part.Amount, out _))
            {
                RefundShares(lobbyId);
                return false;
            }
            receipts.Add(new PendingRefund(part.Player.UserId, part.Slot, part.Amount));
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Refunds a lobby's charged shares, queueing any that can't be paid back yet.</summary>
    public void RefundShares(int lobbyId)
    {
        if (!_shares.Remove(lobbyId, out var receipts))
            return;

        foreach (var receipt in receipts)
        {
            if (!TryRefund(receipt))
            {
                _pendingRefunds.Add(receipt);
                Log.Warning($"Queued crew share refund of {receipt.Amount} for {receipt.User} slot {receipt.Slot} until preferences load.");
            }
        }
    }

    /// <summary>Keeps a lobby's charged shares: what they paid for arrived.</summary>
    public void KeepShares(int lobbyId)
    {
        _shares.Remove(lobbyId);
    }
}
