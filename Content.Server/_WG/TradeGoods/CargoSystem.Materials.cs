using Content.Server._NF.Market.Systems;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private MarketSystem _wgMaterials = default!;
}
