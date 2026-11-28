// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 ImHoks <142083149+ImHoks@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 ImHoks <imhokzzzz@gmail.com>
// SPDX-FileCopyrightText: 2025 KillanGenifer <killangenifer@gmail.com>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Actions;
using Content.Shared.Mind;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Serialization;

namespace Content.Shared._CorvaxNext.Silicons.Borgs;

public abstract partial class SharedAiRemoteControlSystem : EntitySystem
{
    [Dependency] private SharedStationAiSystem _stationAiSystem = default!;
    [Dependency] private SharedTransformSystem _xformSystem = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public void ReturnMindIntoAi(EntityUid entity)
    {
        if (!TryComp<AiRemoteControllerComponent>(entity, out var remoteComp))
            return;

        // WOLFGATE(Cyborgs) START: a destroyed core leaves the AI in the borg instead of throwing on the deleted brain
        if (remoteComp.AiHolder is { } holder && TerminatingOrDeleted(holder))
            return;
        // WOLFGATE END

        if (remoteComp?.AiHolder == null
            || !_stationAiSystem.TryGetCore(remoteComp.AiHolder.Value, out var stationAiCore)
            || stationAiCore.Comp?.RemoteEntity == null)
            return;

        if (remoteComp.LinkedMind == null)
            return;

        if (!TryComp<StationAiHeldComponent>(remoteComp.AiHolder.Value, out var stationAiHeldComp))
            return;

        stationAiHeldComp.CurrentConnectedEntity = null;

        // WOLFGATE(Cyborgs) START: free the eye before the mind returns, so a failed transfer can't strand the core on a dead hologram
        _stationAiSystem.SwitchRemoteEntityMode(stationAiCore, true);
        // WOLFGATE END
        _mind.TransferTo(remoteComp.LinkedMind.Value, remoteComp.AiHolder);

        // _stationAiSystem.SwitchRemoteEntityMode(stationAiCore, true); // WOLFGATE(Cyborgs): the mode switch now runs before the mind transfer
        remoteComp.AiHolder = null;
        remoteComp.LinkedMind = null;

        // WOLFGATE(Cyborgs) START: a drone being deleted has no place left to drop the eye
        // _xformSystem.SetCoordinates(stationAiCore.Comp.RemoteEntity.Value, Transform(entity).Coordinates);
        if (stationAiCore.Comp.RemoteEntity is { } eye && !TerminatingOrDeleted(entity) && !TerminatingOrDeleted(eye))
            _xformSystem.SetCoordinates(eye, Transform(entity).Coordinates);
        // WOLFGATE END
    }
}

public sealed partial class ReturnMindIntoAiEvent : InstantActionEvent
{
}

public sealed partial class ToggleRemoteDevicesScreenEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public enum RemoteDeviceUiKey : byte
{
    Key
}
