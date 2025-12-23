using System.Text.Json.Serialization;

namespace BNLReloadedServer.BaseTypes;

public class StatusCustomGame
{
    public ulong Id { get; set; }
    public string? Name { get; set; }
    public bool Private { get; set; }
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public CustomGameStatus Status { get; set; }
    public string? StatusDescription { get; set; }
    public List<StatusCustomGamePlayer> PlayerList { get; set; } = [];
}

public class StatusCustomGamePlayer
{
    public uint Id { get; set; }
    public string? Nickname { get; set; }
    public bool Owner { get; set; }
    public TeamType Team { get; set; }
}
