using System.Linq;
using Content.Server._DV.Mail.Components;
using Content.Server._DV.Mail.EntitySystems;
using Content.Server._NF.Bank;
using Content.Server._NF.Mail.Components;
using Content.Server._NF.SectorServices;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Administration.Logs;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.Paintings;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Paintings;

/// <summary>The art app's view of the catalogue on one PDA.</summary>
[RegisterComponent]
public sealed partial class WGArtAppComponent : Component
{
    [ViewVariables]
    public int Page;

    [ViewVariables]
    public string Search = string.Empty;

    /// <summary>Showing the viewer's own paintings rather than the whole catalogue.</summary>
    [ViewVariables]
    public bool Mine;

    /// <summary>The player the list was last built for, whose paintings are marked as theirs.</summary>
    [ViewVariables]
    public Guid? Viewer;
}

/// <summary>
/// The art PDA app: browses the catalogue, orders prints delivered by mail, takes a player's own paintings out, and
/// scans finished canvases the PDA is used on.
/// </summary>
public sealed partial class WGArtAppSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private WGCanvasSystem _canvas = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private MailSystem _mail = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private WGPaintingStoreSystem _store = default!;

    private static readonly EntProtoId ArtCartridge = "WGArtCartridge";

    /// <summary>The empty parcel a print is posted in.</summary>
    private static readonly EntProtoId Parcel = "MailAdminFun";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPdaProgramsEvent>(OnPdaPrograms);
        SubscribeLocalEvent<WGArtAppComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<WGArtAppComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<WGArtAppComponent, CartridgeAfterInteractEvent>(OnAfterInteract);
    }

    private void OnPdaPrograms(ref WGPdaProgramsEvent ev)
    {
        ev.Programs.Add(ArtCartridge);
    }

    private void OnUiReady(Entity<WGArtAppComponent> ent, ref CartridgeUiReadyEvent args)
    {
        if (TryComp<ActorComponent>(Transform(args.Loader).ParentUid, out var actor))
            ent.Comp.Viewer = actor.PlayerSession.UserId.UserId;
        Push(ent, args.Loader);
    }

    private void OnMessage(Entity<WGArtAppComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not WGArtAppMessage msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        Guid? userId = TryComp<ActorComponent>(args.Actor, out var actor) ? actor.PlayerSession.UserId.UserId : null;
        ent.Comp.Viewer = userId;

        switch (msg.Action)
        {
            case WGArtAppAction.Page:
                ent.Comp.Page = Math.Max(0, msg.Page);
                break;
            case WGArtAppAction.Search:
                ent.Comp.Search = msg.Text.Length > WGPaintingRules.MaxTitleLength
                    ? msg.Text[..WGPaintingRules.MaxTitleLength]
                    : msg.Text;
                ent.Comp.Page = 0;
                break;
            case WGArtAppAction.ShowCatalogue:
            case WGArtAppAction.ShowMine:
                ent.Comp.Mine = msg.Action == WGArtAppAction.ShowMine;
                ent.Comp.Page = 0;
                break;
            case WGArtAppAction.Order:
                Order(ent, loader, args.Actor, msg.Text);
                return;
            case WGArtAppAction.Remove when userId is { } id:
                Remove(ent, loader, args.Actor, id, msg.Text);
                return;
        }

        Push(ent, loader);
    }

    private void Push(Entity<WGArtAppComponent> ent, EntityUid loader)
    {
        var comp = ent.Comp;
        IEnumerable<WGPaintingEntry> source = _store.Catalogue;
        if (comp.Mine)
            source = comp.Viewer is { } viewer ? source.Where(e => e.Author == viewer) : Enumerable.Empty<WGPaintingEntry>();
        if (comp.Search.Trim() is { Length: > 0 } search)
        {
            source = source.Where(e => e.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Artist.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = source.ToList();
        var pages = Math.Max(1, (all.Count + WGPaintingRules.PageSize - 1) / WGPaintingRules.PageSize);
        comp.Page = Math.Clamp(comp.Page, 0, pages - 1);
        var entries = all.Skip(comp.Page * WGPaintingRules.PageSize)
            .Take(WGPaintingRules.PageSize)
            .Select(e => new WGArtAppEntry(e.Hash, e.Width, e.Height, e.Title, e.Artist,
                comp.Viewer != null && e.Author == comp.Viewer))
            .ToList();

        _cartridgeLoader.UpdateCartridgeUiState(loader, new WGArtAppUiState(entries, comp.Page, pages, comp.Mine,
            comp.Search, _cfg.GetCVar(WGPaintingCVars.OrderPrice), _store.Loaded));
    }

    private void Refresh(Entity<WGArtAppComponent> ent, EntityUid loader)
    {
        if (!TerminatingOrDeleted(ent) && !TerminatingOrDeleted(loader))
            Push(ent, loader);
    }

    private void OnAfterInteract(Entity<WGArtAppComponent> ent, ref CartridgeAfterInteractEvent args)
    {
        var interact = args.InteractEvent;
        if (interact.Handled
            || !interact.CanReach
            || interact.Target is not { } target
            || !TryComp<WGCanvasComponent>(target, out var canvas))
            return;

        interact.Handled = true;
        if (TryComp<ActorComponent>(interact.User, out var actor))
            Scan(ent, args.Loader, interact.User, actor.PlayerSession.UserId.UserId, (target, canvas));
    }

    private async void Scan(Entity<WGArtAppComponent> ent, EntityUid loader, EntityUid user, Guid userId,
        Entity<WGCanvasComponent> target)
    {
        var result = await _store.Scan(target, user, userId);
        if (TerminatingOrDeleted(user))
            return;

        var message = Loc.GetString($"wg-art-app-scan-{result.ToString().ToLowerInvariant()}", ("canvas", target.Owner));
        _popup.PopupEntity(message, user, user,
            result == WGPaintingScanResult.Stored ? PopupType.Medium : PopupType.SmallCaution);
        Refresh(ent, loader);
    }

    private async void Remove(Entity<WGArtAppComponent> ent, EntityUid loader, EntityUid user, Guid userId, string hash)
    {
        var removed = await _store.Remove(hash, user, userId);
        if (!TerminatingOrDeleted(user))
            _popup.PopupEntity(Loc.GetString(removed ? "wg-art-app-removed" : "wg-art-app-remove-failed"), user, user);
        Refresh(ent, loader);
    }

    /// <summary>Why a user can't be posted a print now, as a Fluent id, or null when they can.</summary>
    private string? MailProblem(EntityUid user, out SectorMailComponent? sectorMail, out MailRecipient? recipient,
        out EntityUid? teleporter)
    {
        sectorMail = null;
        recipient = null;
        teleporter = null;
        if (!TryComp(_sectorService.GetServiceEntity(), out sectorMail))
            return "wg-art-app-order-no-mail";
        if (!HasComp<MailReceiverComponent>(user) || !_mail.TryGetMailRecipientForReceiver(user, out recipient))
            return "wg-art-app-order-no-recipient";
        if (!_mail.TryGetMailTeleporterForReceiver(user, out _, out teleporter))
            return "wg-art-app-order-no-teleporter";
        return null;
    }

    /// <summary>Charges the user and posts them a print, which arrives with the next mail delivery.</summary>
    private async void Order(Entity<WGArtAppComponent> ent, EntityUid loader, EntityUid user, string hash)
    {
        if (!_store.TryGetEntry(hash, out var entry))
        {
            _popup.PopupEntity(Loc.GetString("wg-art-app-order-gone"), user, user, PopupType.SmallCaution);
            Refresh(ent, loader);
            return;
        }

        if (MailProblem(user, out _, out _, out _) is { } early)
        {
            _popup.PopupEntity(Loc.GetString(early), user, user, PopupType.SmallCaution);
            return;
        }

        var pixels = await _store.GetPixelsAsync(hash);
        if (TerminatingOrDeleted(user))
            return;

        if (pixels == null || !_store.TryGetEntry(hash, out _))
        {
            _popup.PopupEntity(Loc.GetString("wg-art-app-order-gone"), user, user, PopupType.SmallCaution);
            Refresh(ent, loader);
            return;
        }

        // Checked again: the user may have moved on while the pixels loaded.
        if (MailProblem(user, out var sectorMail, out var recipient, out var teleporter) is { } problem)
        {
            _popup.PopupEntity(Loc.GetString(problem), user, user, PopupType.SmallCaution);
            return;
        }

        var price = _cfg.GetCVar(WGPaintingCVars.OrderPrice);
        if (price > 0 && !_bank.TryBankWithdraw(user, price))
        {
            _popup.PopupEntity(Loc.GetString("wg-art-app-order-funds", ("price", price)), user, user,
                PopupType.SmallCaution);
            return;
        }

        var coordinates = Transform(teleporter!.Value).Coordinates;
        if (_canvas.SpawnPrint(entry, pixels, coordinates) is not { } print)
            return;

        var parcel = Spawn(Parcel, coordinates);
        var contents = _container.EnsureContainer<Container>(parcel, "contents");
        _container.Insert(print, contents);
        _mail.SetupMail(parcel, sectorMail!, recipient!.Value);
        var queue = _container.EnsureContainer<Container>(teleporter.Value, "queued");
        _container.Insert(parcel, queue);

        _popup.PopupEntity(Loc.GetString("wg-art-app-ordered", ("title", entry.Title), ("price", price)), user, user,
            PopupType.Medium);
        _adminLog.Add(LogType.WGPainting, LogImpact.Low,
            $"{ToPrettyString(user):player} ordered a print of \"{entry.Title}\" ({hash}) for {price}");
    }
}
