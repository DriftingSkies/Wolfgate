using Robust.Shared.Configuration;

namespace Content.Shared._WG.MappingMode;

/// <summary>Limits on templates shared with a mapping session.</summary>
[CVarDefs]
public static class WGMappingTemplateCVars
{
    /// <summary>
    /// Size of a PNG a player may share with their session; replicated so the client refuses early. Clamped to
    /// <see cref="WGMappingUploadAssembler.MaxFileBytes"/>.
    /// </summary>
    public static readonly CVarDef<long> TemplateMaxBytes =
        CVarDef.Create("wg.mapping_mode.template_max_bytes", 8L * 1024 * 1024, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Seconds after a share starts before the same player may start another.</summary>
    public static readonly CVarDef<float> TemplateCooldown =
        CVarDef.Create("wg.mapping_mode.template_cooldown", 10f, CVar.SERVERONLY);
}
