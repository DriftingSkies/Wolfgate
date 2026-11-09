using Content.Server.Database;

namespace Content.Server._WF.Census;

/// <summary>A census row and how many players have answered it.</summary>
public sealed record CensusRow(WolfgateCensus Census, int Responses);
