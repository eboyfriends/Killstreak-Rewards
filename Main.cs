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
        private Dictionary<CCSPlayerController, (int KillStreak, int HighestRewardGiven, int PreviousRoundKillStreak)> PlayerStats = new();

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
            Server.NextFrame(() => {
                foreach (var player in PlayerStats.Keys.ToList()) {
                    HandleRewards(player);
                }
            });

            return HookResult.Continue;
        }
        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info) {
            if (@event.Attacker == null || @event.Userid == null) {
                return HookResult.Continue;
            }

            CCSPlayerController Player = @event.Userid;
            CCSPlayerController Attacker = @event.Attacker;

            if (PlayerStats.TryGetValue(Attacker, out var attackerStats)) {
                PlayerStats[Attacker] = (attackerStats.KillStreak + 1, attackerStats.HighestRewardGiven, attackerStats.PreviousRoundKillStreak);
            }
            else {
                PlayerStats[Attacker] = (1, 0, 0);
            }

            if (PlayerStats.TryGetValue(Player, out var playerStats)) {
                PlayerStats[Player] = (0, 0, playerStats.KillStreak); 
            }

            return HookResult.Continue;
        }

        private void HandleRewards(CCSPlayerController playerController) {
            if (!PlayerStats.TryGetValue(playerController, out var stats)) return;

            var steamId = playerController.AuthorizedSteamID?.SteamId64;
            if (steamId == null) return;

            int killStreak = stats.KillStreak;
            int highestRewardGiven = stats.HighestRewardGiven;
            int PreviousRoundKillStreak = stats.PreviousRoundKillStreak;

            if (PreviousRoundKillStreak >= 6 && highestRewardGiven < 1) {
                Task.Run(async () => {
                    var selectedGrenade = await GetSelectedGrenade(steamId.Value);
                    if (!string.IsNullOrEmpty(selectedGrenade)) {
                        Server.NextFrame(() => {
                            if (!HasItem(playerController, "weapon_" + selectedGrenade)) {
                                GiveReward(playerController, "weapon_" + selectedGrenade, 6);
                                PlayerStats[playerController] = (killStreak, 1, PreviousRoundKillStreak);
                            }
                        });
                    }
                });
            }
            if (PreviousRoundKillStreak >= 8 && highestRewardGiven < 2 && !HasItem(playerController, "weapon_taser")) {
                GiveReward(playerController, "weapon_taser", 8);
                PlayerStats[playerController] = (killStreak, 2, PreviousRoundKillStreak);
            }
            if (PreviousRoundKillStreak >= 12 && highestRewardGiven < 3 && !HasItem(playerController, "weapon_flashbang")) {
                GiveReward(playerController, "weapon_flashbang", 12);
                PlayerStats[playerController] = (killStreak, 3, PreviousRoundKillStreak);
            }
        }
        private async Task<string> GetSelectedGrenade(ulong steamId) {
            var result = await _connection.QueryFirstOrDefaultAsync<string>($@"
                SELECT `SelectedGrenade` FROM `{_tableName}` WHERE `steamid` = @SteamId;",
                new { SteamId = steamId });

            return result ?? "smokegrenade"; // Default to smoke grenade if not set
        }

        private void GiveReward(CCSPlayerController player, string reward, int killStreak) {
            Server.NextFrame(() => {
                player.GiveNamedItem(reward);
                player.PrintToChat($"You've been awarded a {reward} for your {killStreak} kill streak!");
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

            if (PlayerStats.TryGetValue(player, out var stats)) {
                player.PrintToChat($"Your current kill streak is: {stats.KillStreak}");
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

        private static bool HasItem(CCSPlayerController player, string itemName) {
            var weapons = player?.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
            if (weapons != null) return weapons.Any(weapon => weapon.IsValid && weapon?.Value?.DesignerName == itemName);
            return false;
        }
    }
}

