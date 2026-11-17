using Content.Server.Radio;
using Content.Shared._WG.RMCCore.Deafness;

namespace Content.Server._WG.RMCCore.Deafness;

/// <summary>Deaf mobs hear no radio. Nearby speech is garbled for them in <c>ChatSystem.Deafness.cs</c>.</summary>
public sealed partial class DeafnessSystem : SharedDeafnessSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        var user = Transform(args.RadioReceiver).ParentUid;

        if (!HasComp<DeafComponent>(user))
            return;

        args.Cancelled = true;
    }
}
