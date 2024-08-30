using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Numerics;

namespace KillStreakRewards
{
    public partial class Main
    {
        public class Queries(string tableName)
        {
            public string CreateTable = $@"
            CREATE TABLE IF NOT EXISTS `{tableName}` (
                `steamid` BIGINT UNSIGNED NOT NULL,
                `SelectedGrenade` VARCHAR(255) NOT NULL DEFAULT 'weapon_smokegrenade',
                PRIMARY KEY (`steamid`));";

            public string InsertNadePreference = $@"
            INSERT INTO `{tableName}` (`steamid`, `SelectedGrenade`) 
            VALUES (@SteamId, @Value)
            ON DUPLICATE KEY
            UPDATE `SelectedGrenade` = @Value;";

            public string SelectNadePreference = $@"
            SELECT `SelectedGrenade`
            FROM {tableName}
            WHERE `steamid` = @SteamId;";

            public string SelectAllNadePreferences = $@"
            SELECT `steamid` as steamID, `SelectedGrenade` as grenade
            FROM `{tableName}`
            WHERE `steamid` IN @SteamIds;";
        }

        public async Task CreateTableAsync(IEnumerable<ulong> steamIDs)
        {
            try
            {
                using MySqlConnection connection = new(_connectionString);
                await connection.OpenAsync();
                MySqlTransaction transaction = await connection.BeginTransactionAsync();
                await connection.ExecuteAsync(_queries.CreateTable, transaction: transaction);
                await transaction.CommitAsync();
                // If plugin is late loaded (Server.MapName != null), grab everyone's preferences.
                if (_late)
                {
                    await SelectAllNadePreferencesAsync(connection, steamIDs!);
                }
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => Logger.LogError("{message}", $"CreateTable Error: {ex.Message}"));
            }
        }
        public async Task SelectAllNadePreferencesAsync(MySqlConnection connection, IEnumerable<ulong> steamIDs)
        {
            try
            {
                MySqlTransaction transaction = await connection.BeginTransactionAsync();
                var results = await connection.QueryAsync<(ulong steamID, string grenade)>(
                    _queries.SelectAllNadePreferences,
                    new { SteamIds = steamIDs },
                    transaction: transaction);
                foreach ((ulong steamID, string grenade) in results)
                {
                    PlayerKillstreakInfo stats = new();
                    stats.SetNadePreference(grenade);
                    PlayerStats.Add(steamID, stats);
                }
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => Logger.LogError("{message}", $"SelectAllNadePreferences Error: {ex.Message}"));
            }
        }

        public async Task SelectNadePreferenceAsync(CCSPlayerController player, ulong steamID)
        {
            try
            {
                using MySqlConnection connection = new(_connectionString);
                await connection.OpenAsync();
                MySqlTransaction transaction = await connection.BeginTransactionAsync();
                var result = await connection.QueryFirstOrDefaultAsync<string>(
                    _queries.SelectNadePreference,
                    new { SteamId = steamID },
                    transaction: transaction);

                if (result != null)
                {
                    // Cache their nade preference, so we don't need to do a database query every time...
                    PlayerKillstreakInfo stats = new();
                    stats.SetNadePreference(result);
                    PlayerStats.Add(steamID, stats);
                    Server.NextFrame(() => player.PrintToChat($"Your default grenade is set to: {result}"));
                }
                else
                {
                    Server.NextFrame(() => Logger.LogError("{message}", "SelectNadePreference Query Failed!"));
                }
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => Logger.LogError("{message}", $"SelectNadePreference Error: {ex.Message}"));
            }
        }
        public async Task InsertNadePreferenceAsync(CCSPlayerController player, ulong steamID, string item)
        {
            try
            {
                using MySqlConnection connection = new(_connectionString);
                await connection.OpenAsync();
                MySqlTransaction transaction = await connection.BeginTransactionAsync();
                await connection.ExecuteAsync(_queries.InsertNadePreference,
                new
                {
                    SteamId = steamID,
                    Value = item
                },
                transaction: transaction);
                await transaction.CommitAsync();
                Server.NextFrame(() => player.PrintToChat($"Changed default grenade to {item}"));
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => Logger.LogError("{message}", $"InsertNadePreference Error: {ex.Message}"));
            }
        }
    }
}
