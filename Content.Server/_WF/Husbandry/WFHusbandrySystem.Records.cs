using System.Linq;
using Content.Server._WF.Husbandry.Components;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Animals.Components;
using Content.Server.Animals.Systems;
using Content.Shared.Animals;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Husbandry;

/// <summary>Writes an outpost's penned livestock into its save and brings it back at the feeders once the save lands.</summary>
public sealed partial class WFHusbandrySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private EggLayerSystem _eggs = default!;
    [Dependency] private UdderSystem _udders = default!;
    [Dependency] private WoolySystem _wool = default!;

    private void InitializeRecords()
    {
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostSavingEvent>(OnSaving);
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostSavedEvent>(OnSaved);
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostSettledEvent>(OnSettled);
    }

    private void OnSaving(Entity<WFOutpostComponent> outpost, ref WFOutpostSavingEvent args)
    {
        RemComp<WFLivestockRecordsComponent>(outpost);
        if (!TryComp<MapGridComponent>(outpost, out var grid))
            return;

        var now = _timing.CurTime;
        var records = new List<WFLivestockRecord>();
        foreach (var pen in GetPens((outpost, grid)))
        {
            foreach (var animal in pen.Livestock)
            {
                if (MetaData(animal).EntityPrototype is not { } proto)
                    continue;

                var livestock = MakeLivestock(animal);
                var name = Name(animal);
                records.Add(new WFLivestockRecord
                {
                    Prototype = proto.ID,
                    Name = name == proto.Name ? string.Empty : name,
                    Age = GetAge(livestock),
                    Feeder = pen.FeederTiles[0],
                    Price = _pricing.GetPrice(animal),
                    Eggs = TryComp<EggLayerComponent>(animal, out var eggs) ? Left(eggs.NextGrowth, now) : null,
                    Milk = TryComp<UdderComponent>(animal, out var udder) ? Left(udder.NextGrowth, now) : null,
                    Wool = TryComp<WoolyComponent>(animal, out var wool) ? Left(wool.NextGrowth, now) : null,
                });
            }
        }

        if (records.Count > 0)
            EnsureComp<WFLivestockRecordsComponent>(outpost).Records = records;
    }

    /// <summary>The records only live in the save; the outpost itself keeps its animals.</summary>
    private void OnSaved(Entity<WFOutpostComponent> outpost, ref WFOutpostSavedEvent args)
    {
        RemComp<WFLivestockRecordsComponent>(outpost);
    }

    /// <summary>
    /// Spawns a landed save's livestock in the pen of the feeder each stood by, or in any pen with room, or at its feeder's
    /// tile when no pen has room.
    /// </summary>
    private void OnSettled(Entity<WFOutpostComponent> outpost, ref WFOutpostSettledEvent args)
    {
        if (!TryComp<WFLivestockRecordsComponent>(outpost, out var records))
            return;

        var list = records.Records;
        RemComp(outpost, records);
        if (!TryComp<MapGridComponent>(outpost, out var grid))
            return;

        var pens = GetPens((outpost, grid));
        var now = _timing.CurTime;
        foreach (var record in list)
        {
            if (!_proto.HasIndex(record.Prototype))
                continue;

            var pen = pens.FirstOrDefault(p => p.Livestock.Count < p.Capacity && p.FeederTiles.Contains(record.Feeder))
                      ?? pens.FirstOrDefault(p => p.Livestock.Count < p.Capacity);
            var tile = pen is { Open.Count: > 0 } ? _random.Pick(pen.Open) : record.Feeder;
            var animal = Spawn(record.Prototype, _maps.GridTileToLocal(outpost, grid, tile));
            pen?.Livestock.Add(animal);

            if (record.Name.Length > 0)
                _metaData.SetEntityName(animal, record.Name);

            var livestock = MakeLivestock(animal);
            livestock.Comp.Age = record.Age;
            livestock.Comp.Since = now;
            if (record.Eggs is { } eggs && TryComp<EggLayerComponent>(animal, out var layer))
                _eggs.WfSetNextGrowth(layer, now + eggs);
            if (record.Milk is { } milk && TryComp<UdderComponent>(animal, out var udder))
                _udders.WfSetNextGrowth(udder, now + milk);
            if (record.Wool is { } wool && TryComp<WoolyComponent>(animal, out var wooly))
                _wool.WfSetNextGrowth(wooly, now + wool);
        }
    }

    private static TimeSpan Left(TimeSpan next, TimeSpan now)
    {
        return next > now ? next - now : TimeSpan.Zero;
    }
}
