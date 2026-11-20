using Content.Server._WG.Bonfires;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Lights a wood-burning hearth for a test, with all the fuel it holds.</summary>
public static class WGHearthFire
{
    public static void Light(IEntityManager entMan, EntityUid hearth)
    {
        var fire = entMan.GetComponent<WGBonfireFuelComponent>(hearth);
        fire.Fuel = fire.MaxFuel;
        entMan.System<WGBonfireFuelSystem>().SetLit((hearth, fire), true);
    }
}
