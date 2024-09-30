using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace KillStreakRewards
{
    public class Reward
    {
        [JsonPropertyName("Item")]
        public required string Item { get; set; }
        [JsonPropertyName("RequiredStreak")]
        public required int RequiredStreak { get; set; }
        // IDK what else to call this. If the grenade is optional, we must compare it to the player's nadePreference before we give it to them.
        [JsonPropertyName("Optional")]
        public required bool Optional { get; set; }
        [JsonPropertyName("Shortname")]
        public required string Shortname { get; set; }
        public void Give(CCSPlayerController player)
        {
            if (HasItem(player, Item))
            {
                player.PrintToChat($"You got a {RequiredStreak} kill streak, but you already had the reward {Item} in your inventory!");
                return;
            }
            player.GiveNamedItem(Item);
            player.PrintToChat($"You've been awarded a {Item} for your {RequiredStreak} kill streak!");
        }
        public static bool HasItem(CCSPlayerController player, string item)
        {
            var weapons = player?.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
            if (weapons != null) return weapons.Any(weapon => weapon.IsValid && weapon?.Value?.DesignerName == item);
            return false;
        }
        public static int GetMaxStreak()
        {
            return MainConfig.Rewards!.Select(reward => reward.RequiredStreak).Max();
        }
        public static void HandleRewards(CCSPlayerController player, PlayerKillstreakInfo stats)
        {
            foreach (Reward reward in stats.GetPendingRewards())
            {
                reward.Give(player);
            }
            // After giving rewards, set PreviousKillstreak = Killstreak
            stats.PreviousKillstreak = stats.Killstreak;
            // If the player had died, reset their stats.
            if (stats.ResetStreakPending)
            {
                stats.Reset();
            }
        }
    }
}