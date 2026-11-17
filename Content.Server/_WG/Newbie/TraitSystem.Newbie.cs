using Content.Server._WG.Newbie;

namespace Content.Server.Traits;

public sealed partial class TraitSystem
{
    [Dependency] private WGNewbieTagSystem _wgNewbie = default!;
}
