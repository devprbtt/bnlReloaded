# BnL Reloaded
This is the open source github repo for the bnl private server recreation project.

To run the private server, you'll need to do a couple things first.

First, you must add to the BNL launch options (which you can access by right clicking Block N Load in your steam library and selecting "Properties") something along the lines of this: ["C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad\Win64\BlockNLoad.exe" %COMMAND%] (without the square brackets). The path you put in may or may not be different depending on where the location of the BlockNLoad.exe file is on your computer.

After, you need to use something like https://github.com/dnSpy/dnSpy to edit the Assembly-CSharp.dll file in ..\Win64\BlockNLoad_Data\Managed and edit this function in LoginLogic to just "return true": https://media.discordapp.net/attachments/1400962017816215595/1401331912059912322/image.png?ex=68967b0b&is=6895298b&hm=90f5d980be3f0e152f38c7600a9c2c462bfc952b0652c1bddf852a0611a4d1d4&=&format=webp&quality=lossless&width=1923&height=402 

The next thing you have to change is the server IP. It's hardcoded to the original server's IP address: "162.55.251.122". You can use dnSpy, but I had trouble getting it to compile correctly, and so I used https://hexed.it/, uploaded the dll's contents, searched for the server's IP address, and replaced the bytes corresponding to that string with "bnlreloaded.co" (they need to be the same length). After you download the edited dll and replace the original dll in ..\Win64\BlockNLoad_Data\Managed with the new edited one, you have to go into your hosts file at C:\Windows\System32\drivers\etc\hosts and add a new entry linking 127.0.0.1 to bnlreloaded.co

The dll changes will be permanent unless you verify the integrity of the game files (or do something to cause steam to do it automatically). You should save a copy of the edited dll somewhere just in case you need to replace it again.

To use the cdb serializer/deserializer, create a folder called Cache in the base directory (should be in the same directory as the BaseTypes, Database, etc. folders). Put the cdb file from the game assets into the new Cache folder. Then change the toJson and fromJson constants to serialize/deserialize the cdb to a json file/zipped cdb file respectively.

## Build phase damage boost
- `BNLReloadedServer/ServerTypes/GameZone.cs` multiplies world damage by 33% during both build phases and deploys the new `effect_build_phase_damage_boost` onto every player so the `gameplayeffects_buildspeed` icon appears for the temporary buff.
- `BNLReloadedServer/Cache/cdb.json` now defines `effect_build_phase_damage_boost` so the client can render the icon and tooltip describing the build-phase bonus.

## Custom game tweaks
- `BNLReloadedServer/Cache/cdb.json` allows `min_build_time` of `0` and `min_respawn_time_mod` of `-1` so custom games can be configured for 0s build time and 0s respawn time.
- `BNLReloadedServer/ServerTypes/GameZone.cs` skips zero-length build timers and immediately advances phases to avoid invalid timer intervals.
- `BNLReloadedServer/Cache/cdb.json` adds a hit sound for Tony's caulk gun slow via the new `impact_engineer_caulk_gun_slow` impact entry.

# Master HTTP status service
This repo now includes a master HTTP status endpoint that aggregates data from all connected region servers (and the master itself): online players, ranked/casual queues, custom lobbies, and live match snapshots with player heroes, team, scoreboard stats, and cube status (per-team totals plus per-cube HP).

## How to use
1. Ensure `BNLReloadedServer/Configs/configs.json` exists.
2. Set `status_http_port` in `configs.json` (defaults to `8080` if omitted). Set to `0` or negative to disable the HTTP service.
3. Run the server with `is_master: true`.
4. Open `http://localhost:<status_http_port>/status` to view JSON.

## Files modified/added for the HTTP service
- `BNLReloadedServer/Servers/MasterStatusHttpServer.cs`: Kestrel HTTP server exposing `/status`.
- `BNLReloadedServer/RunServer.cs`: starts/stops the HTTP server when `is_master` is true.
- `BNLReloadedServer/BNLReloadedServer.csproj`: adds `Microsoft.AspNetCore.App` framework reference.
- `BNLReloadedServer/Database/Configs.cs`: adds `status_http_port` config field.
- `BNLReloadedServer/Database/ConfigDatabase.cs`: reads `status_http_port`.
- `BNLReloadedServer/Database/IConfigDatabase.cs`: interface update for `StatusHttpPort()`.
- `BNLReloadedServer/Database/DummyConfigDatabase.cs`: adds `StatusHttpPort()` for tests.
- `BNLReloadedServer/Status/StatusSnapshots.cs`: status DTOs for online/queue/lobby/match snapshots, including `cube_status` with per-team/per-cube health.
- `BNLReloadedServer/Database/GameInstance.cs`: builds per-match snapshots (hero, team, stats, elapsed time, cube status).
- `BNLReloadedServer/ServerTypes/GameZone.cs`: tracks match elapsed time and freezes it on match end.
- `BNLReloadedServer/ServerTypes/GameZoneUpdateFunctions.cs`: builds scoreboard stats per player (build/destroyed/earned/k/d/a), team lookup, and cube status/health.
- `BNLReloadedServer/ServerTypes/GameLobby.cs`: exposes lobby player snapshot list.
- `BNLReloadedServer/ServerTypes/Matchmaker.cs`: exposes queue snapshots safely.
- `BNLReloadedServer/Database/RegionServerDatabase.cs`: builds region snapshots (online/queues/custom lobbies/matches) and includes cube status on matches/lobbies.
- `BNLReloadedServer/Database/IRegionServerDatabase.cs`: interface update for `BuildStatusSnapshot()`.
- `BNLReloadedServer/Database/MasterServerDatabase.cs`: stores latest per-region snapshots.
- `BNLReloadedServer/Database/IMasterServerDatabase.cs`: interface update for region status snapshot methods.
- `BNLReloadedServer/Service/ServiceRegionServer.cs`: sends region snapshot payloads to master.
- `BNLReloadedServer/Service/ServiceMasterServer.cs`: receives region snapshots on master.
- `BNLReloadedServer/Service/IServiceRegionServer.cs`: interface update for `SendRegionStatus()`.
- `BNLReloadedServer/Servers/RegionClient.cs`: periodically sends snapshots to master.

# Match pause command
The HTTP service also exposes a pause endpoint that freezes match timers and blocks player actions. Use:
`POST /match/pause?instanceId=<instance_id>&paused=true|false` with Basic Auth configured in `configs.json`.

## Files modified/added for match pause
- `BNLReloadedServer/Configs/configs.json`: adds `status_http_username`/`status_http_password` for Basic Auth.
- `BNLReloadedServer/Database/Configs.cs`: adds `StatusHttpUsername`/`StatusHttpPassword` config fields.
- `BNLReloadedServer/Database/ConfigDatabase.cs`: reads the new HTTP auth fields.
- `BNLReloadedServer/Database/IConfigDatabase.cs`: interface update for HTTP auth accessors.
- `BNLReloadedServer/Database/DummyConfigDatabase.cs`: adds HTTP auth accessors for tests.
- `BNLReloadedServer/Database/IRegionServerDatabase.cs`: adds `GetGameInstanceById()` lookup.
- `BNLReloadedServer/Database/RegionServerDatabase.cs`: implements `GetGameInstanceById()` and adds `instance_id` to custom lobby snapshots.
- `BNLReloadedServer/Database/IGameInstance.cs`: adds pause getter/setter.
- `BNLReloadedServer/Database/GameInstance.cs`: exposes pause control to the zone.
- `BNLReloadedServer/Servers/MasterStatusHttpServer.cs`: adds `/match/pause` endpoint and Basic Auth.
- `BNLReloadedServer/Status/StatusSnapshots.cs`: adds `instance_id` to lobby snapshots for pause targeting.
- `BNLReloadedServer/ServerTypes/GameZone.cs`: core pause state, timer freezing, time shifting, and pause corrections.
- `BNLReloadedServer/ServerTypes/BlockIntervalUpdater.cs`: shifts interval effects when paused.
- `BNLReloadedServer/ServerTypes/GameZoneUpdateFunctions.cs`: skips instant effects during pause.
- `BNLReloadedServer/ServerTypes/GameZoneUserInteraction.cs`: blocks player inputs during pause and snaps movement.
