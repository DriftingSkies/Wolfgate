using Robust.Shared.Utility;

namespace Content.Client._WG.PdaUi;

/// <summary>
/// Unified PDA program icons, keyed by the program's localisation id.
/// The set is rendered from the same icon font /tg/station's TGUI uses; programs without an
/// entry fall back to the icon on their <see cref="Content.Shared.CartridgeLoader.CartridgeComponent"/>.
/// </summary>
public static class PdaProgramIcons
{
    private static readonly ResPath IconPath = new("/Textures/_WG/PdaUi/program_icons.rsi");

    private static readonly Dictionary<string, SpriteSpecifier> Icons = new()
    {
        ["notekeeper-program-name"] = Rsi("notes"),
        ["news-read-program-name"] = Rsi("news"),
        ["crew-manifest-program-name"] = Rsi("crew"),
        ["net-probe-program-name"] = Rsi("netprobe"),
        ["log-probe-program-name"] = Rsi("logs"),
        ["wanted-list-program-name"] = Rsi("wanted"),
        ["med-tek-program-name"] = Rsi("medical"),
        ["astro-nav-program-name"] = Rsi("nav"),
        ["nano-chat-program-name"] = Rsi("chat"),
        ["wg-bench-program-name"] = Rsi("bench"),
        ["wg-gamehub-program-name"] = Rsi("gamehub"),
        ["wg-art-app-program-name"] = Rsi("art"),
        ["wg-contract-program-name"] = Rsi("contracts"),
        ["wg-market-program-name"] = Rsi("market"),
        ["wg-lattice-status-program-name"] = Rsi("lattice"),
        ["wg-judgeline-program-name"] = Rsi("judge"),
        ["mail-metrics-program-name"] = Rsi("mail"),
        ["ledger-program-name"] = Rsi("ledger"),
        ["appraisal-program-name"] = Rsi("appraisal"),
        ["bounty-contracts-program-name"] = Rsi("bounty"),
        ["pda-store-program-name"] = Rsi("store"),
    };

    /// <summary>Icon for the given program loc id, or null to use the cartridge's own icon.</summary>
    public static SpriteSpecifier? Get(string programName)
    {
        return Icons.GetValueOrDefault(programName);
    }

    private static SpriteSpecifier.Rsi Rsi(string state)
    {
        return new SpriteSpecifier.Rsi(IconPath, state);
    }
}
