using Robust.Shared.Serialization;

namespace Content.Shared._WF.Census;

/// <summary>How a census question is answered.</summary>
[Serializable, NetSerializable]
public enum CensusQuestionKind : byte
{
    Single,
    Multi,
    Text,
}

/// <summary>One question of a census, with every default filled in.</summary>
[Serializable, NetSerializable]
public sealed class CensusQuestion
{
    public string Id = string.Empty;
    public CensusQuestionKind Kind;
    public string Prompt = string.Empty;

    /// <summary>Single and multi only.</summary>
    public List<string> Options = new();

    public bool Required;

    /// <summary>Multi only: fewest and most options the player may pick.</summary>
    public int Min;
    public int Max;

    /// <summary>Text only.</summary>
    public int MaxLength = CensusRules.DefaultTextLength;
    public bool Multiline;
}

/// <summary>An open census as one player sees it.</summary>
[Serializable, NetSerializable]
public sealed class CensusEntry
{
    public int Id;
    public string Title = string.Empty;
    public string Description = string.Empty;

    /// <summary>When it closes by itself, in UTC, or null.</summary>
    public DateTime? ClosesAt;

    /// <summary>Whether this player has answered it. An answered census comes without its questions.</summary>
    public bool Answered;

    public List<CensusQuestion> Questions = new();
}

/// <summary>The open censuses, sent whenever they or the player's answers change.</summary>
[Serializable, NetSerializable]
public sealed class CensusListEvent : EntityEventArgs
{
    public List<CensusEntry> Censuses = new();

    /// <summary>Open the census window: the player is in the lobby with a census not yet shown to them.</summary>
    public bool Prompt;
}

/// <summary>Asks the server for the open censuses again.</summary>
[Serializable, NetSerializable]
public sealed class CensusListRequestEvent : EntityEventArgs;

/// <summary>A player's answers to a census. A skipped question is left out.</summary>
[Serializable, NetSerializable]
public sealed class CensusSubmitEvent : EntityEventArgs
{
    public int CensusId;

    /// <summary>Picked option indexes of single and multi questions, by question id.</summary>
    public Dictionary<string, List<int>> Choices = new();

    /// <summary>Text answers by question id.</summary>
    public Dictionary<string, string> Texts = new();
}

/// <summary>The server's answer to a <see cref="CensusSubmitEvent"/>.</summary>
[Serializable, NetSerializable]
public sealed class CensusResultEvent : EntityEventArgs
{
    public int CensusId;

    /// <summary>Fluent id of why it was refused, or null when it was stored.</summary>
    public string? Error;

    /// <summary>The prompt of the question the refusal is about, or empty.</summary>
    public string Question = string.Empty;

    /// <summary>A number the refusal names: options to pick, or seconds to wait.</summary>
    public int Count;
}
