using System.Threading.Tasks;
using Content.Shared._WF.Outposts;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>Sends a player one of their saves as grid YAML, for the client's ship previewer.</summary>
public sealed partial class WFOutpostSystem
{
    /// <summary>How often one player may ask for a preview.</summary>
    private static readonly TimeSpan PreviewCooldown = TimeSpan.FromSeconds(1);

    /// <summary>Saves longer than this, in characters, aren't sent; well past what the entity cap allows.</summary>
    private const int MaxPreviewChars = 8 * 1024 * 1024;

    private readonly Dictionary<NetUserId, TimeSpan> _nextPreview = new();

    private void InitializePreview()
    {
        SubscribeNetworkEvent<WFOutpostPreviewRequestEvent>(OnPreviewRequest);
    }

    private void OnPreviewRequest(WFOutpostPreviewRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (ev.Slot < 0 || ev.Slot >= WFOutpostLimits.SaveSlots)
            return;

        // Real time, so the lobby's character editor is limited too.
        var now = _timing.RealTime;
        if (_nextPreview.TryGetValue(session.UserId, out var next) && now < next)
        {
            RaiseNetworkEvent(new WFOutpostPreviewEvent(ev.Slot, default, null, "wf-outpost-preview-wait"), session);
            return;
        }

        _nextPreview[session.UserId] = now + PreviewCooldown;
        _ = SendPreview(session, ev.Slot);
    }

    private async Task SendPreview(ICommonSession session, int slot)
    {
        WFOutpostPreviewEvent reply;
        try
        {
            if (await _db.GetOutpostSaveDataAsync(session.UserId, slot) is not { } row)
            {
                reply = new WFOutpostPreviewEvent(slot, default, null, "wf-outpost-preview-empty");
            }
            else
            {
                var yaml = await Task.Run(() => Unpack(row.Data));
                reply = yaml.Length > MaxPreviewChars
                    ? new WFOutpostPreviewEvent(slot, row.Save.Saved, null, "wf-outpost-preview-too-big")
                    : new WFOutpostPreviewEvent(slot, row.Save.Saved, yaml, null);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read outpost save slot {slot} of {session.Name} for a preview: {e}");
            reply = new WFOutpostPreviewEvent(slot, default, null, "wf-outpost-preview-failed");
        }

        if (session.Status is SessionStatus.Connected or SessionStatus.InGame)
            RaiseNetworkEvent(reply, session);
    }
}
