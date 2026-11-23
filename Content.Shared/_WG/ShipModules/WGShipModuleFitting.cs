using System.Globalization;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// A drydock order to fit a module into a slot, written as a refit id so it travels the console, terminal and booking
/// paths that refits take.
/// </summary>
public static class WGShipModuleFitting
{
    private const string Prefix = "wg-module:";

    /// <summary>The refit id that fits a module into a slot.</summary>
    public static string Key(int slot, string module)
    {
        return Prefix + slot.ToString(CultureInfo.InvariantCulture) + ":" + module;
    }

    /// <summary>Reads a fitting order out of a refit id; false for an ordinary refit.</summary>
    public static bool TryParse(string? key, out int slot, out string module)
    {
        slot = -1;
        module = string.Empty;
        if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var rest = key.AsSpan(Prefix.Length);
        var colon = rest.IndexOf(':');
        if (colon <= 0 || !int.TryParse(rest[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out slot))
            return false;

        module = rest[(colon + 1)..].ToString();
        return module.Length > 0;
    }
}
