using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CultureSanitizer
{
    public static class StackSentinelLocalizationPatches
    {
        // Именно этот метод крутится в стеке зависшего дампа (сотни кадров).
        // Патчим базовый класс — покрывает и RussianTextProcessor.ProcessToken,
        // который зовёт ProcessTokenInternal. Другие языки не тронем:
        // Check срабатывает только когда стек реально заканчивается.
        private const string TargetType =
            "TaleWorlds.Localization.TextProcessor.LanguageSpecificTextProcessor";

        private const string TargetMethod = "ProcessTokenInternal";

        public static bool TryApply(Harmony harmony)
        {
            var processorType = AccessTools.TypeByName(TargetType);
            if (processorType == null)
            {
                ModLog.Log($"StackSentinel: type {TargetType} not found — patch skipped");
                return false;
            }

            var m = AccessTools.Method(processorType, TargetMethod);
            if (m == null)
            {
                ModLog.Log($"StackSentinel: method {TargetType}.{TargetMethod} not found — patch skipped");
                return false;
            }

            harmony.Patch(m,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(StackSentinelLocalizationPatches), nameof(TextPrefix))),
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(StackSentinelLocalizationPatches), nameof(TextFinalizer))));

            ModLog.Log($"StackSentinel: patched {TargetType}.{TargetMethod}");
            return true;
        }

        private static void TextPrefix()
        {
            StackSentinel.Check("TextProcessor");
        }

        private static Exception TextFinalizer(Exception __exception)
        {
            if (__exception is StackSentinelException)
            {
                ModLog.Log($"[StackSentinel] text recursion interrupted:\n{__exception.StackTrace}");
                return null; // глотаем — текст выйдет с сырым токеном, но игра жива
            }
            return __exception;
        }
    }
}
