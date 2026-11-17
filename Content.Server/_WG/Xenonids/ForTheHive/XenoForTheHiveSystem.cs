using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Shared._WG.Xenonids.ForTheHive;
using Content.Shared.Mind;
using Robust.Shared.Player;
using Content.Shared._WG.Xenonids.Hive;
using Robust.Shared.Map;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Server._WG.Xenonids.Respawn;

namespace Content.Server._WG.Xenonids.ForTheHive;

public sealed partial class XenoForTheHiveSystem : SharedXenoForTheHiveSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private XenoRespawnSystem _xenoRespawn = default!;

    protected override void ForTheHiveShout(EntityUid xeno)
    {
        _chat.TrySendInGameICMessage(xeno, Loc.GetString("rmc-xeno-for-the-hive-announce"), InGameICChatType.Speak, false);
    }

    protected override void ForTheHiveRespawn(EntityUid xeno, TimeSpan time, bool atCorpse = false, EntityCoordinates? corpse = null)
    {
        _xenoRespawn.RespawnXeno(xeno, time, atCorpse, corpse);
    }

}
