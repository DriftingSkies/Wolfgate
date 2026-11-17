using GasCanisterComponent = Content.Shared.Atmos.Piping.Unary.Components.GasCanisterComponent;

namespace Content.Server.Atmos.Piping.Unary.EntitySystems;

public sealed partial class GasCanisterSystem
{
    /// <summary>Sets the canister's UI state from its current pressures.</summary>
    public void WgRefreshUi(Entity<GasCanisterComponent> ent)
    {
        DirtyUI(ent.Owner, ent.Comp);
    }
}
