using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CultureSanitizer
{
    public class SubModule : MBSubModuleBase
    {
        private static readonly Harmony _sentinelHarmony =
            new Harmony("com.culturesanitizer.stacksentinel");
        private static bool _sentinelMenuPatched;
        private static bool _sentinelTextPatched;

        private static readonly Harmony _sanitizerHarmony =
            new Harmony("com.culturesanitizer.herolists");
        private static bool _sanitizerPatched;
		private static bool _guardPatched;
        public SubModule() => ModLog.Log("assembly loaded, SubModule constructed");

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            TryPatchStackSentinel();
            TryPatchSanitizer();
			TryPatchSiegeGuard(); 
            if (gameStarterObject is CampaignGameStarter campaignGameStarter)
            {
                ModLog.Log("OnGameStart: CampaignGameStarter detected — adding behavior");
                campaignGameStarter.AddBehavior(new CultureSanitizerBehavior());
            }
            else
            {
                ModLog.Log("OnGameStart: not a campaign (starter=" +
                           (gameStarterObject?.GetType().Name ?? "null") + ")");
            }
        }

        private static void TryPatchStackSentinel()
        {
            try
            {
                if (!_sentinelMenuPatched)
                {
                    _sentinelMenuPatched = StackSentinelPatches.TryApply(_sentinelHarmony);
                }

                if (!_sentinelTextPatched)
                {
                    _sentinelTextPatched = StackSentinelLocalizationPatches.TryApply(_sentinelHarmony);
                }
            }
            catch (Exception ex)
            {
                ModLog.Log($"StackSentinel: patch failed — {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void TryPatchSanitizer()
        {
            try
            {
                if (!_sanitizerPatched)
                {
                    _sanitizerPatched = CultureSanitizerPatches.TryApply(_sanitizerHarmony);
                }
            }
            catch (Exception ex)
            {
                ModLog.Log($"[CultureSanitizer] patch failed — {ex.GetType().Name}: {ex.Message}");
            }
        }
		


private static void TryPatchSiegeGuard()
{
    try
    {
        if (!_guardPatched)
            _guardPatched = SiegeAftermathGuard.TryApply(_sanitizerHarmony);
    }
    catch (Exception ex)
    {
        ModLog.Log($"[SiegeGuard] patch failed — {ex.GetType().Name}: {ex.Message}");
    }
}
    }
}