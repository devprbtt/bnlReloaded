using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.Status;

public record PlayerScoreSnapshot
{
    public int Build { get; init; }
    public int Destroyed { get; init; }
    public int Earned { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
}

public record PlayerSnapshot
{
    public uint PlayerId { get; init; }
    public string? Name { get; init; }
    public string? Hero { get; init; }
    public TeamType? Team { get; init; }
    public PlayerScoreSnapshot? Stats { get; init; }
}

public record QueueSnapshot
{
    public int Count { get; init; }
    public List<PlayerSnapshot> Players { get; init; } = [];
}

public record CubeTeamStatusSnapshot
{
    public TeamType Team { get; init; }
    public int TotalCubes { get; init; }
    public int DestroyedCubes { get; init; }
    public float CurrentHealth { get; init; }
    public float TotalHealth { get; init; }
    public List<CubeHealthSnapshot> Cubes { get; init; } = [];
}

public record CubeStatusSnapshot
{
    public List<CubeTeamStatusSnapshot> Teams { get; init; } = [];
}

public record CubeHealthSnapshot
{
    public UnitLabel Label { get; init; }
    public float CurrentHealth { get; init; }
    public float TotalHealth { get; init; }
    public bool IsDestroyed { get; init; }
}

public record OnlineSnapshot
{
    public int Count { get; init; }
    public List<PlayerSnapshot> Players { get; init; } = [];
}

public record LobbySnapshot
{
    public string InstanceId { get; init; } = string.Empty;
    public string LobbyId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? LobbyName { get; init; }
    public int PlayerCount { get; init; }
    public bool IsPrivate { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool HasStarted { get; init; }
    public float MatchElapsedSeconds { get; init; }
    public CubeStatusSnapshot? CubeStatus { get; init; }
    public List<PlayerSnapshot> Players { get; init; } = [];
}

public record MatchSnapshot
{
    public string MatchId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool HasStarted { get; init; }
    public float MatchElapsedSeconds { get; init; }
    public CubeStatusSnapshot? CubeStatus { get; init; }
    public List<PlayerSnapshot> Players { get; init; } = [];
}

public record GameInstanceSnapshot
{
    public string InstanceId { get; init; } = string.Empty;
    public string GameModeId { get; init; } = string.Empty;
    public GameRankingType Ranking { get; init; }
    public bool IsCustom { get; init; }
    public bool IsMapEditor { get; init; }
    public bool HasStarted { get; init; }
    public bool HasEnded { get; init; }
    public string Status { get; init; } = string.Empty;
    public float MatchElapsedSeconds { get; init; }
    public CubeStatusSnapshot? CubeStatus { get; init; }
    public List<PlayerSnapshot> Players { get; init; } = [];
}

public record RegionStatusSnapshot
{
    public string RegionId { get; init; } = string.Empty;
    public string? RegionName { get; init; }
    public string? RegionHost { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public OnlineSnapshot Online { get; init; } = new();
    public QueueSnapshot RankedQueue { get; init; } = new();
    public QueueSnapshot CasualQueue { get; init; } = new();
    public List<LobbySnapshot> CustomLobbies { get; init; } = [];
    public List<MatchSnapshot> RankedMatches { get; init; } = [];
    public List<MatchSnapshot> CasualMatches { get; init; } = [];
}

public record MasterStatusSnapshot
{
    public DateTimeOffset GeneratedAt { get; init; }
    public List<RegionStatusSnapshot> Regions { get; init; } = [];
}

public static class StatusSnapshotHelpers
{
    public static string? GetKeyId(Key key)
    {
        if (key == Key.None) return null;
        return Databases.Catalogue.GetCard<Card>(key)?.Id ?? key.ToString();
    }
}
