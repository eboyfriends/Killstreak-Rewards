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
        public override string ModuleName => "eboyfriends";
        public override string ModuleVersion => "6.6.6";
        public override string ModuleAuthor => "eboyfriends";
        private MySqlConnection _connection = null!;
        public required MainConfig Config { get; set; }
        private string _tableName = string.Empty;
        private Dictionary<CCSPlayerController, int> PlayerStats = new(); // CCSPlayerController, KillStreak

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
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }

        public override void Unload(bool hotReload) {
            Logger.LogInformation("We are unloading KillStreakRewards!");

            DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
            DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect, HookMode.Post);

            _connection?.Dispose();
        }

        public void OnConfigParsed(MainConfig config) {
            Config = config;
        }

        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info) {
            if (@event.Attacker == null || @event.Userid == null || @event.Attacker == @event.Userid) {
                return HookResult.Continue;
            }

            CCSPlayerController Player = @event.Userid;
            CCSPlayerController Attacker = @event.Attacker;

            if (PlayerStats.TryGetValue(Attacker, out int value)) {
                PlayerStats[Attacker] = ++value;
            }
            else {
                PlayerStats[Attacker] = 1;
            }

            if (PlayerStats.ContainsKey(Player)) {
                PlayerStats[Player] = 0;
            }

           
            AddTimer(0.1f, async () => await HandleRewards(Attacker));

            return HookResult.Continue;
        }

        private async Task HandleRewards(CCSPlayerController playerController) {
            if (!PlayerStats.TryGetValue(playerController, out int killStreak)) return;

            var steamId = playerController.AuthorizedSteamID?.SteamId64;
            if (steamId == null) return;

            if (killStreak == 6) {
                var selectedGrenade = await GetSelectedGrenade(steamId.Value);
                if (!string.IsNullOrEmpty(selectedGrenade)) {
                    Server.NextFrame(() => GiveReward(playerController, "weapon_" + selectedGrenade));
                }
            }
            else if (killStreak == 8) {
                Server.NextFrame(() => GiveReward(playerController, "weapon_taser"));
            }
            else if (killStreak == 12) {
                Server.NextFrame(() => GiveReward(playerController, "weapon_flashbang"));
            }
        }

        private async Task<string> GetSelectedGrenade(ulong steamId) {
            var result = await _connection.QueryFirstOrDefaultAsync<string>($@"
                SELECT `SelectedGrenade` FROM `{_tableName}` WHERE `steamid` = @SteamId;",
                new { SteamId = steamId });

            return result ?? "smokegrenade"; // Default to smoke grenade if not set
        }

        private void GiveReward(CCSPlayerController player, string reward) {
            Server.NextFrame(() => {
                player.GiveNamedItem(reward);
                player.PrintToChat($"You've been awarded a {reward} for your {PlayerStats[player]} kill streak!");
            });
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
                    });
                }
            });

            return HookResult.Continue;
        }

        private void OnClientDisconnect(int playerSlot) {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player != null) {
                PlayerStats.Remove(player);
            }
        }

        [ConsoleCommand("css_streak", "check current kill streak")]
        public void OnStreakCommand(CCSPlayerController? player, CommandInfo commandInfo) {
            if (player == null) return;

            if (PlayerStats.TryGetValue(player, out int killStreak)) {
                player.PrintToChat($"Your current kill streak is: {killStreak}");
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
    }
}

