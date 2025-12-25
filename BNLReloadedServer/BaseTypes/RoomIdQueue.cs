using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public class RoomIdQueue : RoomId
{
    public override RoomIdType Type => RoomIdType.Queue;

    public Key GameModeKey { get; set; }

    public override void Write(BinaryWriter writer)
    {
        new BitField(true).Write(writer);
        GameModeKey.Write(writer);
    }

    public override void Read(BinaryReader reader)
    {
        var bitField = new BitField(1);
        bitField.Read(reader);
        if (!bitField[0])
        {
            return;
        }

        GameModeKey = Key.ReadRecord(reader);
    }

    public static void WriteRecord(BinaryWriter writer, RoomIdQueue value) => value.Write(writer);

    public static RoomIdQueue ReadRecord(BinaryReader reader)
    {
        var roomIdQueue = new RoomIdQueue();
        roomIdQueue.Read(reader);
        return roomIdQueue;
    }

    public override bool Equals(object? obj)
    {
        return obj is RoomIdQueue roomIdQueue && GameModeKey.Equals(roomIdQueue.GameModeKey);
    }

    public override int GetHashCode() => Type.GetHashCode() ^ GameModeKey.GetHashCode();
}
