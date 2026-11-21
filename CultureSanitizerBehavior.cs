using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using TaleWorlds.Library;
using System.Reflection;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.MapEvents;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CultureSanitizer
{




    public class CultureSanitizerBehavior : CampaignBehaviorBase
    {
        private int _healedCopies;
//        private const string FallbackCultureId = "neutral";
        private const string LogTag = "[CultureSanitizer]";

        private static bool _lastSweepFailed;

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnGameLoaded(CampaignGameStarter starter) => RunSweep("OnGameLoaded");
        private void OnNewGameCreated(CampaignGameStarter starter) => RunSweep("OnNewGameCreated");
        private void OnDailyTick() => RunSweep("DailyTick");


        private void RunSweep(string source)
        {
            try
            {
                _healedCopies = 0;

                CultureObject fallback = FindFallbackCulture();
                if (fallback == null)
                {
                    Log($"{source}: no usable culture found — sweep skipped.");
                    return;
                }


                int fixedChars = FixCharacterObjects(fallback);
                int fixedHeroes = FixHeroes(fallback);
                int fixedClans = FixClans(fallback);
                int fixedUpgrades = FixUpgradeTargets(fallback);
int charsScanned = CharacterObject.All.Count();
int heroesScanned = GetHeroes().Count();
int clansScanned = Clan.All.Count();

                int replacedTroops = 0, removedOrphans = 0, rostersScanned = 0;

                foreach (MobileParty party in MobileParty.All)
                {
                    if (party == null || party.Party == null)
                        continue;

                    string label = party.StringId ?? "unknown_party";
                    replacedTroops += RepairRoster(party.MemberRoster, label + ".MemberRoster", fallback, ref removedOrphans);
                    replacedTroops += RepairRoster(party.PrisonRoster, label + ".PrisonRoster", fallback, ref removedOrphans);
                    rostersScanned += 2;
                }

                int garrisonReplaced = 0, garrisonScanned = 0;
                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement == null)
                        continue;

                    try
                    {
                        MobileParty garrison = settlement.Town?.GarrisonParty;
                        if (garrison != null && garrison.MemberRoster != null)
                        {
                            string label = (garrison.StringId ?? settlement.StringId ?? "garrison") + ".Garrison";
                            garrisonReplaced += RepairRoster(garrison.MemberRoster, label, fallback, ref removedOrphans);
                            garrisonScanned++;
                        }
                    }
                    catch { /* у поселения может не быть Town — пропускаем */ }
                }
				_fallbackRef = fallback;
                replacedTroops += garrisonReplaced;
                rostersScanned += garrisonScanned;
				
int fixedTemplates = FixPartyTemplates();
int eventReplaced = RepairMapEvents(ref removedOrphans);

int heroListsScanned = SanitizeAllSettlementHeroLists();
int riskyPools = AuditOrderPools(source);

Log($"{source}: craft-orders: calls={CraftOrderCalls}, null-owner={CraftOrderNullOwner}, bad-owner={CraftOrderBadOwner}");
CraftOrderCalls = 0;
CraftOrderNullOwner = 0;
CraftOrderBadOwner = 0;
replacedTroops += eventReplaced;
// ВРЕМЕННО ОТКЛЮЧЕНО (бисекция краша на входе в город):
// int eventReplaced = RepairMapEvents(ref removedOrphans);
// replacedTroops += eventReplaced;

Log($"{source}: chars={fixedChars}/{charsScanned}, heroes={fixedHeroes}/{heroesScanned}, " +
    $"clans={fixedClans}/{clansScanned}; " +
    $"roster-repair scanned={rostersScanned} rosters, replaced={replacedTroops}, " +
    $"healed-copies={_healedCopies}, upgrades={fixedUpgrades}, orphans-removed={removedOrphans}, " +
    $"templates={fixedTemplates}, events={eventReplaced}, " +
    $"hero-lists scanned={heroListsScanned}, nulls={_hlNulls}, dead={_hlDead}, moved={_hlMoved}, rebound={_hlRebound}, " +
    $"pools-risky={riskyPools}, " +
    $"fallback='{fallback.StringId}'");

                _lastSweepFailed = false;
				


            }
            catch (Exception e)
            {
                if (!_lastSweepFailed)
                {
                    Log($"ERROR in sweep ({source}): {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                    _lastSweepFailed = true;
                }
            }
        }
		
		
private int FixUpgradeTargets(CultureObject fallback)
{
    int fixedCount = 0;
    try
    {
        var prop = AccessTools.Property(typeof(CharacterObject), "UpgradeTargets");
        if (prop == null)
        {
            Log("FixUpgradeTargets: property UpgradeTargets not found.");
            return 0;
        }

        foreach (CharacterObject ch in CharacterObject.All)
        {
            if (ch == null)
                continue;

            if (prop.GetValue(ch, null) is CharacterObject[] targets)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    CharacterObject t = targets[i];
                    if (t == null)
                        continue;

                    if (t.Culture == null && TrySetCulture(t, fallback))
                    {
                        _healedCopies++;
                        fixedCount++;
                        Log($"upgrade-target healed: owner='{ch.StringId}', target='{t.StringId}'");
                    }

                    CharacterObject registered = FindRegistered(t.StringId);
                    if (registered != null && !ReferenceEquals(registered, t))
                    {
                        targets[i] = registered;
                        fixedCount++;
                        Log($"upgrade-target replaced: owner='{ch.StringId}', target='{registered.StringId}'");
                    }
                }
            }
        }
    }
    catch (Exception e)
    {
        Log($"FixUpgradeTargets error: {e.GetType().Name}: {e.Message}");
    }
    return fixedCount;
}		
        // ============================================================
        // ЧАСТЬ 1. Культура: подмена null на fallback
        // ============================================================

        private int FixCharacterObjects(CultureObject fallback)
        {
            int count = 0;
            foreach (CharacterObject character in CharacterObject.All)
            {
                if (character?.Culture != null)
                    continue;

                if (TrySetCulture(character, fallback, "_culture", "culture", "_cultureObject"))
                    count++;
            }
            return count;
        }

        private int FixHeroes(CultureObject fallback)
        {
            int count = 0;
            foreach (Hero hero in GetHeroes())
            {
                if (hero?.Culture != null)
                    continue;

                bool ok = TrySetProperty(hero, "Culture", fallback);
                if (!ok)
                    ok = TrySetCulture(hero, fallback, "_culture", "culture", "_cultureObject");
                if (ok)
                    count++;
            }
            return count;
        }

        private int FixClans(CultureObject fallback)
        {
            int count = 0;
            foreach (Clan clan in Clan.All)
            {
                if (clan?.Culture != null)
                    continue;

                bool ok = TrySetProperty(clan, "Culture", fallback);
                if (!ok)
                    ok = TrySetCulture(clan, fallback, "_culture", "culture", "_cultureObject");
                if (ok)
                    count++;
            }
            return count;
        }

        // ============================================================
        // ЧАСТЬ 2. Ростеры: замена инородных/пустых копий на зарегистрированные
        // ============================================================
private static readonly HashSet<int> _foreignRefsSeen = new HashSet<int>();

private CultureObject _fallbackRef;

//private int RepairRoster(TroopRoster roster, string label, CultureObject fallback, ref int orphansRemoved)
//{
//    if (roster == null)
//        return 0;
//
//    IList elements = GetInternalElementList(roster);
//    if (elements == null)
//    {
//        Log($"roster {label}: internal element list not found — skipped.");
//        return 0;
//    }
//
//    if (label.Contains("95329") || label.Contains("lord_1_16"))
//        DumpRosterFields(roster, label);
//	
//    int replaced = 0, elementsSeen = 0, charNull = 0, charForeign = 0;
//
//    for (int i = elements.Count - 1; i >= 0; i--)
//    {
//        try
//        {
//            object boxed = elements[i];
//            elementsSeen++;
//            CharacterObject troop = GetElementCharacter(boxed);
//
//            if (troop == null)
//            {
//                charNull++;
//                continue;
//            }
//
//           if (troop.Culture == null)
//           {
//                if (TrySetCulture(troop, fallback))
//                    _healedCopies++;
//                else
//                    Log($"roster {label}: could not heal culture on '{troop.StringId}' (copy).");
//            }
//
//            CharacterObject registered = FindRegistered(troop.StringId);
//
//            if (registered == null)
//            {
//                Log($"roster {label}: orphan troop (id='{troop.StringId}') at index {i} — element removed.");
//                elements.RemoveAt(i);
//                orphansRemoved++;
//                continue;
//            }
//
//            if (!ReferenceEquals(registered, troop))
//            {
//                charForeign++;
//                int refHash = RuntimeHelpers.GetHashCode(troop);
//                bool seenBefore = !_foreignRefsSeen.Add(refHash);
//                Log($"roster {label}: foreign copy '{troop.StringId}' (culture={troop.Culture?.StringId ?? "null"}, ref=0x{refHash:x}{(seenBefore ? ", REPEAT" : ", new")}) at index {i}");
//
//                if (SetElementCharacter(boxed, registered))
//                {
//                    elements[i] = boxed;
//
//                    object check = elements[i];
//                    CharacterObject checkChar = GetElementCharacter(check);
//                    if (!ReferenceEquals(checkChar, registered))
//                    {
//                        Log($"roster {label}: VERIFY FAILED at index {i}: list holds " +
//                            $"'{checkChar?.StringId ?? "null"}' (ref=0x{(checkChar != null ? RuntimeHelpers.GetHashCode(checkChar) : 0):x}), " +
//                            $"expected '{registered.StringId}' (ref=0x{RuntimeHelpers.GetHashCode(registered):x})");
//                    }
//                    else
//                    {
//                        replaced++;
//                    }
//                }
//                else
//                {
//                    Log($"roster {label}: could not set Character at index {i} for '{troop.StringId}'.");
//                }
//            }
//        }
//        catch (Exception e)
//        {
//            Log($"roster {label}: error at index {i}: {e.GetType().Name}: {e.Message}");
//        }
//  }
//
//    if (replaced > 0)
//        Log($"roster {label}: replaced={replaced}");
//
//    return replaced;
//}

private int RepairRoster(TroopRoster roster, string label, CultureObject fallback, ref int orphansRemoved)
{
    if (roster == null)
        return 0;

    int replaced = 0;

    IList elements = GetInternalElementList(roster);
    if (elements != null)
        replaced += RepairElementList(elements, label + ".MBList", fallback, ref orphansRemoved, true);

    IList data = GetSerializationData(roster);
    if (data != null && !ReferenceEquals(data, elements))
        replaced += RepairElementList(data, label + ".Data", fallback, ref orphansRemoved, false);

    return replaced;
}
private static IList GetSerializationData(TroopRoster roster)
{
    Type type = roster.GetType();
    while (type != null)
    {
        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (f.Name == "data" && typeof(IList).IsAssignableFrom(f.FieldType))
                return f.GetValue(roster) as IList;
        }
        type = type.BaseType;
    }
    return null;
}

private int RepairElementList(IList elements, string label, CultureObject fallback, ref int orphansRemoved, bool allowRemove)
{
    int replaced = 0;

    if (label.Contains("95329") || label.Contains("lord_1_16"))
        DumpRosterFieldsPlain(label, elements);

    for (int i = elements.Count - 1; i >= 0; i--)
    {
        try
        {
            object boxed = elements[i];
            CharacterObject troop = GetElementCharacter(boxed);

            if (troop == null)
                continue;

            if (troop.Culture == null)
            {
                if (TrySetCulture(troop, fallback))
                    _healedCopies++;
                else
                    Log($"roster {label}: could not heal culture on '{troop.StringId}' (copy).");
            }

            CharacterObject registered = FindRegistered(troop.StringId);

            if (registered == null)
            {
                Log($"roster {label}: orphan troop (id='{troop.StringId}') at index {i}" + (allowRemove ? " — element removed." : " — array slot skipped."));
                if (allowRemove)
                {
                    elements.RemoveAt(i);
                    orphansRemoved++;
                }
                continue;
            }

            if (!ReferenceEquals(registered, troop))
            {
                int refHash = RuntimeHelpers.GetHashCode(troop);
                bool seenBefore = !_foreignRefsSeen.Add(refHash);
                Log($"roster {label}: foreign copy '{troop.StringId}' (culture={troop.Culture?.StringId ?? "null"}, ref=0x{refHash:x}{(seenBefore ? ", REPEAT" : ", new")}) at index {i}");

                if (SetElementCharacter(boxed, registered))
                {
                    elements[i] = boxed;

                    object check = elements[i];
                    CharacterObject checkChar = GetElementCharacter(check);
                    if (!ReferenceEquals(checkChar, registered))
                    {
                        Log($"roster {label}: VERIFY FAILED at index {i}: list holds " +
                            $"'{checkChar?.StringId ?? "null"}' (ref=0x{(checkChar != null ? RuntimeHelpers.GetHashCode(checkChar) : 0):x}), " +
                            $"expected '{registered.StringId}' (ref=0x{RuntimeHelpers.GetHashCode(registered):x})");
                    }
                    else
                    {
                        replaced++;
                    }
                }
                else
                {
                    Log($"roster {label}: could not set Character at index {i} for '{troop.StringId}'.");
                }
            }
        }
        catch (Exception e)
        {
            Log($"roster {label}: error at index {i}: {e.GetType().Name}: {e.Message}");
        }
    }

    if (replaced > 0)
        Log($"roster {label}: replaced={replaced}");

    return replaced;
}

private static void DumpRosterFieldsPlain(string label, IList list)
{
    Log($"roster {label}: container count={list.Count}");
}



private int RepairMapEvents(ref int orphansRemoved)
{
    int replaced = 0;
    try
    {
        object manager = FindMapEventManager();
        if (manager == null)
        {
            Log("RepairMapEvents: event manager not found — skipped.");
            return 0;
        }

        List<MapEvent> events = ExtractMapEvents(manager);
        Log($"RepairMapEvents: manager type='{manager.GetType().Name}', events found={events.Count}");

        foreach (MapEvent ev in events)
        {
            foreach (MapEventParty mp in ExtractEventParties(ev))
            {
                foreach (var prop in mp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.PropertyType != typeof(TroopRoster) || !prop.CanRead)
                        continue;

                    TroopRoster roster = null;
                    try { roster = prop.GetValue(mp, null) as TroopRoster; } catch { }
                    if (roster == null)
                        continue;

                    string partyName = "unknown_party";
                    try { partyName = mp.Party?.MobileParty?.StringId ?? partyName; } catch { }

                    string label = partyName + ".MapEvent." + prop.Name;
                    replaced += RepairRoster(roster, label, _fallbackRef, ref orphansRemoved);
                }
            }
        }
    }
    catch (Exception e)
    {
        Log($"RepairMapEvents error: {e.GetType().Name}: {e.Message}");
    }
    return replaced;
}
private static object FindMapEventManager()
{
    try
    {
        Type t = AccessTools.TypeByName("TaleWorlds.CampaignSystem.CampaignBehaviors.MapEventCampaignManager")
              ?? AccessTools.TypeByName("TaleWorlds.CampaignSystem.MapEventCampaignManager")
              ?? AccessTools.TypeByName("MapEventCampaignManager");

        if (t != null && Campaign.Current != null)
        {
            var behaviors = AccessTools.Field(Campaign.Current.GetType(), "_campaignBehaviors")?.GetValue(Campaign.Current)
                            as IEnumerable;
            if (behaviors != null)
            {
                foreach (object behavior in behaviors)
                {
                    if (behavior != null && t.IsInstanceOfType(behavior))
                        return behavior;
                }
            }
        }

        // резерв: ищем в Campaign.Current поле, связанное с MapEvent
        if (Campaign.Current != null)
        {
            Type ctype = Campaign.Current.GetType();
            while (ctype != null)
            {
                foreach (var f in ctype.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    bool byType = f.FieldType != null && f.FieldType.Name.IndexOf("MapEvent", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool byName = f.Name.IndexOf("MapEvent", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (byType || byName)
                    {
                        object v = f.GetValue(Campaign.Current);
                        if (v != null)
                        {
                            Log($"FindMapEventManager: using field '{f.Name}' of type '{f.FieldType.Name}'");
                            return v;
                        }
                    }
                }
                ctype = ctype.BaseType;
            }
        }

        return null;
    }
    catch (Exception e)
    {
        Log($"FindMapEventManager error: {e.GetType().Name}: {e.Message}");
        return null;
    }
}
private static List<MapEvent> ExtractMapEvents(object manager)
{
    var result = new List<MapEvent>();
    try
    {
        Type type = manager.GetType();
        while (type != null && result.Count == 0)
        {
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!typeof(IEnumerable).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(string))
                    continue;

                if (f.GetValue(manager) is IEnumerable seq)
                {
                    foreach (object item in seq)
                        if (item is MapEvent ev)
                            result.Add(ev);
                }

                if (result.Count > 0)
                    break;
            }
            type = type.BaseType;
        }
    }
    catch (Exception e)
    {
        Log($"ExtractMapEvents error: {e.GetType().Name}: {e.Message}");
    }
    return result;
}

private static IEnumerable<MapEventParty> ExtractEventParties(MapEvent ev)
{
    var result = new List<MapEventParty>();
    try
    {
        Type type = ev.GetType();
        while (type != null)
        {
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (f.FieldType == typeof(MapEventParty))
                {
                    if (f.GetValue(ev) is MapEventParty mp)
                        result.Add(mp);
                }
                else if (typeof(IEnumerable).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(string))
                {
                    if (f.GetValue(ev) is IEnumerable seq)
                        foreach (object item in seq)
                            if (item is MapEventParty mp2)
                                result.Add(mp2);
                }
            }
            type = type.BaseType;
        }
    }
    catch (Exception e)
    {
        Log($"ExtractEventParties error: {e.GetType().Name}: {e.Message}");
    }
    return result;
}
private static void DumpRosterFields(TroopRoster roster, string label)
{
    try
    {
        Type type = roster.GetType();
        while (type != null)
        {
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (typeof(IList).IsAssignableFrom(f.FieldType) || f.FieldType.IsArray)
                {
                    object v = f.GetValue(roster);
                    int count = (v as IList)?.Count ?? -1;
                    Log($"roster {label}: field '{f.Name}' type={f.FieldType.Name} count={count} hash=0x{(v != null ? RuntimeHelpers.GetHashCode(v) : 0):x}");
                }
            }
            type = type.BaseType;
        }
    }
    catch (Exception e)
    {
        Log($"DumpRosterFields error: {e.Message}");
    }
}



private static bool TrySetCulture(CharacterObject troop, CultureObject culture)
{
    if (troop == null || culture == null)
        return false;

    try
    {
        var prop = typeof(CharacterObject).GetProperty("Culture",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(troop, culture, null);
            return true;
        }

        foreach (var field in troop.GetType().GetFields(
                     System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic))
        {
            if (typeof(CultureObject).IsAssignableFrom(field.FieldType))
            {
                field.SetValue(troop, culture);
                return true;
            }
        }
    }
    catch (Exception e)
    {
        Log($"TrySetCulture error for '{troop.StringId}': {e.GetType().Name}: {e.Message}");
    }
    return false;
}
        private static CharacterObject FindRegistered(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                    return null;
                return MBObjectManager.Instance.GetObject<CharacterObject>(id);
            }
            catch
            {
                return null;
            }
        }

        // --- рефлексия: внутренний список TroopRosterElement ---

private static IList GetInternalElementList(TroopRoster roster)
{
    string[] candidates =
    {
        "_troopRosterElements", "troopRosterElements",
        "_troopAndPrisonerRosterElements", "troopAndPrisonerRosterElements",
        "_elements"
    };

    Type type = typeof(TroopRoster);
    while (type != null)
    {
        foreach (string name in candidates)
        {
            var field = AccessTools.Field(type, name);
            if (field != null && field.GetValue(roster) is IList list)
            {

                return list;
            }
        }
        type = type.BaseType;
    }
    return null;
}


private static CharacterObject GetElementCharacter(object element)
{
    var prop = AccessTools.Property(element.GetType(), "Character");
    if (prop != null && prop.CanRead)
        return prop.GetValue(element, null) as CharacterObject;

    foreach (string name in new[] { "Character", "_character", "character" })
    {
        var field = AccessTools.Field(element.GetType(), name);
        if (field != null)
            return field.GetValue(element) as CharacterObject;
    }
    return null;
}


private static bool SetElementCharacter(object element, CharacterObject value)
{
    var prop = AccessTools.Property(element.GetType(), "Character");
    if (prop != null && prop.CanWrite && prop.PropertyType.IsInstanceOfType(value))
    {
        prop.SetValue(element, value, null);
        return true;
    }

    foreach (string name in new[] { "Character", "_character", "character" })
    {
        var field = AccessTools.Field(element.GetType(), name);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(element, value);
            return true;
        }
    }
    return false;
}
        // --- рефлексия: список героев ---

        private static IEnumerable<Hero> GetHeroes()
        {
            Type type = typeof(Hero);
            string[] candidates = { "All", "AllAliveHeroes", "AllHeroes" };

            foreach (string name in candidates)
            {
                var field = AccessTools.Field(type, name);
                if (field != null && field.GetValue(null) is IEnumerable<Hero> fromField)
                    return fromField;
            }

            foreach (string name in candidates)
            {
                var prop = AccessTools.Property(type, name);
                if (prop != null && prop.GetValue(null, null) is IEnumerable<Hero> fromProp)
                    return fromProp;
            }

            Log("could not find heroes list (All/AllAliveHeroes/AllHeroes) — heroes sweep skipped.");
            return Enumerable.Empty<Hero>();
        }

        // --- общие helpers ---

        private static bool TrySetCulture(object target, CultureObject value, params string[] fieldNames)
        {
            if (target == null)
                return false;

            Type type = target.GetType();
            while (type != null)
            {
                foreach (string name in fieldNames)
                {
                    var field = AccessTools.Field(type, name);
                    if (field != null && field.FieldType.IsInstanceOfType(value))
                    {
                        field.SetValue(target, value);
                        return true;
                    }
                }
                type = type.BaseType;
            }
            return false;
        }

        private static bool TrySetProperty(object target, string propertyName, CultureObject value)
        {
            if (target == null)
                return false;

            Type type = target.GetType();
            while (type != null)
            {
                var property = AccessTools.Property(type, propertyName);
                if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
                {
                    property.SetValue(target, value, null);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }
private static CultureObject FindFallbackCulture()
{
    var om = MBObjectManager.Instance;

    string[] ids =
    {
        "neutral",
        "empire",
        "vlandia",
        "sturgia",
        "battania",
        "aserai",
		"nord",
        "khuzait"
    };

    foreach (string id in ids)
    {
        try
        {
            CultureObject byId = om.GetObject<CultureObject>(id);
            if (byId != null)
            {
                Log($"fallback culture resolved by id '{id}'.");
                return byId;
            }
        }
        catch
        {
            // метод недоступен в этой сборке — тихо идём к запасному пути
        }
    }

    // запасной путь: первая культура, найденная у любого юнита
    try
    {
        foreach (CharacterObject ch in CharacterObject.All)
        {
            if (ch?.Culture != null)
            {
                Log($"fallback culture resolved as first unit culture: '{ch.Culture.StringId}'.");
                return ch.Culture;
            }
        }
    }
    catch
    {
        // перебор не удался — уходим с null
    }

    return null;
}
private int FixPartyTemplates()
{
    int fixedCount = 0;
    try
    {
        foreach (PartyTemplateObject pt in MBObjectManager.Instance.GetObjectTypeList<PartyTemplateObject>())
        {
            var stacks = AccessTools.Field(pt.GetType(), "_stacks")?.GetValue(pt) as IEnumerable;
            if (stacks == null)
                continue;

            int idx = 0;
            foreach (object stack in stacks)
            {
                var chProp = AccessTools.Property(stack.GetType(), "Character");
                if (chProp != null && chProp.CanRead && chProp.GetValue(stack, null) is CharacterObject t && t != null)
                {
                    CharacterObject registered = FindRegistered(t.StringId);
                    if (registered != null && !ReferenceEquals(registered, t))
                    {
                        chProp.SetValue(stack, registered, null);
                        fixedCount++;
                        Log($"party-template '{pt.StringId}': stack[{idx}] replaced '{t.StringId}'");
                    }
                    else if (registered == null)
                    {
                        Log($"party-template '{pt.StringId}': stack[{idx}] orphan '{t.StringId}'");
                    }
                }
                idx++;
            }
        }
    }
    catch (Exception e)
    {
        Log($"FixPartyTemplates error: {e.GetType().Name}: {e.Message}");
    }
    return fixedCount;
}
//        private static void Log(string message)
//        {
//            try
//            {
//                FileLog.Log($"{LogTag} {message}");
//            }
//            catch
//            {
                // логгер недоступен — молча
//            }
//        }
		// лог в основной
internal static void Log(string message)
{
    try { TaleWorlds.Library.Debug.Print($"{LogTag} {message}"); } catch { }
    try
    {
        string dir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        string path = System.IO.Path.Combine(dir, "CultureSanitizer_log.txt");
        System.IO.File.AppendAllText(path,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {LogTag} {message}\r\n");
    }
    catch { }
}
    
	
// ============================================================
// ЧАСТЬ 3. Списки героев поселений: HeroesWithoutParty / Notables
// Источник [CD CraftOrder] owner=<null>: нулевая запись в списке
// ============================================================

internal static Settlement LastSettlementSwept;            // запоминаем город для re-own
internal static bool SanitizeHeroLists = true;         // выключатель свипа списков
internal static bool ReownBrokenCraftOrders = true;    // выключатель re-own заказов

private static int _hlNulls, _hlDead, _hlMoved, _hlRebound;

internal static int CraftOrderNullOwner;
internal static int CraftOrderCalls;
internal static int CraftOrderBadOwner;

private static int SanitizeAllSettlementHeroLists()
{
    _hlNulls = _hlDead = _hlMoved = _hlRebound = 0;
    int listsScanned = 0;

    foreach (Settlement settlement in Settlement.All)
    {
        if (settlement == null)
            continue;

        listsScanned += SanitizeSettlementHeroLists(settlement);
    }

    if (_hlNulls + _hlDead + _hlMoved + _hlRebound > 0)
        Log($"hero-lists: nulls-removed={_hlNulls}, dead-removed={_hlDead}, moved-away-removed={_hlMoved}, rebound={_hlRebound}");
    else
        Log("hero-lists: все списки героев чисты.");

    return listsScanned;
}

private static void SanitizeHeroList(IList list, Settlement settlement, string label)
{
    for (int i = list.Count - 1; i >= 0; i--)
    {
        try
        {
            if (!(list[i] is Hero hero))
            {
                Log($"hero-list {label}@{settlement.StringId}: null entry at index {i} — removed.");
                list.RemoveAt(i);
                _hlNulls++;
                continue;
            }

            if (SafeIsDead(hero))
            {
                Log($"hero-list {label}@{settlement.StringId}: dead hero '{hero.StringId}' at index {i} — removed.");
                list.RemoveAt(i);
                _hlDead++;
                continue;
            }

            Settlement actual = SafeCurrentSettlement(hero);
            if (actual == settlement)
                continue; // живой, на месте — здоровая запись

            if (actual != null)
            {
                Log($"hero-list {label}@{settlement.StringId}: stale hero '{hero.StringId}' (actually at '{actual.StringId}') — removed.");
                list.RemoveAt(i);
                _hlMoved++;
                continue;
            }

            // живой герой без привязки: город его помнит — перепривязываем, а не удаляем
            if (SafePartyBelongedTo(hero) == null && SafeGovernorOf(hero) == null)
            {
                if (TrySetReference(hero, settlement, "CurrentSettlement", "_currentSettlement"))
                {
                    Log($"hero-list {label}@{settlement.StringId}: rootless hero '{hero.StringId}' rebound to settlement.");
                    _hlRebound++;
                }
                else
                {
                    Log($"hero-list {label}@{settlement.StringId}: could not rebind '{hero.StringId}' — removed.");
                    list.RemoveAt(i);
                    _hlMoved++;
                }
                continue;
            }

            // у героя есть партия или должность — запись в списке устарела
            Log($"hero-list {label}@{settlement.StringId}: stale hero '{hero.StringId}' (has party/governorship) — removed.");
            list.RemoveAt(i);
            _hlMoved++;
        }
        catch (Exception e)
        {
            Log($"hero-list {label}: error at index {i}: {e.GetType().Name}: {e.Message}");
        }
    }
}


internal static bool IsValidOrderOwner(Hero hero)
{
    if (hero == null || SafeIsDead(hero))
        return false;

    Settlement s = SafeCurrentSettlement(hero);
    return s != null && s.Town != null;
}

internal static Hero PickValidOrderOwner(Settlement settlement)
{
    if (settlement == null)
        return null;

    IList notables = GetHeroListForRead(settlement, "_notables", "notables", "Notables");
    if (notables != null)
        foreach (object o in notables)
            if (o is Hero h && IsValidOrderOwner(h))
                return h;

    IList hwop = GetHeroListForRead(settlement, "_heroesWithoutParty", "heroesWithoutParty", "HeroesWithoutParty");
    if (hwop != null)
        foreach (object o in hwop)
            if (o is Hero h && IsValidOrderOwner(h))
                return h;

    // резерв ванили: лидеры партий, стоящих в городе
    try
    {
        foreach (MobileParty party in settlement.Parties)
            if (party?.LeaderHero != null && IsValidOrderOwner(party.LeaderHero))
                return party.LeaderHero;
    }
    catch { }

    return null;
}// список надо менять через исходное поле: свойство может вернуть копию, и правка не закрепится
private static bool SafeIsDead(Hero hero)
{
    try { return hero.IsDead || !hero.IsAlive; }
    catch { try { return hero.IsDead; } catch { return false; } }
}

private static Settlement SafeCurrentSettlement(Hero hero)
{
    try { return hero.CurrentSettlement; } catch { return null; }
}

private static MobileParty SafePartyBelongedTo(Hero hero)
{
    try { return hero.PartyBelongedTo; } catch { return null; }
}

private static Town SafeGovernorOf(Hero hero)
{
    try { return hero.GovernorOf; } catch { return null; }
}
private static readonly HashSet<string> _propertyOnlyWarned = new HashSet<string>();

// запись: только поле-хранитель; свойства не годятся — это копии или вычисления
private static IList GetMutableHeroList(Settlement settlement, params string[] names)
{
    try
    {
        Type type = settlement.GetType();
        while (type != null)
        {
            foreach (string name in names)
            {
                var field = AccessTools.Field(type, name);
                if (field != null && field.GetValue(settlement) is IList list)
                    return list;
            }
            type = type.BaseType;
        }
    }
    catch (Exception e)
    {
        Log($"GetMutableHeroList error ({names[0]}): {e.GetType().Name}: {e.Message}");
    }
    return null;
}

// чтение: поле или свойство — для выбора владельца копия безопасна
private static IList GetHeroListForRead(Settlement settlement, params string[] names)
{
    IList list = GetMutableHeroList(settlement, names);
    if (list != null)
        return list;

    try
    {
        foreach (string name in names)
        {
            var prop = AccessTools.Property(settlement.GetType(), name);
            if (prop != null && prop.GetValue(settlement, null) is IList fromProp)
            {
                if (_propertyOnlyWarned.Add(name))
                    Log($"hero-list: '{name}' — вычисляемое свойство, в нём нечего чистить; источник смотрим через данные героев.");
                return fromProp;
            }
        }
    }
    catch (Exception e)
    {
        Log($"GetHeroListForRead error ({names[0]}): {e.GetType().Name}: {e.Message}");
    }
    return null;
}

internal static int SanitizeSettlementHeroLists(Settlement settlement)
{
    if (settlement == null)
        return 0;

    int scanned = 0;

    // Notables — единственный хранимый список из пула владельцев: чистим его
    IList notables = GetMutableHeroList(settlement, "_notables", "notables", "Notables");
    if (notables != null)
    {
        scanned++;
        SanitizeHeroList(notables, settlement, "Notables");
    }

//if (settlement.Town == null)
 //   continue; // заказы на ковку создаёт только город — остальное не в счёт
    // HeroesWithoutParty в этой сборке — вычисляемое свойство: мусора там не бывает по построению.
    // Состояние пула смотрим дампом при битом заказе (DumpOrderPool).
    return scanned;
}

// диагноз при owner=<null>: пустой пул или нулевая запись внутри — две разные болезни
internal static void DumpOrderPool(Settlement settlement)
{
    if (settlement == null)
        return;

    void ScanList(IList list, string label)
    {
        if (list == null)
        {
            Log($"order-pool {label} @'{settlement.StringId}': список недоступен");
            return;
        }

int raw = list.Count, valid = 0;
foreach (object o in list)
{
    Hero h = o as Hero;

    if (h == null)
    {
        Log($"order-pool {label} @'{settlement.StringId}': запись null или не Hero ({o?.GetType().Name ?? "<null>"})");
        continue;
    }

    if (IsValidOrderOwner(h))
    {
        valid++;
    }
    else
    {
        Log($"order-pool {label} @'{settlement.StringId}': битая запись hero='{h.StringId}', " +
            $"dead={SafeIsDead(h)}, settlement={h.CurrentSettlement?.StringId ?? "<null>"}, " +
            $"party={h.PartyBelongedTo?.StringId ?? "<null>"}");
    }
}
Log($"order-pool {label} @'{settlement.StringId}': raw={raw}, valid={valid}");

    }

    ScanList(GetHeroListForRead(settlement, "_notables", "notables", "Notables"), "notables");
    ScanList(GetHeroListForRead(settlement, "_heroesWithoutParty", "heroesWithoutParty", "HeroesWithoutParty"), "hwop");

    try
    {
        foreach (MobileParty party in settlement.Parties)
            if (party?.LeaderHero == null)
                Log($"order-pool parties @'{settlement.StringId}': партия без лидера id='{party.StringId}' ({party.Name})");
    }
    catch { }
}


internal static void DumpPatchOwners()
{
    foreach (var m in Harmony.GetAllPatchedMethods())
    {
        var info = Harmony.GetPatchInfo(m);
        if (info.Owners.Count > 1)
            Log($"[Owners] {m.DeclaringType?.Name}.{m.Name} <- " +
                string.Join(", ", info.Owners));
    }
}

internal static int AuditOrderPools(string source)
{
    int risky = 0, scanned = 0;

    foreach (Settlement settlement in Settlement.All)
    {
        if (settlement == null)
            continue;


try
{
    scanned++;

    if (settlement.Town == null)
        continue; // заказы на ковку создаёт только город — деревни и укрытия не в счёт

    int notables = 0, hwop = 0, leaders = 0;

    IList n = GetHeroListForRead(settlement, "_notables", "notables", "Notables");
    if (n != null)
        foreach (object o in n)
            if (o is Hero h && IsValidOrderOwner(h))
                notables++;

            IList hw = GetHeroListForRead(settlement, "_heroesWithoutParty", "heroesWithoutParty", "HeroesWithoutParty");
            if (hw != null)
                foreach (object o in hw)
                    if (o is Hero h && IsValidOrderOwner(h))
                        hwop++;

            foreach (MobileParty party in settlement.Parties)
                if (party?.LeaderHero != null && IsValidOrderOwner(party.LeaderHero))
                    leaders++;

            if (notables + hwop + leaders == 0)
            {
                risky++;
                Log($"order-pool RISK @'{settlement.StringId}' ({settlement.Name?.ToString() ?? "?"}): notables=0, hwop=0, party-leaders=0 — здесь ваниль выдаст owner=null");
            }
        }
        catch (Exception e)
        {
            Log($"order-pool audit error @'{settlement.StringId}': {e.GetType().Name}: {e.Message}");
        }
    }

    Log($"{source}: order-pool audit: scanned={scanned}, risky={risky}");
    return risky;
}
// общий сеттер: свойство (в т.ч. private set), затем поле по иерархии типов
private static bool TrySetReference(object target, object value, params string[] names)
{
    if (target == null)
        return false;

    Type type = target.GetType();
    while (type != null)
    {
        foreach (string name in names)
        {
            var prop = AccessTools.Property(type, name);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsInstanceOfType(value))
            {
                prop.SetValue(target, value, null);
                return true;
            }
        }
        type = type.BaseType;
    }

    type = target.GetType();
    while (type != null)
    {
        foreach (string name in names)
        {
            var field = AccessTools.Field(type, name);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(target, value);
                return true;
            }
        }
        type = type.BaseType;
    }
    return false;
}	
}
}