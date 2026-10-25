using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._WF.Camera;

/// <summary>Reads whether the player controlling an entity wants the view turned north on entering a grid.</summary>
public sealed class WFDefaultNorthSystem : EntitySystem
{
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    /// <summary>True when the entity's player has <see cref="WFCameraCVars.DefaultNorth"/> on.</summary>
    public bool WantsNorth(EntityUid uid)
    {
        if (!_player.TryGetSessionByEntity(uid, out var session))
            return false;

        if (_net.IsClient)
            return session == _player.LocalSession && _netConfig.GetCVar(WFCameraCVars.DefaultNorth);

        return _netConfig.GetClientCVar(session.Channel, WFCameraCVars.DefaultNorth);
    }
}
