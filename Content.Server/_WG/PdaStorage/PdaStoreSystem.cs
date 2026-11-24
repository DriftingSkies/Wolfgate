using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.PdaStorage;
using Content.Shared._WG.PdaStorage.Prototypes;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.PdaStorage;

/// <summary>Installs the app store on every PDA at map init, like the GameHub installer.</summary>
public sealed class PdaStoreInstallerSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PdaAppStoreInstallerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<PdaAppStoreInstallerComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<CartridgeLoaderComponent>(ent, out var loader))
            _cartridges.InstallProgram(ent, ent.Comp.Program.Id, deinstallable: false, loader: loader);
    }
}

/// <summary>
/// Server-authoritative app store: catalogue flags, timed downloads, install/uninstall over the
/// existing cartridge storage, and the state the store fragment renders.
/// </summary>
public sealed class PdaStoreSystem : EntitySystem
{
    /// <summary>Fixed overhead of a download, seconds.</summary>
    private const float DownloadBaseSeconds = 0.5f;

    /// <summary>Extra seconds per GQ of application size.</summary>
    private const float DownloadSecondsPerGq = 0.04f;

    private const float ProgressUpdateInterval = 0.1f;

    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PdaStoreCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PdaStoreCartridgeComponent, CartridgeMessageEvent>(OnStoreMessage);
        SubscribeLocalEvent<PdaStoreCartridgeComponent, CartridgeDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<PdaComponent, BoundUIClosedEvent>(OnPdaUiClosed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PdaDownloadComponent>();
        while (query.MoveNext(out var loaderUid, out var download))
        {
            download.Elapsed += frameTime;

            if (download.Elapsed >= download.NextUpdate)
            {
                download.NextUpdate = download.Elapsed + ProgressUpdateInterval;
                SendState(download.StoreProgram, loaderUid);
            }

            if (download.Elapsed < download.Duration)
                continue;

            CompleteDownload(loaderUid, download);
        }
    }

    private void OnUiReady(Entity<PdaStoreCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        SendState(ent, args.Loader);
    }

    private void OnDeactivated(Entity<PdaStoreCartridgeComponent> ent, ref CartridgeDeactivatedEvent args)
    {
        if (TryComp<PdaDownloadComponent>(args.Loader, out var download) && download.StoreProgram == ent.Owner)
            RemCompDeferred<PdaDownloadComponent>(args.Loader);
    }

    /// <summary>Closing the PDA screen aborts a download, like closing the store program does.</summary>
    private void OnPdaUiClosed(EntityUid uid, PdaComponent pda, BoundUIClosedEvent args)
    {
        if (!PdaUiKey.Key.Equals(args.UiKey))
            return;

        if (HasComp<PdaDownloadComponent>(uid))
            RemCompDeferred<PdaDownloadComponent>(uid);
    }

    private void OnStoreMessage(Entity<PdaStoreCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        // The cartidge loader relays every program message as the base event; only the store's own
        // messages are handled here, everything else belongs to another program.
        if (args is not PdaStoreMessage message)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        // Only the loader that actually has this store installed may act on it.
        if (!_cartridges.GetInstalled(loaderUid).Contains(ent))
            return;

        switch (message.Action)
        {
            case PdaStoreAction.RequestState:
                SendState(ent, loaderUid, error: PdaStoreError.None, loader: loader);
                break;
            case PdaStoreAction.Install:
                StartDownload(ent, loaderUid, message.AppId, loader);
                break;
            case PdaStoreAction.InstallFromMedia:
                InstallFromMedia(ent, loaderUid, message.Program, loader);
                break;
            case PdaStoreAction.CopyToDisk:
                CopyToDisk(ent, loaderUid, message.Program, loader);
                break;
            case PdaStoreAction.CopyFromDisk:
                CopyFromDisk(ent, loaderUid, message.Program, loader);
                break;
            case PdaStoreAction.Uninstall:
                Uninstall(ent, loaderUid, message.Program, loader);
                break;
        }
    }

    private void StartDownload(EntityUid storeProgram, EntityUid loaderUid, string appId, CartridgeLoaderComponent loader)
    {
        if (HasComp<PdaDownloadComponent>(loaderUid))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.AlreadyDownloading, loader);
            return;
        }

        if (!TryGetApp(appId, out var app))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.UnknownApp, loader);
            return;
        }

        if (!app.AvailableInStore)
        {
            SendState(storeProgram, loaderUid, PdaStoreError.Unavailable, loader);
            return;
        }

        if (!app.Devices.HasFlag(PdaDeviceFlags.Pda))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.Incompatible, loader);
            return;
        }

        if (!HasDownloadAccess(loaderUid, app))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.NoAccess, loader);
            return;
        }

        var check = _cartridges.CheckInstall(loaderUid, app.Program, loader);
        if (check != ProgramInstallCheck.Ok)
        {
            SendState(storeProgram, loaderUid, MapInstallError(check), loader);
            return;
        }

        if (!_prototypes.TryIndex(app.Program, out EntityPrototype? programProto)
            || !programProto.TryGetComponent<CartridgeComponent>(out var program))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.UnknownApp, loader);
            return;
        }

        var download = EnsureComp<PdaDownloadComponent>(loaderUid);
        download.StoreProgram = storeProgram;
        download.AppId = app.ID;
        download.Program = app.Program;
        download.Size = Math.Max(0, program.Size);
        download.Duration = DownloadBaseSeconds + DownloadSecondsPerGq * download.Size;
        download.Elapsed = 0f;
        download.NextUpdate = 0f;

        SendState(storeProgram, loaderUid, error: PdaStoreError.None, loader: loader);
    }

    private void CompleteDownload(EntityUid loaderUid, PdaDownloadComponent download)
    {
        var storeProgram = download.StoreProgram;
        var error = PdaStoreError.None;

        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
        {
            RemCompDeferred<PdaDownloadComponent>(loaderUid);
            return;
        }

        if (!TryGetApp(download.AppId, out var app))
        {
            error = PdaStoreError.UnknownApp;
        }
        else if (!HasDownloadAccess(loaderUid, app))
        {
            error = PdaStoreError.NoAccess;
        }
        else
        {
            var check = _cartridges.CheckInstall(loaderUid, app.Program, loader);
            if (check == ProgramInstallCheck.Ok)
            {
                if (!_cartridges.InstallProgram(loaderUid, app.Program, loader: loader))
                    error = PdaStoreError.InstallFailed;
            }
            else
            {
                error = MapInstallError(check);
            }
        }

        RemCompDeferred<PdaDownloadComponent>(loaderUid);
        // The removal is deferred, so exclude the finished job from the completion snapshot.
        SendState(storeProgram, loaderUid, error, loader, completed: download);
    }

    private void InstallFromMedia(EntityUid storeProgram, EntityUid loaderUid, NetEntity mediaNet, CartridgeLoaderComponent loader)
    {
        var media = GetEntity(mediaNet);
        if (loader.CartridgeSlot.Item != media || !TryComp<CartridgeComponent>(media, out var cartridge))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InvalidMedia, loader);
            return;
        }

        var prototype = MetaData(media).EntityPrototype?.ID;
        if (prototype == null)
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InvalidMedia, loader);
            return;
        }

        // The media copy has no download step; it is already in the device.
        var check = _cartridges.CheckInstall(loaderUid, prototype, loader);
        if (check != ProgramInstallCheck.Ok)
        {
            SendState(storeProgram, loaderUid, MapInstallError(check), loader);
            return;
        }

        if (!_cartridges.InstallProgram(loaderUid, prototype, loader: loader))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InstallFailed, loader);
            return;
        }

        // WOLFGATE(PdaStorage): installing from media consumes only disposable media, never downloads or disk copies.
        if (cartridge.Disposable)
        {
            _containers.RemoveEntity(loaderUid, media);
            QueueDel(media);
        }

        SendState(storeProgram, loaderUid, error: PdaStoreError.None, loader: loader);
    }

    private void CopyToDisk(EntityUid storeProgram, EntityUid loaderUid, NetEntity programNet, CartridgeLoaderComponent loader)
    {
        var programUid = GetEntity(programNet);
        if (!_cartridges.GetInstalled(loaderUid).Contains(programUid)
            || !TryComp<CartridgeComponent>(programUid, out var source))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.NotInstalled, loader);
            return;
        }

        if (!TryGetDisk(loaderUid, out var diskUid, out var disk, out var container))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.NoDisk, loader);
            return;
        }

        var prototype = MetaData(programUid).EntityPrototype?.ID;
        if (prototype == null)
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InvalidMedia, loader);
            return;
        }

        if (ProgramStorage.TryFindByName(EntityManager, container, source.ProgramName, out _)
            || ProgramStorage.ContainsPrototype(EntityManager, container, prototype))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.AlreadyInstalled, loader);
            return;
        }

        var size = ProgramStorage.Size(EntityManager, programUid);
        if (ProgramStorage.Used(EntityManager, container) + size > disk.Capacity)
        {
            SendState(storeProgram, loaderUid, PdaStoreError.DiskFull, loader);
            return;
        }

        var copy = Spawn(prototype, new EntityCoordinates(diskUid, 0, 0));
        if (!TryComp<CartridgeComponent>(copy, out var copyCartridge))
        {
            QueueDel(copy);
            SendState(storeProgram, loaderUid, PdaStoreError.InvalidMedia, loader);
            return;
        }

        copyCartridge.LoaderUid = null;
        _containers.Insert(copy, container);
        SendState(storeProgram, loaderUid, error: PdaStoreError.None, loader: loader);
    }

    private void CopyFromDisk(EntityUid storeProgram, EntityUid loaderUid, NetEntity programNet, CartridgeLoaderComponent loader)
    {
        if (!TryGetDisk(loaderUid, out _, out _, out var container))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.NoDisk, loader);
            return;
        }

        var stored = GetEntity(programNet);
        if (!container.ContainedEntities.Contains(stored) || !TryComp<CartridgeComponent>(stored, out _))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InvalidMedia, loader);
            return;
        }

        var prototype = MetaData(stored).EntityPrototype?.ID;
        if (prototype == null)
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InvalidMedia, loader);
            return;
        }

        var check = _cartridges.CheckInstall(loaderUid, prototype, loader);
        if (check != ProgramInstallCheck.Ok)
        {
            SendState(storeProgram, loaderUid, MapInstallError(check), loader);
            return;
        }

        if (!_cartridges.InstallProgram(loaderUid, prototype, loader: loader))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InstallFailed, loader);
            return;
        }

        SendState(storeProgram, loaderUid, error: PdaStoreError.None, loader: loader);
    }

    private bool TryGetDisk(EntityUid loaderUid, out EntityUid diskUid,
        [NotNullWhen(true)] out DataDiskComponent? disk, [NotNullWhen(true)] out BaseContainer? container)
    {
        diskUid = default;
        disk = default!;
        container = default!;

        if (!TryComp<PdaComponent>(loaderUid, out var pda) || pda.DiskSlot.Item is not { } item)
            return false;

        if (!TryComp<DataDiskComponent>(item, out disk))
            return false;

        if (!_containers.TryGetContainer(item, DataDiskComponent.ContainerId, out container))
            return false;

        diskUid = item;
        return true;
    }

    private void Uninstall(EntityUid storeProgram, EntityUid loaderUid, NetEntity programNet, CartridgeLoaderComponent loader)
    {
        var programUid = GetEntity(programNet);
        if (!_cartridges.GetInstalled(loaderUid).Contains(programUid))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.NotInstalled, loader);
            return;
        }

        if (ProgramStorage.IsLocked(EntityManager, programUid))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.Readonly, loader);
            return;
        }

        if (!_cartridges.UninstallProgram(loaderUid, programUid, loader))
        {
            SendState(storeProgram, loaderUid, PdaStoreError.InstallFailed, loader);
            return;
        }

        SendState(storeProgram, loaderUid, error: PdaStoreError.None, loader: loader);
    }

    /// <summary>Rebuilds and sends the store state to the UI hosted on <paramref name="storeProgram"/>.</summary>
    private void SendState(EntityUid storeProgram, EntityUid loaderUid, PdaStoreError error = PdaStoreError.None,
        CartridgeLoaderComponent? loader = null, PdaDownloadComponent? completed = null)
    {
        if (!Resolve(loaderUid, ref loader))
            return;

        var state = BuildState(storeProgram, loaderUid, loader, error, completed);
        _cartridges.UpdateCartridgeUiState(loaderUid, state);
    }

    private PdaStoreUiState BuildState(EntityUid storeProgram, EntityUid loaderUid, CartridgeLoaderComponent loader,
        PdaStoreError error, PdaDownloadComponent? completed = null)
    {
        var state = new PdaStoreUiState
        {
            Capacity = loader.StorageCapacity,
            Used = _cartridges.GetUsedCapacity(loaderUid, loader),
            Error = error,
        };

        if (TryComp<PdaDownloadComponent>(loaderUid, out var download) && download != completed)
        {
            state.Downloading = true;
            state.DownloadAppId = download.AppId;
            state.DownloadSize = download.Size;
            state.DownloadProgress = Math.Clamp(download.Elapsed / Math.Max(download.Duration, 0.01f), 0f, 1f) * download.Size;
        }

        var tags = _access.FindAccessTags(loaderUid);
        var installedPrototypes = new HashSet<string>();
        var categoryOrder = _prototypes.EnumeratePrototypes<PdaAppCategoryPrototype>()
            .ToDictionary(category => category.ID, category => category.Order);

        if (TryGetDisk(loaderUid, out var diskUid, out var disk, out var diskContainer))
        {
            state.DiskCapacity = disk.Capacity;
            state.DiskUsed = ProgramStorage.Used(EntityManager, diskContainer);
            state.DiskName = MetaData(diskUid).EntityName;

            foreach (var storedUid in diskContainer.ContainedEntities)
            {
                if (!TryComp<CartridgeComponent>(storedUid, out var storedProgram))
                    continue;

                state.Disk.Add(new PdaStoreProgramEntry
                {
                    Program = GetNetEntity(storedUid),
                    AppId = FindAppId(MetaData(storedUid).EntityPrototype?.ID),
                    Name = storedProgram.ProgramName,
                    Size = Math.Max(0, storedProgram.Size),
                    Readonly = true,
                });
            }
        }

        if (loader.CartridgeSlot.Item is { } cartridgeItem
            && TryComp<CartridgeComponent>(cartridgeItem, out var cartridgeProgram))
        {
            state.Cartridge.Add(new PdaStoreProgramEntry
            {
                Program = GetNetEntity(cartridgeItem),
                AppId = FindAppId(MetaData(cartridgeItem).EntityPrototype?.ID),
                Name = cartridgeProgram.ProgramName,
                Size = Math.Max(0, cartridgeProgram.Size),
                Readonly = true,
            });
        }

        foreach (var programUid in _cartridges.GetInstalled(loaderUid))
        {
            if (!TryComp<CartridgeComponent>(programUid, out var cartridge))
                continue;

            var programPrototype = MetaData(programUid).EntityPrototype?.ID;
            if (programPrototype != null)
                installedPrototypes.Add(programPrototype);

            state.Installed.Add(new PdaStoreProgramEntry
            {
                Program = GetNetEntity(programUid),
                AppId = FindAppId(programPrototype),
                Name = cartridge.ProgramName,
                Size = Math.Max(0, cartridge.Size),
                Readonly = cartridge.Readonly || cartridge.InstallationStatus == InstallationStatus.Readonly,
                Running = loader.ActiveProgram == programUid,
            });
        }

        foreach (var app in _prototypes.EnumeratePrototypes<PdaAppPrototype>())
        {
            if (!app.AvailableInStore)
                continue;

            if (!_prototypes.TryIndex(app.Program, out EntityPrototype? programProto)
                || !programProto.TryGetComponent<CartridgeComponent>(out var program))
            {
                continue;
            }

            state.Apps.Add(new PdaStoreAppEntry
            {
                Id = app.ID,
                Name = program.ProgramName,
                Description = app.Description,
                Category = app.Category,
                CategoryName = _prototypes.Index(app.Category).Name,
                Icon = program.Icon,
                Size = Math.Max(0, program.Size),
                Installed = installedPrototypes.Contains(app.Program),
                Compatible = app.Devices.HasFlag(PdaDeviceFlags.Pda),
                HasAccess = HasDownloadAccess(loaderUid, app, tags),
                Verified = app.Verified,
            });
        }

        state.Apps.Sort((a, b) => CompareApps(a, b, categoryOrder));
        return state;
    }

    private static int CompareApps(PdaStoreAppEntry a, PdaStoreAppEntry b,
        IReadOnlyDictionary<string, int> categoryOrder)
    {
        var orderA = categoryOrder.GetValueOrDefault(a.Category, int.MaxValue);
        var orderB = categoryOrder.GetValueOrDefault(b.Category, int.MaxValue);
        if (orderA != orderB)
            return orderA.CompareTo(orderB);

        var name = string.CompareOrdinal(a.Name, b.Name);
        return name != 0 ? name : string.CompareOrdinal(a.Id, b.Id);
    }

    private bool TryGetApp(string appId, [NotNullWhen(true)] out PdaAppPrototype? app)
    {
        if (string.IsNullOrEmpty(appId) || !_prototypes.TryIndex(appId, out app))
        {
            app = default;
            return false;
        }

        return true;
    }

    private string FindAppId(string? programPrototype)
    {
        if (programPrototype == null)
            return string.Empty;

        foreach (var app in _prototypes.EnumeratePrototypes<PdaAppPrototype>())
        {
            if (app.Program == programPrototype)
                return app.ID;
        }

        return string.Empty;
    }

    private bool HasDownloadAccess(EntityUid loaderUid, PdaAppPrototype app,
        ICollection<ProtoId<AccessLevelPrototype>>? tags = null)
    {
        if (app.DownloadAccess.Count == 0)
            return true;

        tags ??= _access.FindAccessTags(loaderUid);

        foreach (var required in app.DownloadAccess)
        {
            if (tags.Contains(required))
                return true;
        }

        return false;
    }

    private static PdaStoreError MapInstallError(ProgramInstallCheck check)
    {
        return check switch
        {
            ProgramInstallCheck.UnknownPrototype => PdaStoreError.UnknownApp,
            ProgramInstallCheck.NotProgram => PdaStoreError.UnknownApp,
            ProgramInstallCheck.Duplicate => PdaStoreError.AlreadyInstalled,
            ProgramInstallCheck.CountLimit => PdaStoreError.NoSpace,
            ProgramInstallCheck.NoSpace => PdaStoreError.NoSpace,
            ProgramInstallCheck.Blocked => PdaStoreError.InstallFailed,
            _ => PdaStoreError.None,
        };
    }
}


