using Content.Server.Chat.Systems;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Makes whoever a vehicle runs over scream.</summary>
public sealed partial class VehicleRunoverScreamSystem : EntitySystem
{
    private const string ScreamEmote = "Scream";

    [Dependency] private ChatSystem _chat = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MobStateComponent, VehicleRanOverEvent>(OnRanOver);
    }

    private void OnRanOver(Entity<MobStateComponent> ent, ref VehicleRanOverEvent args)
    {
        _chat.TryEmoteWithChat(ent.Owner, ScreamEmote, ignoreActionBlocker: true, forceEmote: true);
    }
}
