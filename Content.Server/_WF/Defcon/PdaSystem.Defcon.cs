using Content.Server._WF.Defcon;
using Content.Shared.PDA;

namespace Content.Server.PDA;

/// <summary>Shows the sector DEFCON on the PDA, under the war level.</summary>
public sealed partial class PdaSystem
{
    [Dependency] private WFDefconSystem _defcon = default!;

    private void InitializeDefcon()
    {
        SubscribeLocalEvent<WFDefconChangedEvent>(_ => UpdateOpenPdasForDefcon());
    }

    /// <summary>Refreshes open PDAs for their viewers, so the viewer's balance is kept.</summary>
    private void UpdateOpenPdasForDefcon()
    {
        var query = AllEntityQuery<PdaComponent>();
        while (query.MoveNext(out var uid, out var pda))
        {
            if (!_ui.IsUiOpen(uid, PdaUiKey.Key))
                continue;

            foreach (var actor in _ui.GetActors(uid, PdaUiKey.Key))
            {
                UpdatePdaUi(uid, pda, actor);
            }
        }
    }

    private string? WithDefcon(string? warLevel)
    {
        return _defcon.PdaLine() is { } line ? $"{warLevel}\n{line}" : warLevel;
    }
}
