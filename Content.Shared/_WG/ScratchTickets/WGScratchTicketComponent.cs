using Robust.Shared.Audio;
using Robust.Shared.Utility;

namespace Content.Shared._WG.ScratchTickets;

/// <summary>A scratch ticket: symbols hidden under foil, and a match set by its format wins that symbol's prize.</summary>
[RegisterComponent]
public sealed partial class WGScratchTicketComponent : Component
{
    /// <summary>Every symbol the ticket can show. One with no prize only fills cells.</summary>
    [DataField(required: true)]
    public List<WGScratchSymbol> Symbols = new();

    /// <summary>Weight of a ticket that wins nothing, against the symbols' weights.</summary>
    [DataField]
    public float LoseWeight = 1f;

    /// <summary>How the cells are laid out and what wins.</summary>
    [DataField]
    public WGScratchFormat Format = WGScratchFormat.MatchThree;

    /// <summary>The symbol under each cell, row by row, rolled on the server at map init.</summary>
    [DataField]
    public List<int> Cells = new();

    /// <summary>What the ticket pays, known from the roll.</summary>
    [DataField]
    public int Prize;

    [DataField]
    public bool Scratched;

    [DataField]
    public SoundSpecifier? WinSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");

    /// <summary>The name printed across the top of the card.</summary>
    [DataField(required: true)]
    public LocId Title;

    /// <summary>The line of rules printed over the foil.</summary>
    [DataField(required: true)]
    public LocId Tagline;

    /// <summary>What is printed under a symbol that pays nothing.</summary>
    [DataField]
    public LocId Dud = "wg-scratch-ticket-card-dud";

    /// <summary>How many times their size the symbols are printed in the legend. Sprites with wide margins want more.</summary>
    [DataField]
    public int LegendIconScale = 2;

    /// <summary>How many times their size the symbols are printed under the foil.</summary>
    [DataField]
    public int CellIconScale = 3;

    /// <summary>The two colours of the card's sunburst.</summary>
    [DataField]
    public Color CardLight = Color.FromHex("#1F8062");

    [DataField]
    public Color CardDark = Color.FromHex("#155C46");

    /// <summary>The glow behind the title.</summary>
    [DataField]
    public Color CardGlow = Color.FromHex("#6FD9A4");

    [DataField]
    public Color Ribbon = Color.FromHex("#C4261C");
}

/// <summary>One symbol of a scratch ticket.</summary>
[DataDefinition]
public sealed partial class WGScratchSymbol
{
    [DataField(required: true)]
    public SpriteSpecifier Icon = SpriteSpecifier.Invalid;

    /// <summary>Thalers paid for a win on this symbol.</summary>
    [DataField]
    public int Prize;

    /// <summary>Weight of a ticket that wins on this symbol.</summary>
    [DataField]
    public float Weight;
}
