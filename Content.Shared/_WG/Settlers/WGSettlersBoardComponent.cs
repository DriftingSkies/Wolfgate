namespace Content.Shared._WG.Settlers;

/// <summary>A Settlers of Colossus board. It holds one game and the players sitting at it.</summary>
[RegisterComponent]
public sealed partial class WGSettlersBoardComponent : Component
{
    [ViewVariables]
    public SettlersGame? Game;

    /// <summary>The player in each seat.</summary>
    [ViewVariables]
    public EntityUid?[] Seats = new EntityUid?[SettlersRules.MaxPlayers];
}
