using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace CultureSanitizer
{
    internal static class CultureSanitizerPatches
    {
        private static bool _inReown;
        private static bool _signatureLogged;
private static int _dumpBudget = 5;

private static int _detailBudget = 10;

        public static bool TryApply(Harmony harmony)
        {
            bool townOk = TryPatch(harmony, typeof(Town), "DailyTick", "TownDailyTickPrefix");
            bool craftOk = TryPatch(harmony,
                typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior),
                "CreateTownOrder", "CreateTownOrderPrefix", "CreateOrder");
            bool ctxOk = TryPatch(harmony,
                typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior),
                "DailyTickSettlement", "DailyTickSettlementPrefix", "DailyTickTown");

            return townOk || craftOk || ctxOk;
        }

        // контекст: запоминаем поселение на время тика этого города
        private static void DailyTickSettlementPrefix(Settlement settlement)
        {
            if (settlement == null)
                return;

            CultureSanitizerBehavior.LastSettlementSwept = settlement;

            if (!CultureSanitizerBehavior.SanitizeHeroLists)
                return;

            try { CultureSanitizerBehavior.SanitizeSettlementHeroLists(settlement); }
            catch (Exception e) { CultureSanitizerBehavior.Log($"hero-list sweep skipped this tick: {e.Message}"); }
        }

        private static bool TryPatch(Harmony harmony, Type type, string methodName, string prefixName,
            params string[] altNames)
        {
            try
            {
                var original = AccessTools.Method(type, methodName);
                if (original == null && altNames != null)
                {
                    foreach (string alt in altNames)
                    {
                        original = AccessTools.Method(type, alt);
                        if (original != null) { methodName = alt; break; }
                    }
                }

                if (original == null)
                {
                    ModLog.Log($"[CultureSanitizer] patch: {type.Name}.{methodName} не найден в этой сборке — пропущен.");
                    return false;
                }

                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CultureSanitizerPatches), prefixName));
                prefix.priority = 900; // выше High (800) и дефолта (400): обгоняем skip Crash Doctor
                harmony.Patch(original, prefix: prefix);
                ModLog.Log($"[CultureSanitizer] patch: {type.Name}.{methodName} — применён.");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Log($"[CultureSanitizer] patch: {type.Name}.{methodName} не наложился — {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static void TownDailyTickPrefix(Town __instance)
        {
            if (!CultureSanitizerBehavior.SanitizeHeroLists)
                return;
            if (__instance?.Settlement == null)
                return;

            CultureSanitizerBehavior.LastSettlementSwept = __instance.Settlement;

            try { CultureSanitizerBehavior.SanitizeSettlementHeroLists(__instance.Settlement); }
            catch (Exception e) { CultureSanitizerBehavior.Log($"hero-list sweep skipped this tick: {e.Message}"); }
        }

private static bool CreateTownOrderPrefix(object __instance, object[] __args)
{
    if (!CultureSanitizerBehavior.ReownBrokenCraftOrders)
        return true;

    if (__args == null || __args.Length < 1)
        return true;

    CultureSanitizerBehavior.CraftOrderCalls++;

    if (!_signatureLogged)
    {
        _signatureLogged = true;
        string sig = string.Join(", ", __args.Select(a => a == null ? "<null>" : a.GetType().Name));
        CultureSanitizerBehavior.Log($"craft-order call args: ({sig})");
    }

    if (_inReown)
        return true;

    // реальная сигнатура: (Town town, Hero orderOwner, int orderSlot)
    Town town = __args.Length > 0 ? __args[0] as Town : null;
    Hero owner = __args.Length > 1 ? __args[1] as Hero : null;
    int orderSlot = __args.Length > 2 && __args[2] is int slot ? slot : -1;

if (_detailBudget > 0)
{
    _detailBudget--;
    CultureSanitizerBehavior.Log($"craft-order detail: town={town?.Name} owner={owner?.Name} ownerSett={owner?.CurrentSettlement?.Name} slot={orderSlot}");
}
    bool ownerOk = owner != null
        && owner.CurrentSettlement != null
        && owner.CurrentSettlement.Town != null;

    if (ownerOk)
        return true; // здоровый путь — ноль вмешательства

    if (owner == null)
        CultureSanitizerBehavior.CraftOrderNullOwner++;
    else
        CultureSanitizerBehavior.CraftOrderBadOwner++;

    Settlement settlement = town?.Settlement
        ?? CultureSanitizerBehavior.LastSettlementSwept;

    if (settlement != null)
    {
        try { CultureSanitizerBehavior.SanitizeSettlementHeroLists(settlement); }
        catch { }

        if (_dumpBudget > 0) { _dumpBudget--; CultureSanitizerBehavior.DumpOrderPool(settlement); }
    }

         Hero replacement = CultureSanitizerBehavior.PickValidOrderOwner(settlement);
            if (replacement == null)
            {
                CultureSanitizerBehavior.Log($"craft order dropped: нет валидного владельца в '{settlement?.StringId ?? "?"}' для слота {orderSlot}");
                return false;
            }

MethodInfo m = AccessTools.GetDeclaredMethods(
        typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior))
    .FirstOrDefault(x => x.Name == "CreateTownOrder" && x.GetParameters().Length == 2);
            if (m == null)
            {
                CultureSanitizerBehavior.Log("craft order dropped: метод CreateTownOrder не найден для re-own.");
                return false;
            }

            ParameterInfo[] pars = m.GetParameters();
            LogSignatureOnce(pars);

            // если в сигнатуре нет параметра Hero — владелец выбирается внутри метода, re-own невозможен
            int heroIdx = -1;
            for (int i = 0; i < pars.Length; i++)
                if (pars[i].ParameterType == typeof(Hero)) { heroIdx = i; break; }

            if (heroIdx < 0)
            {
                CultureSanitizerBehavior.Log("craft order: в сигнатуре CreateTownOrder нет параметра Hero — re-own невозможен, вызов отдан ванили.");
                return true;
            }

            // сборка аргументов по типам параметров реальной сигнатуры
            var args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                Type pt = pars[i].ParameterType;
                if (pt == typeof(Hero))
                    args[i] = replacement;
                else if (pt == typeof(int) && orderSlot >= 0)
                    args[i] = orderSlot;
                else if (pt == typeof(Settlement) && settlement != null)
                    args[i] = settlement;
                else
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }

            _inReown = true;
            try
            {
                m.Invoke(__instance, args);
                CultureSanitizerBehavior.Log($"craft order re-owned: '{owner?.StringId ?? "<null>"}' → '{replacement.StringId}' ({settlement?.StringId ?? "?"})");
            }
            catch (Exception e)
            {
                CultureSanitizerBehavior.Log($"craft order re-own failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
            finally
            {
                _inReown = false;
            }
            return false; // ваниль никогда не увидит битого владельца
        }

        // одноразовый дамп реальной сигнатуры — снимает все догадки о ней
        private static void LogSignatureOnce(ParameterInfo[] pars)
        {
            if (_signatureLogged)
                return;
            _signatureLogged = true;

            string sig = string.Join(", ", pars.Select(p => p.ParameterType.Name + " " + p.Name));
            CultureSanitizerBehavior.Log($"CreateTownOrder signature: ({sig})");
        }
    }
}