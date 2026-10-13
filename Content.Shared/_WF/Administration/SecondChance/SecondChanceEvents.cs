using Robust.Shared.Serialization;

namespace Content.Shared._WF.Administration.SecondChance;

/// <summary>
/// Sent to a player when an admin offers to put them back in their body, fully healed.
/// </summary>
[Serializable, NetSerializable]
public sealed class SecondChanceOfferEvent : EntityEventArgs
{
    public int OfferId;
    public string BodyName;

    public SecondChanceOfferEvent(int offerId, string bodyName)
    {
        OfferId = offerId;
        BodyName = bodyName;
    }
}

/// <summary>
/// The player's answer to a second chance offer.
/// </summary>
[Serializable, NetSerializable]
public sealed class SecondChanceAnswerEvent : EntityEventArgs
{
    public int OfferId;
    public bool Accepted;

    public SecondChanceAnswerEvent(int offerId, bool accepted)
    {
        OfferId = offerId;
        Accepted = accepted;
    }
}

/// <summary>
/// The offer is over; the client closes its prompt.
/// </summary>
[Serializable, NetSerializable]
public sealed class SecondChanceClosedEvent : EntityEventArgs
{
    public int OfferId;

    public SecondChanceClosedEvent(int offerId)
    {
        OfferId = offerId;
    }
}
