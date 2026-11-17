using Content.Shared._WG.NetTraffic;
using Content.Shared.CCVar;
using Content.Shared.Explosion.Components;
using Robust.Server.Replays;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Sends each explosion's overlay and sound to the players near it as one message. The server keeps its visuals entity
/// for its own bookkeeping but never sends it, so no client gets an entity, its every iteration, and audio entities.
/// </summary>
public sealed class WGNetExplosionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IServerReplayRecordingManager _replay = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGViewPointSystem _viewPoints = default!;

    /// <summary>Whether explosions go out as messages; read by the explosion system.</summary>
    public bool Enabled { get; private set; }

    private float _range;
    private float _persistence;
    private readonly List<(ICommonSession Session, MapCoordinates At)> _points = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, NetTrafficCVars.Explosions, value => Enabled = value, true);
        Subs.CVar(_config, NetTrafficCVars.ExplosionRange, value => _range = value, true);
        Subs.CVar(_config, CCVars.ExplosionPersistence, value => _persistence = value, true);
    }

    /// <summary>
    /// Sends an explosion: its overlay to players within range, the near sound to <paramref name="near"/> and the far
    /// sound to <paramref name="far"/>. Players hearing it are always sent the overlay too.
    /// </summary>
    public void Send(EntityUid visuals, MapCoordinates epicenter, EntityCoordinates soundAt,
        SoundSpecifier? nearSound, Filter? near, SoundSpecifier? farSound, Filter? far)
    {
        if (!TryComp<ExplosionVisualsComponent>(visuals, out var comp))
            return;

        var tiles = new Dictionary<NetEntity, Dictionary<int, List<Vector2i>>>();
        foreach (var (grid, data) in comp.Tiles)
        {
            tiles[GetNetEntity(grid)] = data;
        }

        WGExplosionEvent Make(SoundSpecifier? sound, bool isFar) => new()
        {
            Epicenter = comp.Epicenter,
            ExplosionType = comp.ExplosionType,
            Intensity = comp.Intensity,
            SpaceTiles = comp.SpaceTiles,
            Tiles = tiles,
            SpaceMatrix = comp.SpaceMatrix,
            SpaceTileSize = comp.SpaceTileSize,
            Persistence = _persistence,
            Sound = sound,
            FarSound = isFar,
            SoundAt = GetNetCoordinates(soundAt),
        };

        var told = new HashSet<ICommonSession>();
        if (nearSound != null && near != null)
            Raise(Make(nearSound, false), near.Recipients, told);
        if (farSound != null && far != null)
            Raise(Make(farSound, true), far.Recipients, told);

        // Bigger blasts are seen further off; each iteration is roughly a tile further out. Players watching through
        // a camera or from a vehicle see from there.
        var seeing = new HashSet<ICommonSession>();
        _viewPoints.Collect(_points);
        _viewPoints.InRange(_points, epicenter, _range + comp.Intensity.Count, seeing);
        Raise(Make(null, false), seeing, told);

        // Replays record the unsent visuals entity already; they only need the sounds.
        if (_replay.IsRecording)
        {
            if (nearSound != null)
                _audio.PlayStatic(nearSound, Filter.Empty(), soundAt, true, nearSound.Params);
            if (farSound != null)
                _audio.PlayGlobal(farSound, Filter.Empty(), true, farSound.Params);
        }
    }

    private void Raise(WGExplosionEvent ev, IEnumerable<ICommonSession> recipients, HashSet<ICommonSession> told)
    {
        var filter = Filter.Empty();
        foreach (var session in recipients)
        {
            if (told.Add(session))
                filter.AddPlayer(session);
        }

        if (filter.Count > 0)
            RaiseNetworkEvent(ev, filter, recordReplay: false);
    }
}
