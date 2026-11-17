using Content.Server.Construction;
using Content.Shared._WG.Construction;

namespace Content.Server._WG.Construction;

/// <summary>
/// Crafts an item recipe back to back. Stops at the first failed craft (out of materials, do-after interrupted);
/// a request made mid-run replaces what is left of the current one.
/// </summary>
public sealed partial class CraftRepeatSystem : EntitySystem
{
    [Dependency] private ConstructionSystem _construction = default!;

    private readonly Dictionary<EntityUid, (string Prototype, int Remaining)> _jobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CraftRepeatRequestEvent>(OnRequest);
    }

    private async void OnRequest(CraftRepeatRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        var running = _jobs.ContainsKey(user);
        _jobs[user] = (ev.PrototypeName, Math.Clamp(ev.Count, 1, CraftRepeatRequestEvent.MaxCount));

        // The running loop picks the new job up after its current craft.
        if (running)
            return;

        while (_jobs.TryGetValue(user, out var job) && job.Remaining > 0)
        {
            _jobs[user] = (job.Prototype, job.Remaining - 1);

            if (args.SenderSession.AttachedEntity != user
                || !await _construction.TryStartItemConstruction(job.Prototype, user))
                break;
        }

        _jobs.Remove(user);
    }
}
