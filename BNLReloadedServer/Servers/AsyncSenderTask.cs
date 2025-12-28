using System.Threading.Channels;
using NetCoreServer;

namespace BNLReloadedServer.Servers;

public class AsyncSenderTask
{
    private const int ChannelCapacity = 512;
    private readonly Channel<byte[]> _packetBuffer;
    public Guid Id { get; }

    public AsyncSenderTask(TcpSession session)
    {
        Id = session.Id;
        var channelOptions = new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            AllowSynchronousContinuations = false
        };
        _packetBuffer = Channel.CreateBounded<byte[]>(channelOptions);
        _ = RunSendTask(session, _packetBuffer.Reader);
    }

    public void SendPacket(byte[] packet)
    {
        if (!_packetBuffer.Writer.TryWrite(packet))
        {
            Console.WriteLine($"Send queue full for session {Id}, dropping packet.");
        }
    }

    private static async Task RunSendTask(TcpSession session, ChannelReader<byte[]> packets)
    {
        try
        {
            await foreach (var packet in packets.ReadAllAsync())
            {
                try
                {
                    session.Send(packet);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    public void Stop() => _packetBuffer.Writer.TryComplete();
}