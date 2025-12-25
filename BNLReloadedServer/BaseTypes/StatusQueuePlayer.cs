using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public class StatusQueuePlayer
{
    public uint Id { get; set; }

    public string? Name { get; set; }

    public ulong? SquadId { get; set; }

    public long JoinedAt { get; set; }

    public static void WriteRecord(BinaryWriter writer, StatusQueuePlayer value)
    {
        new BitField(true, value.Name != null, value.SquadId.HasValue, true).Write(writer);
        writer.Write(value.Id);
        writer.WriteOption(value.Name, writer.Write);
        writer.WriteOption(value.SquadId, writer.Write);
        writer.Write(value.JoinedAt);
    }

    public static StatusQueuePlayer ReadRecord(BinaryReader reader)
    {
        var bitField = new BitField(4);
        bitField.Read(reader);

        return new StatusQueuePlayer
        {
            Id = bitField[0] ? reader.ReadUInt32() : 0,
            Name = bitField[1] ? reader.ReadString() : null,
            SquadId = bitField[2] ? reader.ReadUInt64() : null,
            JoinedAt = bitField[3] ? reader.ReadInt64() : 0
        };
    }
}

public class StatusGameStatus
{
    public string Id { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public long? StartedAt { get; set; }

    public float? MatchDurationSeconds { get; set; }

    public List<StatusGamePlayer> Players { get; set; } = [];

    public static void WriteRecord(BinaryWriter writer, StatusGameStatus value)
    {
        new BitField(true, true, value.StartedAt.HasValue, value.MatchDurationSeconds.HasValue, true).Write(writer);
        writer.Write(value.Id);
        writer.Write(value.Mode);
        writer.WriteOption(value.StartedAt, writer.Write);
        writer.WriteOption(value.MatchDurationSeconds, writer.Write);
        writer.WriteList(value.Players, StatusGamePlayer.WriteRecord);
    }

    public static StatusGameStatus ReadRecord(BinaryReader reader)
    {
        var bitField = new BitField(5);
        bitField.Read(reader);

        return new StatusGameStatus
        {
            Id = bitField[0] ? reader.ReadString() : string.Empty,
            Mode = bitField[1] ? reader.ReadString() : string.Empty,
            StartedAt = bitField[2] ? reader.ReadInt64() : null,
            MatchDurationSeconds = bitField[3] ? reader.ReadSingle() : null,
            Players = bitField[4]
                ? reader.ReadList<StatusGamePlayer, List<StatusGamePlayer>>(StatusGamePlayer.ReadRecord)
                : []
        };
    }
}

public class StatusGamePlayer
{
    public uint Id { get; set; }

    public string? Name { get; set; }

    public TeamType Team { get; set; }

    public int Kills { get; set; }

    public int Deaths { get; set; }

    public int Assists { get; set; }

    public static void WriteRecord(BinaryWriter writer, StatusGamePlayer value)
    {
        new BitField(true, value.Name != null, true, true, true, true).Write(writer);
        writer.Write(value.Id);
        writer.WriteOption(value.Name, writer.Write);
        writer.WriteByteEnum(value.Team);
        writer.Write(value.Kills);
        writer.Write(value.Deaths);
        writer.Write(value.Assists);
    }

    public static StatusGamePlayer ReadRecord(BinaryReader reader)
    {
        var bitField = new BitField(6);
        bitField.Read(reader);

        return new StatusGamePlayer
        {
            Id = bitField[0] ? reader.ReadUInt32() : 0,
            Name = bitField[1] ? reader.ReadString() : null,
            Team = bitField[2] ? reader.ReadByteEnum<TeamType>() : TeamType.Neutral,
            Kills = bitField[3] ? reader.ReadInt32() : 0,
            Deaths = bitField[4] ? reader.ReadInt32() : 0,
            Assists = bitField[5] ? reader.ReadInt32() : 0
        };
    }
}
