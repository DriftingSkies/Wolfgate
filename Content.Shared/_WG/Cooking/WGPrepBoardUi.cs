using Robust.Shared.Serialization;

namespace Content.Shared._WG.Cooking;

[Serializable, NetSerializable]
public enum WGPrepBoardUiKey : byte
{
    Key,
}

/// <summary>The prep board window: what lies on the board, and the dishes it could make.</summary>
[Serializable, NetSerializable]
public sealed class WGPrepBoardState(List<WGPrepItemView> items, List<WGPrepRecipeView> recipes) : BoundUserInterfaceState
{
    public readonly List<WGPrepItemView> Items = items;
    public readonly List<WGPrepRecipeView> Recipes = recipes;
}

/// <summary>An item on the board, with its cut options and the cuts each takes.</summary>
[Serializable, NetSerializable]
public sealed record WGPrepItemView(NetEntity Entity, string Name, string? Prototype, int Cuts, List<string> Options, List<int> OptionCuts);

/// <summary>A dish the board's contents are working toward: what is there and what is still missing.</summary>
[Serializable, NetSerializable]
public sealed record WGPrepRecipeView(string Name, string? Result, List<string> Have, List<string> Missing, bool Unbaked);

/// <summary>Puts the board's contents together into the first dish they make.</summary>
[Serializable, NetSerializable]
public sealed class WGPrepBoardAssembleMessage : BoundUserInterfaceMessage;

/// <summary>Takes one thing off the board into the hand.</summary>
[Serializable, NetSerializable]
public sealed class WGPrepBoardTakeMessage(NetEntity item) : BoundUserInterfaceMessage
{
    public readonly NetEntity Item = item;
}

/// <summary>A finished chop, scored 0 to 1.</summary>
[Serializable, NetSerializable]
public sealed class WGPrepBoardChopMessage(NetEntity item, float score, int option = 0) : BoundUserInterfaceMessage
{
    public readonly NetEntity Item = item;
    public readonly float Score = score;

    /// <summary>Which way of cutting it, of the item's <see cref="WGPrepItemView.Options"/>.</summary>
    public readonly int Option = option;
}
