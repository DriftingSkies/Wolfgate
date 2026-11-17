namespace Content.Server._WG.Chimera;

/// <summary>
/// Sends the first player who takes this mob a chat message, such as how to talk as it.
/// </summary>
[RegisterComponent, Access(typeof(MindGreetingSystem))]
public sealed partial class MindGreetingComponent : Component
{
    /// <summary>Chat message sent to the first player who takes the mob.</summary>
    [DataField(required: true)]
    public LocId Message;

    /// <summary>Whether the greeting has been sent.</summary>
    [ViewVariables]
    public bool Greeted;
}
