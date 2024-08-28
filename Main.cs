using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace KillStreakRewards {

    public class Main : BasePlugin, IPluginConfig<MainConfig>
    {
        // Changed modulename to match DLL so I can refer to ModuleName in config folder.
        public override string ModuleName => "KillStreakRewards";
        public override string ModuleVersion => "7.7.7";
        public override string ModuleAuthor => "eboyfriends";
        private MySqlConnection _connection = null!;
        public required MainConfig Config { get; set; }
        private string _tableName = string.Empty;
        private Dictionary<ulong, PlayerKillstreakInfo> PlayerStats = new();

        public override void Load(bool hotReload)  {
            Logger.LogInformation("We are loading KillStreakRewards!");

            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
            RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

            bool late = !string.IsNullOrEmpty(Server.MapName);

            // Load happens AFTER OnConfigParsed (so I've learned after wasting a lot of time debugging >_>.
            // OFC, this is my fault for moving stuff there in the first place).
            _connection = Config.DatabaseConfig.CreateConnection();
            _connection.Open();

            List<ulong>? steamIDs = null;
            if (late)
            {
                steamIDs = Utilities.GetPlayers().Select(player => player.SteamID).ToList();
            }

            Task.Run(async () =>
            {
                await _connection.ExecuteAsync($@"
                    CREATE TABLE IF NOT EXISTS `{_tableName}` (
                        `steamid` BIGINT UNSIGNED NOT NULL,
                        `SelectedGrenade` VARCHAR(255) NOT NULL DEFAULT 'weapon_smokegrenade',
                        PRIMARY KEY (`steamid`));");

                // If plugin is late loaded (Server.MapName != null), grab everyone's preferences.
                if (late)
                {
                    var results = await _connection.QueryAsync<(ulong steamID, string grenade)>(
                    $"SELECT `steamid` as steamID, `SelectedGrenade` as grenade FROM `{_tableName}` WHERE `steamid` IN @SteamIds;",
                    new { SteamIds = steamIDs! });
                    foreach ((ulong steamID, string grenade) in results)
                    {
                        PlayerKillstreakInfo stats = new();
                        stats.SetNadePreference(grenade);
                        PlayerStats.Add(steamID, stats);
                    }
                }
            });
        }

        public override void Unload(bool hotReload) {
            Logger.LogInformation("We are unloading KillStreakRewards!");

            DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
            DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);

            _connection?.Dispose();
        }

        public void OnConfigParsed(MainConfig config) {
            // CounterStrikeSharp uses a cached config, it doesn't deserialize it again on plugin unload/reload.
            ReloadConfig();
            MainConfig.Rewards = Config.AllRewards;
            _tableName = Config.DatabaseConfig.Table;
        }
        public void ReloadConfig()
        {
            string path = Path.Join(ModuleDirectory, $"../../configs/plugins/{ModuleName}/{ModuleName}.json");
            if (!File.Exists(path))
            {
                Logger.LogError("{message}", "Missing config!");
                return;
            }
            string fullText = File.ReadAllText(path);
            IEnumerable<string> splitText = fullText.Split('\n').Skip(1);
            string jsonText = string.Join('\n', splitText);
            MainConfig? config = JsonSerializer.Deserialize<MainConfig>(jsonText);
            if (config == null)
            {
                Logger.LogError("{message}", "Failed to deserialize config!");
                return;
            }
            Config = config;
        }
        private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info) {
            var player = @event.Userid;
            if (player == null) {
                return HookResult.Continue;
            }
            if (PlayerStats.TryGetValue(player.SteamID, out var stats))
            Server.NextFrame(() => {
                Reward.HandleRewards(player, stats);
            });

            return HookResult.Continue;
        }
        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info) {
            if (@event.Attacker == null || @event.Userid == null) {
                return HookResult.Continue;
            }

            CCSPlayerController Victim = @event.Userid;
            CCSPlayerController Attacker = @event.Attacker;

            if (PlayerStats.TryGetValue(Attacker.SteamID, out var attackerStats)) {
                attackerStats.Killstreak++;
            }
            else {
                PlayerStats.Add(Attacker.SteamID, new() { Killstreak = 1 });
            }

            if (PlayerStats.TryGetValue(Victim.SteamID, out var victimStats)) {
                victimStats.ResetStreakPending = true; 
            }

            return HookResult.Continue;
        }
        private void OnClientAuthorized(int playerSlot, SteamID steamId)
        {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player == null) return;

            Task.Run(async () =>
            {
                try
                {
                    var result = await _connection.QueryFirstOrDefaultAsync<string>(
                        $"SELECT `SelectedGrenade` FROM {_tableName} WHERE `steamid` = @SteamId;",
                        new { SteamId = steamId.SteamId64 });

                    if (result != null)
                    {
                        Server.NextFrame(() => {
                            player.PrintToChat($"Your default grenade is set to: {result}");
                            // Cache their nade preference, so we don't need to do a database query every time...
                            PlayerKillstreakInfo stats = new();
                            stats.SetNadePreference(result);
                            PlayerStats.Add(player.SteamID, stats);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("{message}", ex.Message);
                }
            });
        }

        private void OnClientDisconnect(int playerSlot) {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player == null) return;
            PlayerStats.Remove(player.SteamID);
        }

        [ConsoleCommand("css_streak", "check current kill streak")]
        public void OnStreakCommand(CCSPlayerController? player, CommandInfo commandInfo) {
            if (player == null) return;

            if (PlayerStats.TryGetValue(player.SteamID, out var stats)) {
                player.PrintToChat($"Your current kill streak is: {stats.Killstreak}");
            }
            else {
                player.PrintToChat("You don't have an active kill streak.");
            }
        }

        [ConsoleCommand("css_nade", "change default nade reward")]
        public void OnNadeCommand(CCSPlayerController? player, CommandInfo commandInfo) {
            if (player == null) return;

            var steamId = player.AuthorizedSteamID?.SteamId64;
            if (steamId == null)
            {
                Logger.LogError("{message}", $"Failed to find SteamID for player: {player.PlayerName}");
                return;
            }
            if (Config.AllRewards == null)
            {
                Logger.LogError("{message}", "Missing Rewards in Config");
                return;
            }
            PlayerStats.TryGetValue(player.SteamID, out var stats);

            var value = commandInfo.GetArg(1);
            if (string.IsNullOrEmpty(value) && stats != null)
            {
                player.PrintToChat($"Your default grenade reward is: \"{stats.GetNadePreference()}\".");
                return;
            }

            var reward = Config.AllRewards.Where(reward => value.Equals(reward.Shortname)).FirstOrDefault();
            if (reward == null)
            {
                var cleanedRewards = Config.AllRewards.Where(reward => reward.Optional).Select(reward => reward.Shortname.ToLower());
                player.PrintToChat($"Valid types are: {string.Join(", ", cleanedRewards)}");
                return;
            }
            if (stats != null)
            {
                stats.SetNadePreference(reward.Item);
                Task.Run(async () =>
                {
                    try
                    {
                        await _connection.ExecuteAsync($@"
                    INSERT INTO `{_tableName}` (`steamid`, `SelectedGrenade`) 
                    VALUES (@SteamId, @Value)
                    ON DUPLICATE KEY UPDATE `SelectedGrenade` = @Value;",
                        new
                        {
                            SteamId = steamId,
                            Value = reward.Item
                        });

                        Server.NextFrame(() =>
                        {
                            player.PrintToChat($"Changed default grenade to {reward.Item}");
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("{message}", ex.Message);
                    }
                });
            }
        }
    }
}

