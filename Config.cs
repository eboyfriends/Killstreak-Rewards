using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using MySqlConnector;

namespace KillStreakRewards{
    public class MainConfig : IBasePluginConfig {
        public int Version { get; set; } = 2;
        [JsonPropertyName("DatabaseConfig")]
        public DatabaseConfig DatabaseConfig { get; set; } = new DatabaseConfig();
        // Haven't figured out why these don't get populated...
        // It creates them as "{}", without writing their properties.
        // So I had to manually add them to the config.
        [JsonPropertyName("Rewards")]
        public List<Reward>? AllRewards { get; set; } = [
            new Reward() { Item = "weapon_smokegrenade", RequiredStreak = 6, Optional = true, Shortname = "smoke" },
            new Reward() { Item = "weapon_hegrenade", RequiredStreak = 6, Optional = true, Shortname = "he" },
            new Reward() { Item = "weapon_decoy", RequiredStreak = 6, Optional = true, Shortname = "decoy" },
            new Reward() { Item = "weapon_taser", RequiredStreak = 8, Optional = false, Shortname = "taser" },
            new Reward() { Item = "weapon_decoy", RequiredStreak = 12, Optional = false, Shortname = "flash" }
        ];

        // Only exists because I don't want to pass the Config as a parameter to some functions.
        public static List<Reward>? Rewards;
    }

    public class DatabaseConfig {
        [JsonPropertyName("host")]
		public string Host { get; set; } = "localhost";

		[JsonPropertyName("username")]
		public string Username { get; set; } = "root";

		[JsonPropertyName("database")]
		public string Database { get; set; } = "database";

		[JsonPropertyName("password")]
		public string Password { get; set; } = "password";

		[JsonPropertyName("port")]
		public int Port { get; set; } = 3306;
        
		[JsonPropertyName("table")]
		public string Table { get; set; } = "KillStreakRewards";

		[JsonPropertyName("sslmode")]
		public string Sslmode { get; set; } = "none";

        public MySqlConnection CreateConnection() {
            MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
            {
                Server = Host,
                UserID = Username,
                Password = Password,
                Database = Database,
                Port = (uint)Port,
                SslMode = Enum.Parse<MySqlSslMode>(Sslmode, true),
            };

            return new MySqlConnection(builder.ToString());
        }
    }
}

