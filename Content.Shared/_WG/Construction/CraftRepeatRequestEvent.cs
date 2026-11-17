using Robust.Shared.Serialization;

namespace Content.Shared._WG.Construction;

/// <summary>
/// Client asks the server to craft an item recipe several times in a row.
/// </summary>
[Serializable, NetSerializable]
public sealed class CraftRepeatRequestEvent : EntityEventArgs
{
    /// <summary>
    /// Most crafts one request can run; the "All" button asks for this.
    /// </summary>
    public const int MaxCount = 100;

    public readonly string PrototypeName;
    public readonly int Count;

    public CraftRepeatRequestEvent(string prototypeName, int count)
    {
        PrototypeName = prototypeName;
        Count = count;
    }
}
