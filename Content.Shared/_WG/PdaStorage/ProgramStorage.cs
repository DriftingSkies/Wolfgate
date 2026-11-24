using Content.Shared.CartridgeLoader;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.Shared._WG.PdaStorage;

/// <summary>Why a program could not be installed into a storage container.</summary>
public enum ProgramInstallCheck : byte
{
    Ok,
    UnknownPrototype,
    NotProgram,
    Duplicate,
    CountLimit,
    NoSpace,
    Blocked,
}

/// <summary>
/// Storage maths shared by the PDA hard drive, data disks and cartridge media.
/// A storage container holds program entities; used space is derived from their
/// <see cref="CartridgeComponent.Size"/> so it can never disagree with the contents.
/// </summary>
public static class ProgramStorage
{
    /// <summary>Storage footprint of a program entity in GQ.</summary>
    public static int Size(IEntityManager entMan, EntityUid program)
    {
        return entMan.TryGetComponent<CartridgeComponent>(program, out var cartridge)
            ? Math.Max(0, cartridge.Size)
            : 0;
    }

    /// <summary>Total size of every program in a container.</summary>
    public static int Used(IEntityManager entMan, BaseContainer container)
    {
        var used = 0;
        foreach (var program in container.ContainedEntities)
        {
            used += Size(entMan, program);
        }

        return used;
    }

    /// <summary>
    /// Size of the programs that count against user storage. Preinstalled firmware is excluded,
    /// like factory apps on a phone; disks count everything they hold.
    /// </summary>
    public static int UsedUserStorage(IEntityManager entMan, BaseContainer container)
    {
        var used = 0;
        foreach (var program in container.ContainedEntities)
        {
            if (!IsSystemApp(entMan, program))
                used += Size(entMan, program);
        }

        return used;
    }

    /// <summary>Whether a program came with the device and does not consume user storage.</summary>
    public static bool IsSystemApp(IEntityManager entMan, EntityUid program)
    {
        return entMan.TryGetComponent<CartridgeComponent>(program, out var cartridge) && cartridge.SystemApp;
    }

    /// <summary>Whether a program is locked against uninstall (core or role-restricted).</summary>
    public static bool IsLocked(IEntityManager entMan, EntityUid program)
    {
        return entMan.TryGetComponent<CartridgeComponent>(program, out var cartridge)
               && (cartridge.Readonly || cartridge.InstallationStatus == InstallationStatus.Readonly);
    }

    /// <summary>Remaining capacity, never negative.</summary>
    public static int Free(IEntityManager entMan, BaseContainer container, int capacity)
    {
        return Math.Max(0, capacity - Used(entMan, container));
    }

    /// <summary>Whether a program with the same localised program name is already stored.</summary>
    public static bool TryFindByName(IEntityManager entMan, BaseContainer container, LocId programName, out EntityUid program)
    {
        foreach (var candidate in container.ContainedEntities)
        {
            if (entMan.TryGetComponent<CartridgeComponent>(candidate, out var cartridge)
                && cartridge.ProgramName == programName)
            {
                program = candidate;
                return true;
            }
        }

        program = EntityUid.Invalid;
        return false;
    }

    /// <summary>Whether a program spawned from the given entity prototype is already stored.</summary>
    public static bool ContainsPrototype(IEntityManager entMan, BaseContainer container, string prototypeId)
    {
        foreach (var candidate in container.ContainedEntities)
        {
            if (entMan.TryGetComponent<MetaDataComponent>(candidate, out var meta)
                && meta.EntityPrototype?.ID == prototypeId)
            {
                return true;
            }
        }

        return false;
    }
}

