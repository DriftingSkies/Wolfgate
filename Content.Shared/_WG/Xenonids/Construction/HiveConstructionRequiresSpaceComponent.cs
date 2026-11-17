using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._WG.Xenonids.Construction;
/// <summary>
/// Indicates that the hive structure must have unobstructed adjacent tiles to be built
/// </summary>
[RegisterComponent]
public sealed partial class HiveConstructionRequiresSpaceComponent : Component
{
}
