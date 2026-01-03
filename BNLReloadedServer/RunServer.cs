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

const int bufferSize = 2000000;  // 2MB

List<Card>? LoadJsonCatalogue(string jsonPath)
{
    if (!File.Exists(jsonPath))
    {
        return null;
    }

    using var fs = new StreamReader(File.OpenRead(jsonPath));
    var deserializedCards = JsonSerializer.Deserialize<List<Card>>(fs.ReadToEnd(), JsonHelper.DefaultSerializerSettings);
    if (deserializedCards is null)
    {
        return null;
    }

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

    return deserializedCards;
}

byte[] BuildCdbBytes(ICollection<Card> cards)
{
    var memStream = new MemoryStream();
    var writer = new BinaryWriter(memStream);
    writer.Write((byte)0);
    writer.WriteList(cards, Card.WriteVariant);
    using var zipped = memStream.GetBuffer().Zip(0);
    return zipped.ToArray();
}

if (toJson || fromJson)
{
    var serializedPath = Path.Combine(Databases.CacheFolderPath, configs.FromJsonCdbName());
    var serializedPath2 = Path.Combine(Databases.CacheFolderPath, configs.ToJsonCdbName());
    var deserializedPath = Path.Combine(Databases.CacheFolderPath, configs.CdbName());
    if (toJson)
    {
        var cards = Databases.Catalogue.All;
        using var fs = new StreamWriter(File.Create(serializedPath2));
        fs.Write(JsonSerializer.Serialize(cards, JsonHelper.DefaultSerializerSettings).Replace("\\u00A0", "\u00A0"));
    }
    if (fromJson)
    {
        var deserializedCards = LoadJsonCatalogue(serializedPath);
        if (deserializedCards is not null)
        {
            File.WriteAllBytes(deserializedPath, BuildCdbBytes(deserializedCards));
        }
    }
}

if (runServer)
{
    MasterServer? server = null;
    MasterStatusHttpServer? statusServer = null;
    if (masterMode)
    {
        // Create a new TCP server
        server = new MasterServer(configs.MasterIp(), 28100);
        server.OptionSendBufferSize = bufferSize;
        server.OptionReceiveBufferSize = bufferSize;
        
        // Start the server
        server.Start();

        var statusPort = configs.StatusHttpPort();
        if (statusPort > 0)
        {
            statusServer = new MasterStatusHttpServer(statusPort);
            statusServer.Start();
        }
    }

    var regionServer = new RegionServer(configs.RegionIp(), 28101);
    regionServer.OptionNoDelay = true;
    regionServer.OptionSendBufferSize = bufferSize;
    regionServer.OptionReceiveBufferSize = bufferSize;
    var regionClient = new RegionClient(configs.MasterHost(), 28100);
    regionClient.OptionNoDelay = true;
    regionClient.OptionSendBufferSize = bufferSize;
    regionClient.OptionReceiveBufferSize = bufferSize;
    var matchServer = new MatchServer(configs.RegionIp(), 28102);
    matchServer.OptionNoDelay = true;
    matchServer.OptionSendBufferSize = bufferSize;
    matchServer.OptionReceiveBufferSize = bufferSize;
    Databases.SetRegionDatabase(new RegionServerDatabase(regionServer, matchServer));
   
    regionServer.Start();
    regionClient.ConnectAsync();
    matchServer.Start();
    
    FileSystemWatcher? jsonWatcher = null;
    Timer? jsonReloadTimer = null;
    string? jsonPath = null;

    if (toJson)
    {
        jsonPath = Path.Combine(Databases.CacheFolderPath, configs.ToJsonCdbName());
    }
    else if (fromJson)
    {
        jsonPath = Path.Combine(Databases.CacheFolderPath, configs.FromJsonCdbName());
    }

    if (!string.IsNullOrWhiteSpace(jsonPath))
    {
        void ApplyCatalogueFromJson()
        {
            if (Databases.Catalogue is not ServerCatalogue serverCatalogue) return;

            List<Card>? cards = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    cards = LoadJsonCatalogue(jsonPath);
                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(200);
                }
            }

            if (cards is null) return;

            CatalogueCache.Save(BuildCdbBytes(cards));
            serverCatalogue.Replicate(cards);
            var catalogueReplicator = new ServiceCatalogue(new ServerSender(regionServer));
            catalogueReplicator.SendReplicate(cards);
            Console.WriteLine($"Applied JSON catalogue changes from {jsonPath}");
        }

        jsonReloadTimer = new Timer(_ => ApplyCatalogueFromJson(), null, Timeout.Infinite, Timeout.Infinite);
        jsonWatcher = new FileSystemWatcher(Path.GetDirectoryName(jsonPath)!, Path.GetFileName(jsonPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        FileSystemEventHandler onChange = (_, _) => jsonReloadTimer.Change(500, Timeout.Infinite);
        jsonWatcher.Changed += onChange;
        jsonWatcher.Created += onChange;
        jsonWatcher.Renamed += (_, _) => jsonReloadTimer.Change(500, Timeout.Infinite);
        jsonWatcher.EnableRaisingEvents = true;
    }

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
                Task.Delay(Timeout.InfiniteTimeSpan).Wait();
            }
        }
    }
    finally
    {
        // Stop the server
        Console.Write("Server stopping...");
        jsonWatcher?.Dispose();
        jsonReloadTimer?.Dispose();
        server?.Stop();
        statusServer?.StopAsync().GetAwaiter().GetResult();
        regionServer.Stop();
        regionClient.DisconnectAndStop();
        if (configs.IsMaster())
        {
            Databases.MasterServerDatabase.RemoveRegionServer("master");
        }
        matchServer.Stop();
        Console.WriteLine("Done!");
    }
}
