namespace Content.Shared.Materials.OreSilo;

public abstract partial class SharedOreSiloSystem
{
    /// <summary>
    /// Whether a powered silo can reach a client on its own grid within the given range.
    /// </summary>
    public bool CanTransmit(Entity<TransformComponent?> silo, EntityUid client, float range)
    {
        if (!Resolve(silo, ref silo.Comp))
            return false;

        if (!_powerReceiver.IsPowered(silo.Owner))
            return false;

        if (_transform.GetGrid(client) != _transform.GetGrid(silo.Owner))
            return false;

        return _transform.InRange((silo.Owner, silo.Comp), client, range);
    }

    /// <summary>
    /// Links a client to a silo as the silo's window does, without the power or range check (the caller has its own
    /// rules), moving the client's stored materials into the silo; widens the silo's range to at least
    /// <paramref name="range"/> when given. False when the client is already linked or isn't a silo client. Used when
    /// a commanded ship (ShipCommand) fits its lathes out at spawn.
    /// </summary>
    public bool TryLinkClient(Entity<OreSiloComponent?> silo, EntityUid client, float? range = null)
    {
        if (!Resolve(silo, ref silo.Comp) || !_clientQuery.TryComp(client, out var clientComp) || clientComp.Silo != null)
            return false;

        if (range is { } wider && wider > silo.Comp.Range)
            silo.Comp.Range = wider;

        var clientMats = _materialStorage.GetStoredMaterials(client, true);
        var inverseMats = new Dictionary<string, int>();
        foreach (var (mat, amount) in clientMats)
            inverseMats.Add(mat, -amount);
        _materialStorage.TryChangeMaterialAmount(client, inverseMats, localOnly: true);
        _materialStorage.TryChangeMaterialAmount(silo.Owner, clientMats);

        silo.Comp.Clients.Add(client);
        Dirty(silo.Owner, silo.Comp);
        clientComp.Silo = silo;
        Dirty(client, clientComp);
        return true;
    }
}
