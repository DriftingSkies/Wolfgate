using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared._WF.Contracts;
using Robust.Shared.Utility;

namespace Content.Server._WF.Contracts;

/// <summary>
/// Standing is kept between rounds per character. It is loaded when a character spawns, and the first time it is
/// loaded in a round it drifts toward neutral by each contractor's <see cref="WFContractorPrototype.Drift"/>: favour
/// fades without work, grudges fade without trouble. Every change is saved as it happens.
/// </summary>
public sealed partial class WFContractSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>Characters whose stored standing has been read this round.</summary>
    private readonly HashSet<WFContractHolder> _loaded = new();

    /// <summary>Saves run one after another, so two changes to a new row cannot both insert it.</summary>
    private Task _saves = Task.CompletedTask;

    private void InitializePersistence()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        _ = LoadStanding(new WFContractHolder(ev.Player.UserId, ev.Profile.Name));
    }

    /// <summary>
    /// Reads a character's stored standing, drifts it once for this round, and adds any change made before it loaded.
    /// Does nothing if it has already been read this round.
    /// </summary>
    public async Task LoadStanding(WFContractHolder holder)
    {
        if (!_loaded.Add(holder))
            return;

        List<WFStoredContractStanding> stored;
        try
        {
            stored = await _db.GetContractStandingAsync(holder.User, holder.Character);
        }
        catch (Exception e)
        {
            Log.Error($"Could not load contract standing for {holder.Character} ({holder.User}): {e}");
            return;
        }

        var round = _ticker.RoundId;
        var standing = _standing.GetOrNew(holder);
        foreach (var row in stored)
        {
            var points = row.Points;
            if (row.LastRound != round && _proto.TryIndex<WFContractorPrototype>(row.Contractor, out var contractor))
                points = Drifted(points, contractor.Drift);

            // Changes made this round before the load are on top of what was stored.
            standing[row.Contractor] = Math.Clamp(points + standing.GetValueOrDefault(row.Contractor), MinStanding, MaxStanding);
        }

        foreach (var contractor in standing.Keys.ToList())
        {
            SaveStanding(holder, contractor);
        }

        _dirty = true;
    }

    /// <summary>Standing moved toward neutral by up to <paramref name="drift"/>.</summary>
    public static int Drifted(int points, int drift) =>
        points > 0 ? Math.Max(0, points - drift) : Math.Min(0, points + drift);

    /// <summary>Stores a character's standing with one contractor, once their stored standing has been read.</summary>
    private void SaveStanding(WFContractHolder holder, string contractor)
    {
        if (!_loaded.Contains(holder))
            return;

        var points = Standing(holder, contractor);
        var round = _ticker.RoundId;
        var previous = _saves;
        _saves = Save();

        async Task Save()
        {
            await previous;
            try
            {
                await _db.SetContractStandingAsync(holder.User, holder.Character, contractor, points, round);
            }
            catch (Exception e)
            {
                Log.Error($"Could not save contract standing for {holder.Character} ({holder.User}) with {contractor}: {e}");
            }
        }
    }
}
