using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;

var configs = Databases.ConfigDatabase;
var masterMode = configs.IsMaster();
var toJson = configs.DoToJson();
var fromJson = configs.DoFromJson();
var runServer = configs.DoRunServer();

const int masterBufferSize = 512000;   // 500KB for control traffic
const int regionBufferSize = 1500000;  // 1.5MB for player-facing data
const int matchBufferSize = 1500000;   // 1.5MB for match traffic
const int regionClientBufferSize = 750000; // 750KB for upstream client

if (toJson || fromJson)
{
    var serializedPath = Path.Combine(Databases.CacheFolderPath, configs.FromJsonCdbName());
    var serializedPath2 = Path.Combine(Databases.CacheFolderPath, configs.ToJsonCdbName());
    var deserializedPath = Path.Combine(Databases.CacheFolderPath, configs.CdbName());
    if (toJson)
    {
        var cards = Databases.Catalogue.All;
        using var fs = File.Create(serializedPath2);
        JsonSerializer.Serialize(fs, cards, JsonHelper.DefaultSerializerSettings);
    }
    if (fromJson)
    {
        using var fs = File.OpenRead(serializedPath);
        var deserializedCards = JsonSerializer.Deserialize<List<Card>>(fs, JsonHelper.DefaultSerializerSettings);
        if (deserializedCards is not null)
        {
            deserializedCards.RemoveAll(c => c is CardMap or CardMapData);
            
            // Add maps
            foreach (var map in Databases.MapDatabase.GetMapCards())
            {
                var exists = false;
                foreach (var (_, idx) in deserializedCards.Select((x, idx) => (x, idx))
                             .Where(x => x.x is CardMap && x.x.Id == map.Id).ToList())
                {
                    exists = true;
                    deserializedCards[idx] = map;
                }

                if (!exists)
                {
                    deserializedCards.Add(map);
                }
            }

            if (Databases.MapDatabase.GrabExtraMaps() is { } extraMaps)
            {
                foreach (var mapList in deserializedCards.OfType<CardMapList>())
                {
                    if (extraMaps.Custom is { Count: > 0 })
                    {
                        if (mapList.Custom is not null)
                        {
                            mapList.Custom.AddRange(extraMaps.Custom);
                        }
                        else
                        {
                            mapList.Custom = extraMaps.Custom;
                        }
                    }

                    if (extraMaps.Friendly is { Count: > 0 })
                    {
                        if (mapList.Friendly is not null)
                        {
                            mapList.Friendly.AddRange(extraMaps.Friendly);
                        }
                        else
                        {
                            mapList.Friendly = extraMaps.Friendly;
                        }
                    }

                    if (extraMaps.FriendlyNoob is { Count: > 0 })
                    {
                        if (mapList.FriendlyNoob is not null)
                        {
                            mapList.FriendlyNoob.AddRange(extraMaps.FriendlyNoob);
                        }
                        else
                        {
                            mapList.FriendlyNoob = extraMaps.FriendlyNoob;
                        }
                    }

                    if (extraMaps.Ranked is { Count: > 0 })
                    {
                        if (mapList.Ranked is not null)
                        {
                            mapList.Ranked.AddRange(extraMaps.Ranked);
                        }
                        else
                        {
                            mapList.Ranked = extraMaps.Ranked;
                        }
                    }
                }
            }
            
            foreach (var t in deserializedCards)
            {
                t.Key = Catalogue.Key(t.Id ?? string.Empty);
            }

            var memStream = new MemoryStream();
            var writer = new BinaryWriter(memStream);
            using var fs2 = File.Create(deserializedPath);
            writer.Write((byte)0);
            writer.WriteList(deserializedCards, Card.WriteVariant);
            var zipped = (writer.BaseStream as MemoryStream)?.GetBuffer().Zip(0);
            zipped?.CopyTo(fs2);
            zipped?.Close();
        }
    }
}

if (runServer)
{
    MasterServer? server = null;
    StatusHttpServer? statusHttpServer = null;
    MasterStatusHttpServer? masterStatusHttpServer = null;
    using var shutdownCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        shutdownCts.Cancel();
    };
    if (masterMode)
    {
        // Create a new TCP server
        server = new MasterServer(configs.MasterIp(), 28100);
        server.OptionNoDelay = true;
        server.OptionSendBufferSize = masterBufferSize;
        server.OptionReceiveBufferSize = masterBufferSize;
        
        // Start the server
        server.Start();

        if (configs.EnableMasterStatusHttp())
        {
            masterStatusHttpServer = new MasterStatusHttpServer(configs.MasterStatusHttpPrefix(), Databases.MasterServerDatabase);
            if (!masterStatusHttpServer.Start())
            {
                Console.WriteLine("Master status HTTP server failed to start; continuing without it.");
                masterStatusHttpServer = null;
            }
        }
    }

    var regionServer = new RegionServer(configs.RegionIp(), 28101);
    regionServer.OptionNoDelay = true;
    regionServer.OptionSendBufferSize = regionBufferSize;
    regionServer.OptionReceiveBufferSize = regionBufferSize;
    var regionClient = new RegionClient(configs.MasterHost(), 28100);
    regionClient.OptionNoDelay = true;
    regionClient.OptionSendBufferSize = regionClientBufferSize;
    regionClient.OptionReceiveBufferSize = regionClientBufferSize;
    var matchServer = new MatchServer(configs.RegionIp(), 28102);
    matchServer.OptionNoDelay = true;
    matchServer.OptionSendBufferSize = matchBufferSize;
    matchServer.OptionReceiveBufferSize = matchBufferSize;
    Databases.SetRegionDatabase(new RegionServerDatabase(regionServer, matchServer));

    if (configs.EnableStatusHttp())
    {
        statusHttpServer = new StatusHttpServer(configs.StatusHttpPrefix(), Databases.RegionServerDatabase);
        if (!statusHttpServer.Start())
        {
            Console.WriteLine("Status HTTP server failed to start; continuing without it.");
            statusHttpServer = null;
        }
    }
   
    regionServer.Start();
    regionClient.ConnectAsync();
    matchServer.Start();
    
    Console.WriteLine("Press Enter to stop the server or '!' to restart the server...");
    try 
    {
        // Perform text input
        while (true)
        {
            if (Databases.ConfigDatabase.DoReadline())
            {
                var line = Console.ReadLine();
                if (string.IsNullOrEmpty(line))
                    break;

                switch (line)
                {
                    // Restart the server
                    case "!":
                    {
                        Console.Write("Server restarting...");
                        server?.Restart();
                        regionServer.Restart();
                        regionClient.Disconnect();
                        regionClient.Reconnect();
                        matchServer.Restart();
                        Console.WriteLine("Done!");
                        break;
                    }
                    case "refreshCdb":
                        if (Databases.Catalogue is ServerCatalogue serverCatalogue)
                        {
                            Console.Write("Refreshing cdb...");
                            var newCardList = CatalogueCache.UpdateCatalogue(CatalogueCache.Load());
                            serverCatalogue.Replicate(newCardList);
                            var catalogueReplicator = new ServiceCatalogue(new ServerSender(regionServer));
                            catalogueReplicator.SendReplicate(newCardList);
                            Console.WriteLine("Done!");
                        }
                        break;
                }
            }
            else
            {
                shutdownCts.Token.WaitHandle.WaitOne();
                break;
            }
        }
    }
    finally
    {
        // Stop the server
        Console.Write("Server stopping...");
        server?.Stop();
        regionServer.Stop();
        regionClient.DisconnectAndStop();
        masterStatusHttpServer?.Dispose();
        statusHttpServer?.Dispose();
        if (configs.IsMaster())
        {
            Databases.MasterServerDatabase.RemoveRegionServer("master");
        }
        matchServer.Stop();
        Console.WriteLine("Done!");
    }
}
