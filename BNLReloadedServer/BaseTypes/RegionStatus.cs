using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public class RegionStatus
{
    public string RegionId { get; set; } = string.Empty;
    public string? RegionName { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public int OnlinePlayers { get; set; }
    public Dictionary<string, int> Queues { get; set; } = new();
    public List<StatusCustomGame> CustomGames { get; set; } = [];
    public string? Error { get; set; }

    public static void WriteRecord(BinaryWriter writer, RegionStatus value)
    {
        writer.Write(value.RegionId);
        writer.WriteOption(value.RegionName, writer.Write);
        writer.WriteOption(value.Host, writer.Write);
        writer.Write(value.Port);
        writer.Write(value.OnlinePlayers);
        writer.WriteMap(value.Queues, writer.Write, writer.Write);
        writer.WriteList(value.CustomGames, StatusCustomGame.WriteRecord);
        writer.WriteOption(value.Error, writer.Write);
    }

    public static RegionStatus ReadRecord(BinaryReader reader)
    {
        return new RegionStatus
        {
            RegionId = reader.ReadString(),
            RegionName = reader.ReadOption(reader.ReadString),
            Host = reader.ReadOption(reader.ReadString),
            Port = reader.ReadInt32(),
            OnlinePlayers = reader.ReadInt32(),
            Queues = reader.ReadMap<string, int, Dictionary<string, int>>(reader.ReadString, reader.ReadInt32),
            CustomGames = reader.ReadList<StatusCustomGame, List<StatusCustomGame>>(StatusCustomGame.ReadRecord),
            Error = reader.ReadOption(reader.ReadString)
        };
    }
}
