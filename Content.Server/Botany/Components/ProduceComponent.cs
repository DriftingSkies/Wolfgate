using Content.Server.Botany.Systems;
using Content.Shared.Botany.Components;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Botany.Components;

[RegisterComponent]
[Access(typeof(BotanySystem))]
public sealed partial class ProduceComponent : SharedProduceComponent
{
    [DataField("targetSolution")] public string SolutionName { get; set; } = "food";

    /// <summary>
    ///     Seed data used to create a <see cref="SeedComponent"/> when this produce has its seeds extracted.
    /// </summary>
    [DataField]
    public SeedData? Seed;

    /// <summary>
    ///     Seed data used to create a <see cref="SeedComponent"/> when this produce has its seeds extracted.
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<SeedPrototype>))]
    public string? SeedId;
}
