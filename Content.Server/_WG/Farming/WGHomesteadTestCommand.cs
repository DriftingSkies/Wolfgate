using System.Numerics;
using Content.Server._WG.Bonfires;
using Content.Server._WG.Planets;
using Content.Server.Administration;
using Content.Server.Parallax;
using Content.Shared._WG.Planets;
using Content.Shared.Administration;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Server.Power.Components;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Farming;

/// <summary><c>wghomesteadtest [section] [surface]</c>: builds a test ground with every homestead feature on a fresh world.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WGHomesteadTestCommand : LocalizedEntityCommands
{
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private WGBonfireFuelSystem _fire = default!;
    [Dependency] private WGPlanetNetworkSystem _networks = default!;
    [Dependency] private WGHomesteadTestSystem _homestead = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string DefaultSurface = "WGSurfaceAsclepiu";

    /// <summary>Patch origin near the world's centre, which POI generation keeps clear.</summary>
    private static readonly Vector2 Origin = new(-24, -14);

    private static readonly string[] Sections = { "all", "kitchen", "farm", "machines", "fishing", "hunting" };

    public override string Command => "wghomesteadtest";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Sections, Loc.GetString("cmd-wghomesteadtest-hint")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<WGPlanetSurfacePrototype>(), Loc.GetString("cmd-wghomesteadtest-surface-hint")),
            _ => CompletionResult.Empty,
        };
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-wghomesteadtest-no-body"));
            return;
        }

        var section = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
        if (Array.IndexOf(Sections, section) < 0)
        {
            shell.WriteError(Loc.GetString("cmd-wghomesteadtest-bad-section", ("sections", string.Join(", ", Sections))));
            return;
        }

        var surfaceId = args.Length > 1 ? args[1] : DefaultSurface;
        if (!_proto.TryIndex<WGPlanetSurfacePrototype>(surfaceId, out var surface))
        {
            shell.WriteError(Loc.GetString("cmd-wghomesteadtest-bad-surface", ("surface", surfaceId)));
            return;
        }

        if (BuildGround(surface) is not { } grid)
        {
            shell.WriteError(Loc.GetString("cmd-wghomesteadtest-no-world"));
            return;
        }

        // The world places its sites, roads and creatures over the next while.
        shell.WriteLine(Loc.GetString("cmd-wghomesteadtest-waiting", ("surface", surfaceId)));
        var patch = new Box2(Origin, Origin + new Vector2(48, 28));
        _homestead.Queue(grid, patch, () =>
        {
            var all = section == "all";
            if (all || section == "kitchen")
                Kitchen(grid, new Vector2(1, 1));
            if (all || section == "farm")
                Farm(grid, new Vector2(12, 1));
            if (all || section == "machines")
                Machines(grid, new Vector2(1, 12));
            if (all || section == "fishing")
                Fishing(grid, new Vector2(24, 1));
            // Below the machine row, which runs most of the way across.
            if (all || section == "hunting")
                Hunting(grid, new Vector2(32, 18));

            if (!EntityManager.Deleted(player))
                _transform.SetCoordinates(player, new EntityCoordinates(grid, Origin + new Vector2(10.5f, 9.5f)));
            shell.WriteLine(Loc.GetString("cmd-wghomesteadtest-done", ("section", section), ("surface", surfaceId)));
        });
    }

    /// <summary>Builds a standalone world from the surface and returns its ground map.</summary>
    private EntityUid? BuildGround(WGPlanetSurfacePrototype surface)
    {
        if (_networks.BuildNetwork(surface, Vector2.Zero, Loc.GetString("cmd-wghomesteadtest-world-name"), null) is not { } network
            || !EntityManager.TryGetComponent<WGPlanetNetworkComponent>(network, out var comp))
            return null;

        var ground = comp.GroundMap;
        var tiles = new List<(Vector2i Index, Tile Tile)>();
        _biomes.ReserveTiles(ground, new Box2(Origin, Origin + new Vector2(48, 28)), tiles);
        if (EntityManager.TryGetComponent<MapGridComponent>(ground, out var grid))
            _maps.SetTiles(ground, grid, tiles);
        return ground;
    }

    private EntityUid Put(EntityUid grid, Vector2 at, string id)
    {
        var uid = EntityManager.SpawnEntity(id, new EntityCoordinates(grid, Origin + at + new Vector2(0.5f, 0.5f)));
        if (EntityManager.HasComponent<ApcPowerReceiverComponent>(uid))
            _power.SetNeedsPower(uid, false);
        // A wood-burning hearth comes stocked and lit, as a powered one comes on.
        if (EntityManager.TryGetComponent<WGBonfireFuelComponent>(uid, out var fire))
        {
            fire.Fuel = fire.MaxFuel;
            _fire.SetLit((uid, fire), true);
        }
        return uid;
    }

    /// <summary>Lays a row of things, one a tile, from <paramref name="at"/> to the right.</summary>
    private void Row(EntityUid grid, Vector2 at, params string[] ids)
    {
        for (var i = 0; i < ids.Length; i++)
            Put(grid, at + new Vector2(i, 0), ids[i]);
    }

    private void Water(EntityUid uid, string solution, float amount)
    {
        if (_solutions.TryGetSolution(uid, solution, out var soln))
            _solutions.TryAddReagent(soln.Value, "Water", FixedPoint2.New(amount));
    }

    private void Kitchen(EntityUid grid, Vector2 at)
    {
        Row(grid, at, "WGHearthStove", "KitchenElectricRange", "KitchenMicrowave", "WGPrepBoard", "WGPrepBoard");
        Row(grid, at + new Vector2(0, 2), "WGCookPan", "WGCookPan", "KitchenKnife", "WGCookBowl", "WGCookBowl", "WGCookBowl");
        Water(Put(grid, at + new Vector2(6, 2), "WGCookPot"), "pot", 200);
        Water(Put(grid, at + new Vector2(7, 2), "WGCookPotCopper"), "pot", 200);
        Row(grid, at + new Vector2(0, 3), "FoodPotato", "FoodPotato", "FoodPotato", "FoodOnion", "FoodOnion", "FoodMeat", "FoodMeat", "FoodCabbage");
        Row(grid, at + new Vector2(0, 4), "FoodEgg", "FoodEgg", "FoodEgg", "FoodCheeseSlice", "FoodCheeseSlice", "FoodCakeBatter", "FoodDough", "FoodDoughSlice");
        Row(grid, at + new Vector2(0, 5), "WGFoodRaspberry", "WGFoodRaspberry", "WGFoodRaspberry", "WGFoodStrawberry", "WGFoodStrawberry", "WGFoodStrawberry", "WheatBushel", "FoodMushroom");
    }

    private void Farm(EntityUid grid, Vector2 at)
    {
        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
                Put(grid, at + new Vector2(x, y), "HydroponicsTrayEmpty");
        }

        var sprinkler = Put(grid, at + new Vector2(1, 3), "WGSprinklerQuality");
        Water(sprinkler, "tank", 200);
        Row(grid, at + new Vector2(4, 0), "WGGrowLamp", "WGRainCatcher", "WGMoistureVaporator", "WGSnowMelter", "WGFarmScarecrow");
        Row(grid, at + new Vector2(4, 1), "WGCucumberSeeds", "WGStrawberrySeeds", "WGTurnipSeeds", "WGSunflowerSeeds", "WheatSeeds", "TomatoSeeds", "PotatoSeeds");
        Row(grid, at + new Vector2(4, 2), "WGFertilizerBasic", "WGFertilizerQuality", "WGSpeedGro", "WGRetainingSoil", "Bucket");
        Water(Put(grid, at + new Vector2(9, 2), "Bucket"), "bucket", 100);
        Row(grid, at + new Vector2(4, 4), "MobChicken", "MobCow", "MobGoat");
        Put(grid, at + new Vector2(3, 6), "WGVendingMachineHomestead");
    }

    private void Machines(EntityUid grid, Vector2 at)
    {
        // Each machine with something to put in it on the tile above.
        (string Machine, string[] Inputs)[] machines =
        {
            ("WGFarmKeg", new[] { "WGFoodRaspberry", "WGGlassBottle" }),
            ("WGFarmPreservesJar", new[] { "FoodTomato" }),
            ("WGFarmCheesePress", new[] { "DrinkMilkCarton" }),
            ("WGFarmMayoMachine", new[] { "FoodEgg" }),
            ("WGFarmLoom", new[] { "MaterialCotton1" }),
            ("WGFarmMill", new[] { "WheatBushel" }),
            ("WGFarmComposter", new[] { "FoodPotato" }),
            ("WGFarmFertilizerBarrel", new[] { "WGCompost" }),
            ("WGFarmNutrientMixer", new[] { "WGBone" }),
            ("WGFarmCask", new[] { "DrinkWineBottleFull" }),
            ("WGFarmGlassKiln", new[] { "SheetGlass1" }),
            ("WGFarmSeedMaker", new[] { "WGFoodCucumber" }),
            ("WGHuntSmokingRack", new[] { "WGMeatSaiga" }),
            ("WGHuntTanningRack", new[] { "WGPeltWolf" }),
            ("WGFarmDigester", new[] { "FoodPotato" }),
            ("WGFarmOreWasher", new[] { "SteelOre1" }),
            ("WGFarmGeodeCracker", new[] { "WGGeode" }),
            ("WGFarmBeeHive", Array.Empty<string>()),
            ("WGFarmMushroomLog", Array.Empty<string>()),
            ("WGFarmComputeRack", Array.Empty<string>()),
            ("WGFarmPackingTable", new[] { "WGFoodHoneyCake", "WGFoodHoneyCake" }),
            ("WGFarmOrderBoard", Array.Empty<string>()),
        };

        for (var i = 0; i < machines.Length; i++)
        {
            var spot = at + new Vector2(i * 2 % 44, i * 2 / 44 * 4);
            Put(grid, spot, machines[i].Machine);
            for (var j = 0; j < machines[i].Inputs.Length; j++)
                Put(grid, spot + new Vector2(j % 2, 1), machines[i].Inputs[j]);
        }

        // Two crops beside the hive so it runs.
        Row(grid, at + new Vector2(34, 2), "HydroponicsTrayEmpty", "HydroponicsTrayEmpty");
    }

    private void Fishing(EntityUid grid, Vector2 at)
    {
        Put(grid, at, "WGFishPond");
        Row(grid, at + new Vector2(0, 2), "WGFishingRod", "WGFishingRod", "WGBaitWorm", "WGBaitWorm", "WGBaitDoughball");
        Row(grid, at + new Vector2(0, 4), "WGForageBerryBush", "WGForageStrawberries", "WGForageRaspberries", "WGForageBlackberries", "WGForageOnions", "WGForagePoppies");
        Row(grid, at + new Vector2(0, 5), "WGForageSunflowers", "WGForageTea", "WGForageCoffee", "WGForageNuts", "WGForageMushrooms", "WGForageWorms");
    }

    private void Hunting(EntityUid grid, Vector2 at)
    {
        // Live prey to chase, and a carcass of every animal to butcher.
        Row(grid, at, "WGGameSaiga", "WGGameGoat", "WGGameRaccoon");
        string[] carcasses =
        {
            "WGGameSaiga", "WGGameSaigaBuck", "WGGameGoat", "WGGameBadger", "WGGameRaccoon", "WGGameMole",
            "WGGameWolf", "WGGameDirebear", "WGGameBobcat", "WGGameHyena", "WGGameBigRat", "WGGameMossback",
        };
        for (var i = 0; i < carcasses.Length; i++)
        {
            var body = Put(grid, at + new Vector2(i % 6 * 2 - 6, 3 + i / 6 * 2), carcasses[i]);
            _mobs.ChangeMobState(body, MobState.Dead);
        }

        Row(grid, at + new Vector2(-6, 8), "KitchenKnife", "KitchenKnife", "WGHuntSmokingRack", "WGHuntTanningRack");
    }
}
