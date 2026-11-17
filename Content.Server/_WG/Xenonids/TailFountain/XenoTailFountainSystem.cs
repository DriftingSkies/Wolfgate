using Content.Server.Atmos.EntitySystems;
using Content.Shared._WG.Xenonids.TailFountain;

namespace Content.Server._WG.Xenonids.TailFountain;

public sealed partial class XenoTailFountainSystem : SharedXenoTailFountainSystem
{
    [Dependency] private FlammableSystem _flammable = default!;

    protected override void Extinguish(EntityUid target)
    {
        _flammable.Extinguish(target);
    }
}
