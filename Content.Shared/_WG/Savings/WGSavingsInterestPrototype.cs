using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Savings;

/// <summary>
/// How savings grow: a share of the balance, paid at round end to each account that played the round and survived it.
/// </summary>
[Prototype("wgSavingsInterest")]
public sealed partial class WGSavingsInterestPrototype : IPrototype
{
    /// <summary>The one the game uses.</summary>
    public static readonly ProtoId<WGSavingsInterestPrototype> Default = "Default";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Share of the balance paid each round, 0.02 for 2%.</summary>
    [DataField(required: true)]
    public double Rate;

    /// <summary>Interest is earned on at most this much of the balance, which caps one round's payout.</summary>
    [DataField(required: true)]
    public long BalanceCap;

    /// <summary>How long the account must have been in the round, from its first spawn, to earn interest.</summary>
    [DataField]
    public TimeSpan MinimumTime = TimeSpan.FromMinutes(30);

    /// <summary>The most savings can hold. Neither ATM deposits nor interest take them past it.</summary>
    [DataField(required: true)]
    public long MaxBalance;

    /// <summary>What a balance earns in one round, rounded down and stopping at <see cref="MaxBalance"/>.</summary>
    public long InterestOn(long balance)
    {
        if (balance <= 0 || Rate <= 0)
            return 0;

        var earned = (long) Math.Floor(Math.Min(balance, BalanceCap) * Rate);
        return Math.Clamp(earned, 0, Math.Max(0, MaxBalance - balance));
    }

    /// <summary>How much more savings can take before <see cref="MaxBalance"/>.</summary>
    public long Room(long balance)
    {
        return Math.Max(0, MaxBalance - balance);
    }
}
