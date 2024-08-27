using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KillStreakRewards
{
    public class PlayerKillstreakInfo
    {
        public required CCSPlayerController player;
        public int Killstreak = 0;
        public int PreviousKillstreak = 0;
        public bool ResetStreakPending = false;
        private string? _nadePreference;
        public void Reset()
        {
            Killstreak = 0;
            PreviousKillstreak = 0;
            ResetStreakPending = false;
        }
        public void HandleRewards()
        {
            foreach (Reward reward in GetPendingRewards()) {
                reward.Give(player);
            }
            // After giving rewards, set PreviousKillstreak = Killstreak
            PreviousKillstreak = Killstreak;
            // If the player had died, reset their stats.
            if (ResetStreakPending)
            {
                Reset();
            }
        }
        public bool HasItem(string item)
        {
            var weapons = player?.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
            if (weapons != null) return weapons.Any(weapon => weapon.IsValid && weapon?.Value?.DesignerName == item);
            return false;
        }
        public bool CanReceiveReward(Reward reward)
        {
            int maxStreak = Reward.GetMaxStreak();
            // If the item is optional and NOT the player's preference, don't give it to them.
            if (reward.Optional && !reward.Item.Equals(GetNadePreference()))
            {
                return false;
            }
            // If the player's actual killstreak equals 0, don't give them anything (otherwise, because we're doing % 12, it will never give them the flashbang (12 == 0).
            if (Killstreak == 0)
            {
                return false;
            }
            // If the current streak isn't high enough, don't give it to them.
            if (Killstreak % maxStreak < reward.RequiredStreak % maxStreak)
            {
                return false;
            }
            // If the previous streak is greater/equal to the required streak, the player has already received it.
            if (PreviousKillstreak % maxStreak >= reward.RequiredStreak % maxStreak)
            {
                return false;
            }
            // If the player already has the reward, don't give it to them.
            if (HasItem(reward.Item))
            {
                return false;
            }
            return true;
        }
        public IEnumerable<Reward> GetPendingRewards()
        {
            return Reward.Rewards.Where(CanReceiveReward);
        }
        public void SetNadePreference(string nadePreference)
        {
            _nadePreference = nadePreference;
        }
        public string GetNadePreference()
        {
            return _nadePreference ?? "weapon_smokegrenade";
        }
    }
}
