using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace KillStreakRewards {

    public class Main : BasePlugin, IPluginConfig<MainConfig>
    {
        public override string ModuleName => "Killstreak-Rewards";
        public override string ModuleVersion => "6.6.6";
        public override string ModuleAuthor => "eboyfriends";
        private MySqlConnection _connection = null!;
        public required MainConfig Config { get; set; }
        private string _tableName = string.Empty;
        private List<PlayerKillstreakInfo> PlayerStats = new();

        public override void Load(bool hotReload)  {
            Logger.LogInformation("We are loading KillStreakRewards!");

            _connection = Config.DatabaseConfig.CreateConnection();
            _connection.Open();
            _tableName = Config.DatabaseConfig.Table;

            Task.Run(async () =>
            {
                await _connection.ExecuteAsync($@"
                    CREATE TABLE IF NOT EXISTS `{_tableName}` (
                        `steamid` BIGINT UNSIGNED NOT NULL,
                        `SelectedGrenade` VARCHAR(255) NOT NULL DEFAULT 'weapon_smokegrenade',
                        PRIMARY KEY (`steamid`));");
            });

            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect, HookMode.Post);
            RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }

        public override void Unload(bool hotReload) {
            Logger.LogInformation("We are unloading KillStreakRewards!");

            DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
            DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect, HookMode.Post);
            DeregisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);

            _connection?.Dispose();
        }

        public void OnConfigParsed(MainConfig config) {
            Config = config;
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info) {
            // Maybe EventPlayerSpawn instead?
            Server.NextFrame(() => {
                foreach (var stats in PlayerStats) {
                    stats.HandleRewards();
                }
            });

            return HookResult.Continue;
        }
        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info) {
            if (@event.Attacker == null || @event.Userid == null) {
                return HookResult.Continue;
            }

            CCSPlayerController Victim = @event.Userid;
            CCSPlayerController Attacker = @event.Attacker;

            PlayerKillstreakInfo? attackerStats = GetPlayerKillstreakInfo(Attacker);
            if (attackerStats != null) {
                attackerStats.Killstreak++;
            }
            else {
                PlayerStats.Add(new() { player = Attacker });
            }

            PlayerKillstreakInfo? victimStats = GetPlayerKillstreakInfo(Victim);
            if (victimStats != null) {
                victimStats.ResetStreakPending = true; 
            }

            return HookResult.Continue;
        }
        private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info) {
            var player = @event.Userid;
            if (player == null) return HookResult.Continue;

            var steamId = player.AuthorizedSteamID?.SteamId64;
            if (steamId == null) return HookResult.Continue;

            Task.Run(async () =>
            {
                var result = await _connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT `SelectedGrenade` FROM `players` WHERE `steamid` = @SteamId;",
                    new { SteamId = steamId });

                if (result != null) {
                    Server.NextFrame(() => {
                        player.PrintToChat($"Your default grenade is set to: {result}");
                        // Cache their nade preference, so we don't need to do a database query every time...
                        PlayerKillstreakInfo? stats = PlayerStats.Where(stats => stats.player == player).FirstOrDefault();
                        if (stats == null) return;
                        stats.SetNadePreference(result);
                    });
                }
            });

            return HookResult.Continue;
        }

        private void OnClientDisconnect(int playerSlot) {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            PlayerKillstreakInfo? stats = PlayerStats.Where(stats => stats.player == player).FirstOrDefault();
            if (stats != null) {
                PlayerStats.Remove(stats);
            }
        }

        [ConsoleCommand("css_streak", "check current kill streak")]
        public void OnStreakCommand(CCSPlayerController? player, CommandInfo commandInfo) {
            if (player == null) return;

            PlayerKillstreakInfo? stats = GetPlayerKillstreakInfo(player);

            if (stats != null) {
                player.PrintToChat($"Your current kill streak is: {stats.Killstreak}");
            }
            else {
                player.PrintToChat("You don't have an active kill streak.");
            }
        }

        [ConsoleCommand("css_nade", "change default nade reward")]
        public void OnNadeCommand(CCSPlayerController? player, CommandInfo commandInfo) {
            if (player == null) return;

            var steamId = player.AuthorizedSteamID?.SteamId64; if (steamId == null) return;
            var value = commandInfo.GetArg(1);
            
            if (value == "smoke") value = "smokegrenade";
            if (value == "he") value = "hegrenade";

            var cleanedRewards = Config.GrenadeRewards.Select(reward => Regex.Replace(reward, "grenade", "", RegexOptions.IgnoreCase)).ToList();

            if (!IsValidReward(value))  {
                player.PrintToChat($"Valid types are: {string.Join(", ", cleanedRewards)}");
                return;
            }

            Task.Run(async () =>
            {
                await _connection.ExecuteAsync($@"
                    INSERT INTO `{_tableName}` (`steamid`, `SelectedGrenade`) 
                    VALUES (@SteamId, @Value)
                    ON DUPLICATE KEY UPDATE `SelectedGrenade` = @Value;",
                    new
                    {
                        SteamId = steamId,
                        Value = value
                    });

                Server.NextFrame(() =>
                {
                    player.PrintToChat($"Changed default grenade to {value}");
                });
            });
        }

        public bool IsValidReward(string name) {
            return Config.GrenadeRewards.Contains(name);
        }
        public PlayerKillstreakInfo? GetPlayerKillstreakInfo(CCSPlayerController player)
        {
            return PlayerStats.Where(stats => stats.player == player).FirstOrDefault();
        }
    }
}

