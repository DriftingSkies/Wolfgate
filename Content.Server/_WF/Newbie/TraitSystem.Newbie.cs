using Content.Server._WF.Newbie;

namespace Content.Server.Traits;

public sealed partial class TraitSystem
{
    [Dependency] private WFNewbieTagSystem _wfNewbie = default!;
}
