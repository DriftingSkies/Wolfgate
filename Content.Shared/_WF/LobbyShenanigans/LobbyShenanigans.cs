using Robust.Shared.Serialization;

namespace Content.Shared._WF.LobbyShenanigans;

/// <summary>Every shenanigan by ID; the client's classes and the server's catalogue for the panel both go by these.</summary>
public static class LobbyShenaniganIds
{
    public const string MoproachRain = "moproach-rain";
    public const string CarpMigration = "carp-migration";
    public const string Singularity = "singularity";

    public static readonly string[] All = [MoproachRain, CarpMigration, Singularity];

    /// <summary>Fluent ID of a shenanigan's name; its tooltip adds -tooltip.</summary>
    public static string Name(string id) => $"wf-lobby-shenanigans-{id}";
}

/// <summary>A shenanigan set off for every client from the SSymphony panel.</summary>
[Serializable, NetSerializable]
public sealed class LobbyShenaniganTriggerEvent(string id) : EntityEventArgs
{
    public string Id = id;
}

/// <summary>Everything the shenanigans show goes, on every client, from the SSymphony panel.</summary>
[Serializable, NetSerializable]
public sealed class LobbyShenaniganClearEvent : EntityEventArgs;
