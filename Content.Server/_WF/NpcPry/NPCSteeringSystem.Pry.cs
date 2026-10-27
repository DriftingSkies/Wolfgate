using Content.Server._WF.NpcPry;

namespace Content.Server.NPC.Systems;

/// <summary>Stops NPCs retrying a door they can't pry open.</summary>
public sealed partial class NPCSteeringSystem
{
    /// <summary>Pry starts on one door, without a long gap between them, before the NPC gives up on it.</summary>
    private const int PryMaxAttempts = 3;

    /// <summary>A gap this long between attempts starts the count again.</summary>
    private static readonly TimeSpan PryAttemptWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PryGiveUpTime = TimeSpan.FromSeconds(60);

    /// <summary>Counts a pry start on <paramref name="door"/>; false once the NPC has given up on it.</summary>
    private bool TryNotePryAttempt(EntityUid uid, EntityUid door)
    {
        var memory = EnsureComp<WFNpcPryMemoryComponent>(uid);
        var now = _timing.CurTime;

        if (memory.Door != door)
        {
            memory.Door = door;
            memory.Attempts = 0;
            memory.GiveUpUntil = TimeSpan.Zero;
        }
        else if (now < memory.GiveUpUntil)
            return false;
        else if (now - memory.LastAttempt > PryAttemptWindow)
            memory.Attempts = 0;

        memory.LastAttempt = now;
        if (++memory.Attempts <= PryMaxAttempts)
            return true;

        memory.Attempts = 0;
        memory.GiveUpUntil = now + PryGiveUpTime;
        return false;
    }
}
