using System.Net.Sockets;
using Timer = System.Timers.Timer;
using BNLReloadedServer.Database;
using TcpClient = NetCoreServer.TcpClient;

namespace BNLReloadedServer.Servers;

public class RegionClient : TcpClient
{
    private readonly RegionClientServiceDispatcher _serviceDispatcher;
    private readonly SessionReader _reader;
    private bool _connected;
    private Timer? _statusTimer;
    private int _statusSending;

    public RegionClient(string address, int port) : base(address, port)
    {
        var sender = new ClientSender(this);
        _serviceDispatcher = new RegionClientServiceDispatcher(sender);
        _reader = new SessionReader(_serviceDispatcher, Databases.ConfigDatabase.DebugMode(),
            "Region client server received packet with incorrect length");
    }
    
    public void DisconnectAndStop()
    {
        _stop = true;
        DisconnectAsync();
        while (IsConnected)
            Thread.Yield();
    }

    protected override void OnConnecting()
    {
        Console.WriteLine("Region client connecting...");
    }

    protected override void OnConnected()
    {
        _connected = true;
        Console.WriteLine($"Region TCP client connected a new session with Id {Id}");

        var host = Databases.ConfigDatabase.RegionPublicHost();
        var guiInfo = Databases.ConfigDatabase.GetRegionInfo();
        
        Databases.PlayerDatabase.SetRegionServerService(_serviceDispatcher.ServiceRegionServer);
        _serviceDispatcher.ServiceRegionServer.SendRegionInfo(host, guiInfo);
        StartStatusReporting();
    }

    protected override void OnDisconnected()
    {
        if (_connected)
            Console.WriteLine($"Region TCP client disconnected a session with Id {Id}");

        _connected = false;
        StopStatusReporting();
        
        // Wait for a while...
        Task.Delay(1000).Wait();

        // Try to connect again
        if (!_stop)
            ConnectAsync();
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        if (size <= 0) return;
        
        _reader.ProcessPacket(buffer, offset, size);
    }

    protected override void OnError(SocketError error)
    {
        Console.WriteLine($"Chat TCP client caught an error with code {error}");
    }

    private void StartStatusReporting()
    {
        StopStatusReporting();
        _statusTimer = new Timer(TimeSpan.FromSeconds(5).TotalMilliseconds);
        _statusTimer.AutoReset = true;
        _statusTimer.Elapsed += (_, _) => SendStatusSnapshot();
        _statusTimer.Start();
    }

    private void StopStatusReporting()
    {
        if (_statusTimer == null) return;
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _statusTimer = null;
    }

    private void SendStatusSnapshot()
    {
        if (Interlocked.Exchange(ref _statusSending, 1) == 1)
        {
            return;
        }

        try
        {
            if (!IsConnected) return;

            var snapshot = Databases.RegionServerDatabase.BuildStatusSnapshot();
            _serviceDispatcher.ServiceRegionServer.SendRegionStatus(snapshot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send region status snapshot: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _statusSending, 0);
        }
    }

    private bool _stop;
}
