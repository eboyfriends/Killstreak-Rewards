using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KillStreakRewards
{
    public class Reward
    {
        // Hard coding these in for now.
        // Ideally we would have a config that has a format really similar to how I'm writing these below.
        // Then we would just populate Rewards from the config.
        public static List<Reward> Rewards = [
            new() {
                Item = "weapon_smokegrenade",
                RequiredStreak = 6,
                Optional = true
            },
            new() {
                Item = "weapon_hegrenade",
                RequiredStreak = 6,
                Optional = true
            },
            new() {
                Item = "weapon_decoy",
                RequiredStreak = 6,
                Optional = true
            },
            new() {
                Item = "weapon_taser",
                RequiredStreak = 8,
                Optional = false
            },
            new() {
                Item = "weapon_flashbang",
                RequiredStreak = 12,
                Optional = false
            }
        ];
        public required string Item;
        public required int RequiredStreak;
        // IDK what else to call this. If the grenade is optional, we must compare it to the player's nadePreference before we give it to them.
        public required bool Optional;
        public void Give(CCSPlayerController player)
        {
            player.GiveNamedItem(Item);
            player.PrintToChat($"You've been awarded a {Item} for your {RequiredStreak} kill streak!");
        }
        public static int GetMaxStreak()
        {
            return Rewards.Select(reward => reward.RequiredStreak).Max();
        }
    }
}
