using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Emp;

namespace Content.Server._WF.Emp;

/// <summary>
/// Makes shuttle consoles and APCs ignore EMP pulses, so a pulse can't take a ship's helm or power offline.
/// </summary>
public sealed class EmpImmunitySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, EmpAttemptEvent>(OnEmpAttempt);
        SubscribeLocalEvent<ApcComponent, EmpAttemptEvent>(OnEmpAttempt);
    }

    private void OnEmpAttempt<T>(Entity<T> ent, ref EmpAttemptEvent args) where T : IComponent
    {
        args.Cancelled = true;
    }
}
