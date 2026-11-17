#nullable enable
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Ships;
using Content.Shared._WG.ShipCommand;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>An unrestricted grid jumps past the FTL mass limit, and the command drive reaches past the sector.</summary>
[TestFixture]
public sealed class WGFtlUnrestrictedTest
{
    private static readonly ResPath Hull = new("/SharedMaps/_WG/OldVessels/balor.yml");

    [Test]
    public async Task MassLimitDoesNotHoldAnUnrestrictedGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var shuttles = entMan.System<ShuttleSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var oldLimit = cfg.GetCVar(CCVars.FTLMassLimit);

        await server.WaitPost(() => cfg.SetCVar(CCVars.FTLMassLimit, 1f));
        await server.WaitAssertion(() =>
        {
            var massReason = loc.GetString("shuttle-console-mass");
            maps.CreateMap(out var mapId);
            Assert.That(loader.TryLoadGrid(mapId, Hull, out var grid), $"Couldn't load {Hull}.");
            var uid = grid!.Value.Owner;

            Assert.That(shuttles.CanFTL(uid, out var reason), Is.False, "A grid over the mass limit could jump.");
            Assert.That(reason, Is.EqualTo(massReason));

            entMan.AddComponent<WGFtlUnrestrictedComponent>(uid);
            var can = shuttles.CanFTL(uid, out reason);
            Assert.That(can || reason != massReason, "The mass limit still held an unrestricted grid.");
            Assert.That(shuttles.WgIgnoresFtlProximity(uid) && shuttles.WgIgnoresFtlExclusionZones(uid),
                "The component's defaults should lift the proximity and exclusion checks.");

            maps.DeleteMap(mapId);
        });
        await server.WaitPost(() => cfg.SetCVar(CCVars.FTLMassLimit, oldLimit));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CommandDriveReachesPastTheSector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>("WGFtlDriveUnrestricted");
            Assert.That(proto.TryGetComponent<FTLDriveComponent>(out var drive, factory), "The command drive has no FTLDrive.");
            Assert.That(drive!.Range, Is.GreaterThanOrEqualTo(60000f), "The command drive doesn't reach across the 30 km sector.");
            Assert.That(drive.MassAffectedDrive, Is.False, "The command drive's range still shrinks with mass.");
        });
        await pair.CleanReturnAsync();
    }
}
