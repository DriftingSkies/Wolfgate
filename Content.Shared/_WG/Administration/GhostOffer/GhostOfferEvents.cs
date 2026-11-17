using Robust.Shared.Serialization;

namespace Content.Shared._WG.Administration.GhostOffer;

/// <summary>
/// Sent to every ghost when an admin offers them a ghost role, so they can sign up from a prompt.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOfferEvent : EntityEventArgs
{
    public int OfferId;
    public string Name;
    public string Description;
    public string Rules;

    /// <summary>
    /// Signing up enters a draw; otherwise the first ghost to sign up takes the role.
    /// </summary>
    public bool Raffled;

    public GhostOfferEvent(int offerId, string name, string description, string rules, bool raffled)
    {
        OfferId = offerId;
        Name = name;
        Description = description;
        Rules = rules;
        Raffled = raffled;
    }
}

/// <summary>
/// A ghost signs up for an offer.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOfferSignUpEvent : EntityEventArgs
{
    public int OfferId;

    public GhostOfferSignUpEvent(int offerId)
    {
        OfferId = offerId;
    }
}

/// <summary>
/// A ghost asks to follow the offered entity.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOfferFollowEvent : EntityEventArgs
{
    public int OfferId;

    public GhostOfferFollowEvent(int offerId)
    {
        OfferId = offerId;
    }
}

/// <summary>
/// Answer to a sign-up, for the ghost who sent it.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOfferSignUpResultEvent : EntityEventArgs
{
    public int OfferId;
    public string Message;
    public bool IsError;

    public GhostOfferSignUpResultEvent(int offerId, string message, bool isError)
    {
        OfferId = offerId;
        Message = message;
        IsError = isError;
    }
}

/// <summary>
/// The offered role was taken or is gone; clients close its prompt.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOfferClosedEvent : EntityEventArgs
{
    public int OfferId;

    public GhostOfferClosedEvent(int offerId)
    {
        OfferId = offerId;
    }
}
