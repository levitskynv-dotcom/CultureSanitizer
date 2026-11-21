using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace CultureSanitizer
{
  public static class StackSentinelPatches
  {
    private const string TargetType = "TaleWorlds.CampaignSystem.GameMenus.GameMenu";
    private static readonly string[] Candidates = new string[4]
    {
      "EnterMenuState",
      "ActivateGameMenu",
      "SwitchToMenu",
      "ExitToLast"
    };

    public static bool TryApply(Harmony harmony)
    {
      Type type = AccessTools.TypeByName("TaleWorlds.CampaignSystem.GameMenus.GameMenu");
      if (type == (Type) null)
      {
        ModLog.Log("StackSentinel: type TaleWorlds.CampaignSystem.GameMenus.GameMenu not found — patch skipped");
        return false;
      }
      foreach (string candidate in StackSentinelPatches.Candidates)
      {
        MethodInfo methodInfo = AccessTools.Method(type, candidate, (Type[]) null, (Type[]) null);
        if (!(methodInfo == (MethodInfo) null))
        {
          harmony.Patch((MethodBase) methodInfo, new HarmonyMethod(AccessTools.Method(typeof (StackSentinelPatches), "MenuPrefix", (Type[]) null, (Type[]) null)), (HarmonyMethod) null, (HarmonyMethod) null, new HarmonyMethod(AccessTools.Method(typeof (StackSentinelPatches), "MenuFinalizer", (Type[]) null, (Type[]) null)));
          ModLog.Log("StackSentinel: patched TaleWorlds.CampaignSystem.GameMenus.GameMenu." + candidate);
          return true;
        }
      }
      ModLog.Log("StackSentinel: no candidate method on TaleWorlds.CampaignSystem.GameMenus.GameMenu. Declared: " + string.Join(", ", AccessTools.GetDeclaredMethods(type).Select<MethodInfo, string>((Func<MethodInfo, string>) (mi => mi.Name)).Distinct<string>().Take<string>(40)));
      return false;
    }

    private static void MenuPrefix() => StackSentinel.Check("GameMenu");

    private static Exception MenuFinalizer(Exception __exception)
    {
      if (!(__exception is StackSentinelException))
        return __exception;
      ModLog.Log("[StackSentinel] recursion interrupted in menu:\n" + __exception.StackTrace);
      return (Exception) null;
    }
  }
}