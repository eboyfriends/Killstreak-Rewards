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
        public bool CanReceiveReward(Reward reward)
        {
            int maxStreak = Reward.GetMaxStreak();
            int lastRoundKills = Killstreak - PreviousKillstreak;
            int roundedPreviousStreak = PreviousKillstreak % maxStreak;
            int threshold = reward.RequiredStreak % maxStreak;
            // If the item is optional and NOT the player's preference, don't give it to them.
            if (reward.Optional && !reward.Item.Equals(GetNadePreference()))
            {
                return false;
            }
            // If the player's actual killstreak equals 0, don't give them anything.
            // Reason: We don't want to reward players with the maxStreak when they have 0 kills...
            // (maxStreak % maxStreak == 0) 
            if (Killstreak == 0)
            {
                return false;
            }
            // If their previous streak was not high enough,
            // and the amount of kills they got this round does not let them surpass the threshold for this reward,
            // don't give them anything.
            if (roundedPreviousStreak < threshold && roundedPreviousStreak + lastRoundKills < threshold)
            {
                return false;
            }
            // If the previous streak was too high,
            // and the amount of kills they got (minus the remaining difference from the maxStreak) is beneath the threshold,
            // don't give them anything.
            // eg:
            // - Previous streak was 6. MaxStreak is 12 (flashbang, currently).
            // - They would need to get 12 kills to get this killstreak again.
            if (roundedPreviousStreak >= threshold && lastRoundKills - (maxStreak - roundedPreviousStreak) < threshold)
            {
                return false;
            }
            return true;
        }
        public IEnumerable<Reward> GetPendingRewards()
        {
            return MainConfig.Rewards!.Where(CanReceiveReward);
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
