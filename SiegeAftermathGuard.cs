using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Encounters;

namespace CultureSanitizer
{
    internal static class SiegeAftermathGuard
    {
        private static int _guardHits;
        private const int LogBudget = 10;

        private static readonly FieldInfo BesiegerField =
            AccessTools.Field(typeof(SiegeAftermathCampaignBehavior), "_besiegerParty");

        internal static bool TryApply(Harmony harmony)
        {
            if (BesiegerField == null)
            {
                ModLog.Log("[SiegeGuard] Поле _besiegerParty не найдено — гард не установлен.");
                return false;
            }

            bool ok = true;
            ok &= PatchMenu(harmony, "menu_settlement_taken_player_army_member_on_init");
            ok &= PatchMenu(harmony, "menu_settlement_taken_player_participant_on_init");

            ModLog.Log(ok
                ? "[SiegeGuard] Гарды установлены (army_member + participant)."
                : "[SiegeGuard] Часть гардов не установилась — см. логи выше.");
            return ok;
        }

        private static bool PatchMenu(Harmony harmony, string methodName)
        {
            try
            {
                var target = AccessTools.Method(typeof(SiegeAftermathCampaignBehavior), methodName);
                if (target == null)
                {
                    ModLog.Log($"[SiegeGuard] Метод {methodName} не найден — пропущен.");
                    return false;
                }

                harmony.Patch(target,
                    prefix: new HarmonyMethod(
                        typeof(SiegeAftermathGuard), nameof(Prefix)));

                ModLog.Log($"[SiegeGuard] Патч {methodName} применён.");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Log($"[SiegeGuard] Ошибка при установке патча {methodName}: {ex.Message}");
                return false;
            }
        }

        private static bool Prefix(object __instance, MenuCallbackArgs __args)
        {
            // 1. Безопасное получение besieger
            MobileParty besieger;
            try
            {
                besieger = BesiegerField?.GetValue(__instance) as MobileParty;
            }
            catch (Exception ex)
            {
                ModLog.Log($"[SiegeGuard] Ошибка чтения поля _besiegerParty: {ex.Message}");
                return true; // отдаём управление ванили, чтобы не ломать игру
            }

            // 2. Проверка состояния
            var settlement = besieger?.CurrentSettlement;
            if (settlement != null)
                return true; // всё хорошо, ванильная логика работает

            // 3. Логирование (с бюджетом)
            if (_guardHits < LogBudget)
            {
                _guardHits++;

string menuId;
try
{
    menuId = __args.MenuContext.GameMenu.StringId;
}
catch (Exception ex)
{
    menuId = "err:" + ex.GetType().Name;
}

                ModLog.Log($"[SiegeGuard] besieger={besieger?.StringId ?? "null"} " +
                           $"CurrentSettlement=null, menu={menuId} — меню пропущено, " +
                           $"энкаунтер закрываю.");
            }

            // 4. Безопасное завершение энкаунтера
            SafeFinishEncounter();

            // Возвращаем false: ванильное тело меню не выполняем —
            // оно упало бы на CurrentSettlement
            return false;
        }



private static void SafeFinishEncounter()
{
    try
    {
        PlayerEncounter.Finish();
        ModLog.Log("[SiegeGuard] PlayerEncounter.Finish() вызван.");
    }
    catch (Exception ex)
    {
        ModLog.Log($"[SiegeGuard] Finish failed — {ex.GetType().Name}: {ex.Message}");
    }
}
}
}