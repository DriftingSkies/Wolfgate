namespace Content.Server._WG.CustomMarkings;

/// <summary>Art as the database keeps it.</summary>
/// <param name="Png">Every frame on one PNG sheet, as <c>CustomMarkingArt.ToPng</c> lays them out.</param>
/// <param name="FrameTimes">Packed frame times, as <c>CustomMarkingRules.PackFrameTimes</c> makes; null for a still marking.</param>
/// <param name="Erase">The mask of erased body pixels; null for none.</param>
/// <param name="Glow">The mask of glowing pixels for each frame, as <c>CustomMarkingArt.Glow</c> holds it; null for none.</param>
public sealed record CustomMarkingStoredArt(byte[] Png, byte[]? FrameTimes, byte[]? Erase, byte[]? Glow = null);
