using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using LegacyText = UnityEngine.UI.Text;

namespace Armaphract.HarmonyXLocalization;

[BepInPlugin(Guid, Name, Version)]
public sealed class HarmonyXLocalizationPlugin : BasePlugin
{
    public const string Guid = "armaphract.harmonyx.unitintro";
    public const string Name = "Armaphract HarmonyX Localization";
    public const string Version = "1.9.74";

    private static ManualLogSource? Logger;
    private static bool CandidateLogged;
    private static readonly string MappingPath = Path.Combine(
        Paths.BepInExRootPath,
        "Translation",
        "zh-CN",
        "Text",
        "armaphract_zh-CN.txt");
    private static readonly List<MappingEntry> Mappings = new();
    private static readonly Dictionary<string, string> ExactMappingsOrdinal = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ExactMappingsIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, TextState> AppliedStates = new();
    private static readonly Dictionary<int, FontLayoutState> ObjectiveFontStates = new();
    private static readonly Dictionary<int, FontLayoutState> StatusOverlayFontStates = new();
    private static readonly Dictionary<int, FontLayoutState> PauseMenuButtonFontStates = new();
    private static readonly Dictionary<int, FontLayoutState> OptionsMenuFontStates = new();
    private static readonly Dictionary<int, MissionListFontState> MissionListFontStates = new();
    private static readonly HashSet<int> MissionListFontLoggedIds = new();
    private static readonly HashSet<int> OptionsMenuTextIds = new();
    private static readonly Dictionary<int, HorizontalAlignmentOptions> OptionsMenuTmpAlignments = new();
    private static readonly Dictionary<int, TextAnchor> OptionsMenuLegacyAlignments = new();
    private static readonly Dictionary<int, TextMeshFontLayoutState> OptionsMenuTextMeshStates = new();
    private static readonly Dictionary<int, TextAnchor> OptionsMenuTextMeshAnchors = new();
    private static readonly Dictionary<int, TextAlignment> OptionsMenuTextMeshAlignments = new();
    private static readonly Dictionary<int, TextMeshFontLayoutState> MenuTextMeshFontStates = new();
    private static readonly Dictionary<int, PanelTitleLayoutState> PanelTitleLayoutStates = new();
    private static readonly Dictionary<int, PanelTitleLayoutState> UnitActionButtonLayoutStates = new();
    private static readonly Dictionary<int, FontLayoutState> MotorPoolTitleFontStates = new();
    private static readonly Dictionary<int, FontLayoutState> UnitCardNameFontStates = new();
    private static readonly Dictionary<int, Vector2> MotorPoolTitlePositions = new();
    private static readonly HashSet<int> ModuleDetailTextLoggedIds = new();
    private static TMP_Text? CachedContractDetailText;
    private static readonly Dictionary<int, StatusOverlayTexts> StatusOverlayTextCache = new();
    private static readonly Queue<int> StatusOverlayTextCacheOrder = new();
    private static readonly HashSet<int> StatusOverlayComponentIds = new();
    private static readonly Dictionary<int, string> LastStatusOverlaySources = new();
    private static readonly Dictionary<int, string> LastProcessedTexts = new();
    // Setting TMP_Text.text from a one-shot scene translation re-enters the patched
    // setter.  Keep that write out of the translation pipeline; otherwise a
    // producer can race the scanner and make the same control alternate
    // between the source and a partially translated value.
    private static readonly HashSet<int> InternalTextWrites = new();
    private static readonly Dictionary<string, string> TranslationCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> TranslationCacheOrder = new();
    private static readonly HashSet<int> FrontLayoutLoggedIds = new();
    private static readonly HashSet<int> UiContextLoggedIds = new();
    private static readonly HashSet<int> ExitContextLoggedIds = new();
    private static readonly HashSet<int> PanelTitleContextLoggedIds = new();
    private static readonly HashSet<int> PauseMenuLayoutLoggedIds = new();
    private static readonly HashSet<string> CampaignDynamicTargetsLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ImGuiCandidatesLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ImGuiPanelTitlesLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ImGuiMenuCommandsLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> UiToolkitMenuCommandsLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MenuTranslationEntryTracesLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<int> MenuDisplayComponentsLogged = new();
    private static readonly HashSet<string> FragmentedTranslationsLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> UntranslatedCombatChatterLogged = new(StringComparer.Ordinal);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjectiveCounterRegex = new(
        @"\s*\[\d+\s*/\s*\d+\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RepairEtaRegex = new(
        @"^\s*repairs\s+complete\s+in\s+(\d+)\s+days?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NoAmmoForRegex = new(
        @"^\s*NO\s+AMMO\s+FOR\s+(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NoAmmoForTokenRegex = new(
        @"(?<![A-Za-z])NO\s+AMMO\s+FOR\s+(?<weapon>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MissingCrewRegex = new(
        @"^\s*MISSING\s+CREW\s*:\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LowEngineTorqueRegex = new(
        @"^\s*(?:LOW|INSUFFICIENT)\s+ENGINE\s+TORQUE\s*:\s*([0-9]+(?:\.[0-9]+)?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LowEnginePowerRegex = new(
        @"^\s*(?:LOW|INSUFFICIENT)\s+ENGINE\s+POWER\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EngineTorqueWarningTokenRegex = new(
        @"(?<![A-Za-z])(?:LOW|INSUFFICIENT)\s+ENGINE\s+TORQUE\s*:\s*([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PartiallyTranslatedEngineTorqueWarningRegex = new(
        @"发动机不足\s*TORQUE\s*:\s*([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EnginePowerWarningTokenRegex = new(
        @"(?<![A-Za-z])(?:LOW|INSUFFICIENT)\s+ENGINE\s+POWER(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PartiallyTranslatedEnginePowerWarningRegex = new(
        @"发动机不足\s*POWER(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LowAmmunitionWarningTokenRegex = new(
        @"(?<![A-Za-z])LOW[ \t]+(?:AMMUNITION|弹药)[ \t]*:[ \t]*(?<ammo>[^\r\n]+)(?<category>\r?\n[ \t]*\[[ \t]*(?:AMMUNITION|弹药)[ \t]*\])?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex RequiresRoleRegex = new(
        @"^\s*REQUIRES\s+(.+?)\s+ROLE\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BrokenStatusTokenRegex = new(
        @"(?<![A-Za-z])BROKEN(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DamagedStatusTokenRegex = new(
        @"(?<![A-Za-z])DAMAGED(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FailureStatusTokenRegex = new(
        @"(?<![A-Za-z])FAILURE(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReducedStatusTokenRegex = new(
        @"(?<![A-Za-z])REDUCED(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeStatusTokenRegex = new(
        @"(?<![A-Za-z])UNSAFE(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RoutedStatusTokenRegex = new(
        @"(?<![A-Za-z])ROUTED(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BleedoutStatusTokenRegex = new(
        @"(?<![A-Za-z])BLEEDOUT(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StunnedStatusTokenRegex = new(
        @"(?<![A-Za-z])STUNNED(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JammerStatusTokenRegex = new(
        @"(?<![A-Za-z])JAMMER(?=\s+(?:BROKEN|损坏)(?![A-Za-z]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SmokeStatusTokenRegex = new(
        @"(?<![A-Za-z])SMOKE(?=\s+(?:BROKEN|损坏)(?![A-Za-z]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PartiallyTranslatedSmokeStatusRegex = new(
        @"烟幕(?=\s+(?:BROKEN|损坏)(?![A-Za-z]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ChineseCharacterRegex = new(
        @"[\u3400-\u9fff]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LongEnglishRunRegex = new(
        @"(?:[A-Za-z]+\s+){4,}[A-Za-z]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContractCoordinateRegex = new(
        @"\(\s*\d+\s*,\s*\d+\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContractHighValueRegex = new(
        @"(?<![A-Za-z])HIGH(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContractMediumValueRegex = new(
        @"(?<![A-Za-z])(?:MEDIUM|MED)(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContractLowValueRegex = new(
        @"(?<![A-Za-z])LOW(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContractExtremeValueRegex = new(
        @"(?<![A-Za-z])EXTREME(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CampaignDayCounterRegex = new(
        @"^\s*(?:DAY|日)\s*[:：]?\s*(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CampaignDayFormatRegex = new(
        @"^\s*(?:DAY|日)\s*[:：]?\s*(\{0(?:[^}]*)\})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MainMenuVersionRegex = new(
        @"(?<![A-Za-z0-9])V0\.6\.3(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LocalizationCreditRegex = new(
        @"Instant[_ ]?Comet\s*汉化",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ModuleUiTokenRegex = new(
        @"(?<![A-Za-z])(?:INFANTRY REPLENISHMENT SECTION|ANTI-TANK MISSILE|MEDICAL MATERIALS|REPAIR MATERIALS|TURRET TRAVERSE|AMMUNITION REPLENISHMENT|AMMO REPLENISHMENT|ENGINE POWER|REVERSE GEAR|TURN SPEED|ACCELERATION|PROTECTION|COOLDOWN|APPLIQUE|REACTIVE|DURATION|TORQUE|RANGE|TYPE)(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReplenishmentClassTokenRegex = new(
        @"(?<![A-Za-z])(?:LIGHT|HEAVY|ADVANCED)(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DismountCountTokenRegex = new(
        @"(?<![A-Za-z0-9])(?<count>\d+)-MAN\s+DISMOUNT(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ExactUiOnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMMAND", "DRIVER", "GUNNER", "LOADER",
        "FAIR", "MEDIUM", "LOW", "WEAK", "FALLING",
        "STUNNED", "LIGHT", "HEAVY", "ADVANCED"
    };
    private static readonly HashSet<string> CaseSensitiveKeys = new(StringComparer.Ordinal)
    {
        // Same English text is used for two different contexts. Preserve
        // title case for the extraction objective and uppercase for the menu.
        "Exit", "EXIT",
        // Dynamic module statuses are emitted in uppercase. Keep this suffix
        // fallback case-sensitive so normal prose such as "broken mirror" is
        // handled only by its complete sentence mapping.
        "BROKEN", "DAMAGED", "FAILURE", "HIGH", "UNSAFE", "ROUTED", "BLEEDOUT", "JAMMER",
        "ASSAULT SQUAD", "MED",
        "TYPE", "INTERCEPTOR", "COOLDOWN", "RANGE", "USES", "ANGLE",
        "INFANTRY REPLENISHMENT SECTION", "MEDICAL MATERIALS", "REPAIR MATERIALS",
        "REACTIVE", "APPLIQUE", "PROTECTION", "ENGINE POWER", "ACCELERATION",
        "TORQUE", "TURRET TRAVERSE"
    };
    private static readonly Dictionary<string, string> ModuleUiTokenTranslations = new(StringComparer.Ordinal)
    {
        ["INFANTRY REPLENISHMENT SECTION"] = "步兵补员舱",
        ["ANTI-TANK MISSILE"] = "反坦克导弹",
        ["MEDICAL MATERIALS"] = "医疗物资",
        ["REPAIR MATERIALS"] = "维修物资",
        ["TURRET TRAVERSE"] = "炮塔转速",
        ["AMMUNITION REPLENISHMENT"] = "弹药补给",
        ["AMMO REPLENISHMENT"] = "弹药补给",
        ["ENGINE POWER"] = "发动机功率",
        ["REVERSE GEAR"] = "倒车挡",
        ["TURN SPEED"] = "转向速度",
        ["ACCELERATION"] = "加速度",
        ["PROTECTION"] = "防护",
        ["COOLDOWN"] = "冷却时间",
        ["APPLIQUE"] = "附加式",
        ["REACTIVE"] = "反应式",
        ["DURATION"] = "持续时间",
        ["TORQUE"] = "扭矩",
        ["RANGE"] = "射程",
        ["TYPE"] = "类型"
    };
    private static readonly Dictionary<string, string> ReplenishmentClassTranslations = new(StringComparer.Ordinal)
    {
        ["LIGHT"] = "轻型",
        ["HEAVY"] = "重型",
        ["ADVANCED"] = "高级"
    };
    private static readonly Dictionary<string, string> CrewRoleTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["COMMAND"] = "车长",
        ["COMMANDER"] = "车长",
        ["DRIVER"] = "驾驶员",
        ["GUNNER"] = "炮手",
        ["LOADER"] = "装填手"
    };
    private static readonly Dictionary<string, string> ModuleStatLabels = new(StringComparer.Ordinal)
    {
        ["ENGINEPOWER"] = "发动机功率",
        ["TURNSPEED"] = "转向速度",
        ["REVERSEGEAR"] = "倒车挡",
        ["ACCELERATION"] = "加速度",
        ["TORQUE"] = "扭矩",
        ["TURRETTRAV"] = "炮塔转速",
        ["TURRETTRAVERSE"] = "炮塔转速",
        ["炮塔转速ERSE"] = "炮塔转速"
    };
    private static DateTime MappingTimestampUtc;
    private static bool MappingsLoaded;
    private static FileSystemWatcher? MappingWatcher;
    private static volatile bool MappingReloadRequested;
    private static bool TranslationsEnabled = true;
    private static bool PanelActivationTranslationInProgress;
    private static bool SceneScanRequested;
    private static string ActiveSceneName = string.Empty;
    private const int TranslationCacheCapacity = 8192;
    private const float OptionsMenuFontScale = 1.10f;
    private const float MissionListFontSize = 16f;
    private static readonly HashSet<string> ObjectiveTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Exit", "Extract", "default objective", "intercept Convoy", "Exit region",
        "retreat", "Destroy Convoy", "assault", "Destroy supplies", "Defend",
        "Counterattack", "Destroy Garrison", "neutralize 4x S-92", "break out",
        "Destroy targets", "Eliminate Warlord"
    };
    private static readonly HashSet<string> ObjectiveDetails = new(StringComparer.OrdinalIgnoreCase)
    {
        "exit the region.", "extract anytime", "complete the objective",
        "intercept and destroy the logistic convoy rear.",
        "intercept and destroy the logistic convoy head.",
        "Exit the region via the extraction zone.",
        "Perform a general retreat once permission is given.",
        "Eliminate all convoy trucks", "eliminate half of the hostile forces",
        "eliminate the defenders", "Ambush enemy supply trucks",
        "Repel the enemy assaults.", "Eliminate 80% of the attacking forces",
        "Remove the defenders of the garrison outpost.",
        "destroy heavy assault guns (optional)",
        "Break out of the encirclement, exit the region via the extraction zone.",
        "Find and eliminate enemy command trucks", "eliminate the warlord"
    };
    private static readonly HashSet<string> ManualPauseLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANUAL", "PAUSED", "paused"
    };
    private static readonly HashSet<string> PauseMenuButtonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPTIONS", "SAVE AND EXIT", "EXIT",
        "选项", "保存并退出", "退出"
    };
    private static readonly HashSet<string> CampaignPanelTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPANY", "OPS", "SYSTEMS", "OPPOSITION FORCES",
        "DISTRICT / ENVIRON DATA", "DISTRICT/ENVIRON DATA",
        "AVAILABLE CONTRACTS", "DISTRICT INFO", "LOCATION DATA", "ORDERS",
        // GUI.Label/Button can translate the GUIContent before the nested
        // GUIStyle.Draw hook sees it, so recognize the final labels as well.
        "佣兵团", "运营", "系统", "敌对部队", "地区 / 环境数据", "地区/环境数据",
        "可用合约", "地区信息", "地点信息", "指令"
    };
    private static readonly HashSet<string> MainMenuPanelTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMMS", "通讯"
    };
    private static readonly HashSet<string> ManualGuideLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "MOVEMENT", "MOVE PATH", "REVERSE PATH", "AUTOPATH",
        "AIMING", "AIM TURRET", "POINT BODY", "CANCEL AIM",
        "HIGHLIGHT", "HIGHLIGHT UNIT", "HIGHLIGHT MULTIPLE",
        "移动", "移动方式", "绘制路径", "倒车路径", "自动寻路",
        "朝向控制", "炮塔朝向", "车体朝向", "取消朝向",
        "选择", "选择单位", "框选多个单位"
    };
    private const float ManualGuideImGuiFontScale = 1.6f;
    private const float StatusOverlayFontScale = 1.4f;
    private const float PauseMenuButtonFontScale = 1.5f;
    private const float MenuCommandButtonFontSize = 15f;
    private const float CampaignPanelTitleFontScale = 0.72f;
    private const float CampaignPanelTitleDownShiftScale = 0.07f;
    private const float UnitActionButtonFontScale = 0.72f;
    private const float UnitActionButtonDownShiftScale = 0.07f;
    private const float MotorPoolTitleFontScale = 0.64f;
    private const float MotorPoolTitleDownShiftScale = 0.15f;
    private const float UnitCardNameFontScale = 0.58f;
    private const string AsperaNameVerticalOffsetTag = "<voffset=-7px>";
    private const string ContractTitleVerticalOffsetTag = "<voffset=-5px>";
    private const string ContractDetailLabelLineHeightTag = "<line-height=95%>";
    private const string MainMenuSceneName = "0StartView";
    private static readonly HashSet<GUIStyle> ActiveManualGuideStyles = new();

    internal static readonly string Original =
        "The timeless iron hammer of the Imperial Army, the K-75 Celeres has endured through the  sandstorms of the early water crises, fought in ruins scorched by orbital ashfall, and endured bitter, senseless fighting across a thousand streets during the collapse.\n\n" +
        "Representing a paradigm shift in tank design philosophy during the height of expansionism, the K-75A eschewed superheavy armor in favor of weapon and countermeasure packages, improving mobility, logistics, and mission profile versatility. The resounding success of the design cemented the main battle tank as the core force in armored combat to this day. The aging K-75A fleet was undergoing a modernization program when the Coalition rebellion broke out.";

    internal static readonly string Translation =
        "帝国陆军历久不衰的铁锤——K-75 Celeres 经历过早期水资源危机的沙暴，在被轨道灰烬灼焦的废墟中作战，并在崩溃时期于千百条街道上熬过惨烈而毫无意义的战斗。\n\n" +
        "扩张主义鼎盛时期，K-75A 标志着坦克设计理念的一次范式转变：它舍弃超重型装甲，转而采用武器与对抗措施套件，由此提升机动性、后勤效率和任务适应性。这一设计大获成功，令主战坦克至今稳居装甲战核心力量之位。Coalition 叛乱爆发时，日渐老旧的 K-75A 车队正处于现代化改造计划之中。";

    private static readonly string FirstOriginal =
        "The timeless iron hammer of the Imperial Army, the K-75 Celeres has endured through the  sandstorms of the early water crises, fought in ruins scorched by orbital ashfall, and endured bitter, senseless fighting across a thousand streets during the collapse.";
    private static readonly string FirstTranslation =
        "帝国陆军历久不衰的铁锤——K-75 Celeres 经历过早期水资源危机的沙暴，在被轨道灰烬灼焦的废墟中作战，并在崩溃时期于千百条街道上熬过惨烈而毫无意义的战斗。";
    private static readonly string SecondOriginal =
        "Representing a paradigm shift in tank design philosophy during the height of expansionism, the K-75A eschewed superheavy armor in favor of weapon and countermeasure packages, improving mobility, logistics, and mission profile versatility. The resounding success of the design cemented the main battle tank as the core force in armored combat to this day. The aging K-75A fleet was undergoing a modernization program when the Coalition rebellion broke out.";
    private static readonly string SecondTranslation =
        "扩张主义鼎盛时期，K-75A 标志着坦克设计理念的一次范式转变：它舍弃超重型装甲，转而采用武器与对抗措施套件，由此提升机动性、后勤效率和任务适应性。这一设计大获成功，令主战坦克至今稳居装甲战核心力量之位。Coalition 叛乱爆发时，日渐老旧的 K-75A 车队正处于现代化改造计划之中。";

    public override void Load()
    {
        Logger = Log;
        ActiveSceneName = SceneManager.GetActiveScene().name;
        ReloadMappingsIfChanged(force: true);
        StartMappingWatcher();
        var harmony = new Harmony(Guid);
        harmony.Patch(
            AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text)),
            prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(TmpTextPrefix)));
        harmony.Patch(
            AccessTools.PropertySetter(typeof(LegacyText), nameof(LegacyText.text)),
            prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(LegacyTextPrefix)));
        harmony.Patch(
            AccessTools.PropertySetter(typeof(TextMesh), nameof(TextMesh.text)),
            prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(TextMeshTextPrefix)));
        PatchTmpStringWriters(harmony);
        PatchPanelActivation(harmony);
        PatchPauseMenu(harmony);
        PatchOptionsMenu(harmony);
        PatchCampaignMissionData(harmony);
        PatchUnitCardLayout(harmony);
        PatchArmoryModuleData(harmony);
        PatchInventoryModuleData(harmony);
        PatchSceneLoaded(harmony);
        PatchCombatChatter(harmony);
        PatchUnitStatusWriters(harmony);
        PatchScreenStatusWriters(harmony);
        PatchUiToolkitTextWriters(harmony);
        PatchImGuiTextMethods(harmony);
        AddComponent<RefreshComponent>();
        Log.LogInfo($"HarmonyX localization patch loaded for TMP_Text, UnityEngine.UI.Text, and Unity IMGUI text; status overlay font scale {StatusOverlayFontScale:0.0}x, objective font layout and Alt+T toggle enabled.");
    }

    private static void TmpTextPrefix(TMP_Text __instance, ref string value)
    {
        TextPrefix(__instance, ref value);
    }

    private static void LegacyTextPrefix(LegacyText __instance, ref string value)
    {
        TextPrefix(__instance, ref value);
    }

    private static void TextMeshTextPrefix(TextMesh __instance, ref string value)
    {
        TextPrefix(__instance, ref value);
    }

    private static void PatchTmpStringWriters(Harmony harmony)
    {
        var stringPrefix = new HarmonyMethod(
            typeof(HarmonyXLocalizationPlugin), nameof(TmpStringWriterStringPrefix));
        var builderPrefix = new HarmonyMethod(
            typeof(HarmonyXLocalizationPlugin), nameof(TmpStringWriterBuilderPrefix));
        var patched = 0;
        foreach (var method in typeof(TMP_Text).GetMethods(
                     System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic).Where(method =>
                     (method.Name == nameof(TMP_Text.SetText) ||
                      method.Name == "SetTextInternal") &&
                     method.GetParameters().Length > 0 &&
                     (method.GetParameters()[0].ParameterType == typeof(string) ||
                      method.GetParameters()[0].ParameterType == typeof(Il2CppSystem.Text.StringBuilder))))
        {
            try
            {
                var firstParameterType = method.GetParameters()[0].ParameterType;
                harmony.Patch(
                    method,
                    prefix: firstParameterType == typeof(string) ? stringPrefix : builderPrefix);
                patched++;
            }
            catch (System.Exception ex)
            {
                Logger?.LogWarning($"Could not patch TMP string writer {method}: {ex.Message}");
            }
        }
        Logger?.LogInfo($"Patched {patched} TMP SetText string overloads.");
    }

    private static void TmpStringWriterStringPrefix(TMP_Text __instance, ref string __0)
    {
        if (__0 == null)
            return;
        TextPrefix(__instance, ref __0);
    }

    private static void TmpStringWriterBuilderPrefix(
        TMP_Text __instance,
        ref Il2CppSystem.Text.StringBuilder __0)
    {
        if (__0 == null)
            return;

        var original = __0.ToString();
        var value = original;
        TextPrefix(__instance, ref value);
        if (!string.Equals(original, value, StringComparison.Ordinal))
            __0 = new Il2CppSystem.Text.StringBuilder(value);
    }

    private static void PatchPanelActivation(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) }),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(GameObjectSetActivePostfix)));
        Logger?.LogInfo("Patched GameObject.SetActive for event-driven panel translation.");
    }

    private static void PatchPauseMenu(Harmony harmony)
    {
        var patched = 0;
        foreach (var methodName in new[] { nameof(UIMainMenu.ToggleMainMenu), nameof(UIMainMenu.ToggleMainMenuButton) })
        {
            var method = AccessTools.Method(typeof(UIMainMenu), methodName, Type.EmptyTypes);
            if (method == null)
            {
                Logger?.LogWarning($"Could not find UIMainMenu.{methodName}().");
                continue;
            }

            harmony.Patch(
                method,
                postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(PauseMenuTogglePostfix)));
            patched++;
        }
        Logger?.LogInfo($"Patched {patched} UIMainMenu toggle methods for targeted pause-menu button layout.");
    }

    private static void PatchOptionsMenu(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(UIMainMenu), nameof(UIMainMenu.ToggleOptions), Type.EmptyTypes);
        if (method == null)
        {
            Logger?.LogWarning("Could not find UIMainMenu.ToggleOptions().");
            return;
        }
        harmony.Patch(
            method,
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(OptionsMenuTogglePostfix)));
        Logger?.LogInfo("Patched UIMainMenu.ToggleOptions for left-column layout.");
    }

    private static void OptionsMenuTogglePostfix(UIMainMenu __instance)
    {
        if (!TranslationsEnabled)
            return;

        var matched = 0;
        foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!IsOptionsLabelsBlockPath(text))
                continue;

            // The options block starts in English and can be inactive during
            // the scene scan. Translate it at the exact open/close boundary,
            // then apply layout in the same frame. Do not require a previous
            // Alt+T/global scan to make the content recognizable first.
            TranslateCurrentComponent(text);
            if (!IsOptionsLabelsBlock(text))
                continue;

            RegisterAndApplyOptionsMenuFont(text);
            matched++;
        }
        Logger?.LogInfo($"Options scene-path layout applied: matched={matched}.");
    }

    private static bool IsOptionsLabelsBlock(TMP_Text text)
    {
        var plain = PlainText(text.text);
        if (!plain.Contains('\n'))
            return false;

        var english = plain.Contains("MASTER", StringComparison.OrdinalIgnoreCase) &&
                      plain.Contains("MUSIC", StringComparison.OrdinalIgnoreCase) &&
                      plain.Contains("CRT", StringComparison.OrdinalIgnoreCase) &&
                      plain.Contains("MOUSE CONTROLS", StringComparison.OrdinalIgnoreCase);
        var chinese = plain.Contains("总音量", StringComparison.Ordinal) &&
                      plain.Contains("CRT", StringComparison.OrdinalIgnoreCase) &&
                      plain.Contains("鼠标操作", StringComparison.Ordinal);
        return english || chinese;
    }

    private static bool IsOptionsLabelsBlockPath(TMP_Text text)
    {
        return text.name.Equals("text", StringComparison.OrdinalIgnoreCase) &&
               text.transform.parent != null &&
               text.transform.parent.name.Equals("options", StringComparison.OrdinalIgnoreCase) &&
               HasAncestorNamed(text.transform, "Menu", 4);
    }

    private static bool IsOptionsLeftLabel(string value)
    {
        var plain = PlainText(value);
        return plain.Equals("SOUND", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("声音", StringComparison.Ordinal) ||
               plain.Equals("MASTER", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("总音量", StringComparison.Ordinal) ||
               plain.Equals("MUSIC", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("音乐", StringComparison.Ordinal) ||
               plain.Equals("SFX", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("音效", StringComparison.Ordinal) ||
               plain.Equals("UI", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("界面", StringComparison.Ordinal) ||
               plain.Equals("界面音效", StringComparison.Ordinal) ||
               plain.Equals("ZOOM LENS BLUR", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("变焦镜头模糊", StringComparison.Ordinal) ||
               plain.Equals("CRT DISPLAY EFFECT", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("CRT 显示效果", StringComparison.Ordinal) ||
               plain.Equals("SLOW TIME WITH MOUSE CONTROLS", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("鼠标操作时减缓时间", StringComparison.Ordinal);
    }

    private static void RegisterAndApplyOptionsMenuFont(Component component)
    {
        OptionsMenuTextIds.Add(component.GetInstanceID());
        ApplyOptionsMenuFontLayout(component);
    }

    private static void ApplyOptionsMenuFontLayout(Component component)
    {
        var instanceId = component.GetInstanceID();
        if (!OptionsMenuTextIds.Contains(instanceId))
            return;

        if (component is TMP_Text tmp)
        {
            if (!OptionsMenuFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(tmp.fontSize, null);
                OptionsMenuFontStates[instanceId] = state;
                OptionsMenuTmpAlignments[instanceId] = tmp.horizontalAlignment;
                Logger?.LogInfo(
                    $"Options label layout: text={PlainText(tmp.text)}, font={tmp.fontSize:0.##}->{tmp.fontSize * OptionsMenuFontScale:0.##}, align=Right, path={BuildTransformPath(tmp.transform)}");
            }
            tmp.fontSize = state.TmpFontSize!.Value * OptionsMenuFontScale;
            tmp.horizontalAlignment = HorizontalAlignmentOptions.Right;
        }
        else if (component is LegacyText legacy)
        {
            if (!OptionsMenuFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(null, legacy.fontSize);
                OptionsMenuFontStates[instanceId] = state;
                OptionsMenuLegacyAlignments[instanceId] = legacy.alignment;
            }
            legacy.fontSize = Mathf.RoundToInt(state.LegacyFontSize!.Value * OptionsMenuFontScale);
            legacy.alignment = TextAnchor.MiddleRight;
        }
        else if (component is TextMesh textMesh)
        {
            if (!OptionsMenuTextMeshStates.TryGetValue(instanceId, out var state))
            {
                state = new TextMeshFontLayoutState(textMesh.fontSize, textMesh.characterSize);
                OptionsMenuTextMeshStates[instanceId] = state;
                OptionsMenuTextMeshAnchors[instanceId] = textMesh.anchor;
                OptionsMenuTextMeshAlignments[instanceId] = textMesh.alignment;
                Logger?.LogInfo(
                    $"Options label layout: type=TextMesh, text={PlainText(textMesh.text)}, font={textMesh.fontSize}, characterSize={textMesh.characterSize:0.###}->{textMesh.characterSize * OptionsMenuFontScale:0.###}, align=Right, path={BuildTransformPath(textMesh.transform)}");
            }
            textMesh.characterSize = state.CharacterSize * OptionsMenuFontScale;
            textMesh.anchor = TextAnchor.MiddleRight;
            textMesh.alignment = TextAlignment.Right;
        }
    }

    private static void PauseMenuTogglePostfix(UIMainMenu __instance)
    {
        if (!TranslationsEnabled || __instance == null || __instance.mainMenu == null ||
            !__instance.mainMenu.activeInHierarchy)
            return;

        var root = __instance.mainMenu;
        TranslateHierarchy(root);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            ApplyPauseMenuButtonFontLayout(text, text.text, knownPauseMenuTarget: true);
        foreach (var text in root.GetComponentsInChildren<LegacyText>(true))
            ApplyPauseMenuButtonFontLayout(text, text.text, knownPauseMenuTarget: true);
    }

    private static void PatchCampaignMissionData(Harmony harmony)
    {
        PatchCampaignUiMethod(
            harmony,
            nameof(CampaignUIManager.UpdateTime),
            new[] { typeof(CampaignData) },
            nameof(CampaignTimePostfix));
        PatchCampaignUiMethod(
            harmony,
            nameof(CampaignUIManager.OpenMissionData),
            new[] { typeof(MissionData) },
            nameof(CampaignMissionDataPostfix));
        PatchCampaignUiMethod(
            harmony,
            "OnHoverEnter",
            new[] { typeof(MissionNode) },
            nameof(CampaignNodeDataPostfix));
        PatchCampaignUiMethod(
            harmony,
            nameof(CampaignUIManager.NodeView),
            new[] { typeof(CampaignObject) },
            nameof(CampaignNodeDataPostfix));

        PatchCampaignUiMethod(
            harmony,
            nameof(CampaignUIManager.SetupAlldataMissionNode),
            new[] { typeof(MissionNode) },
            nameof(CampaignMissionListNodePostfix));
        PatchCampaignUiMethod(
            harmony,
            nameof(CampaignUIManager.LateUpdate),
            Type.EmptyTypes,
            nameof(CampaignLateUpdatePostfix));
    }

    private static void PatchCampaignUiMethod(
        Harmony harmony,
        string methodName,
        Type[] argumentTypes,
        string postfixName)
    {
        var method = AccessTools.Method(typeof(CampaignUIManager), methodName, argumentTypes);
        if (method == null)
        {
            Logger?.LogWarning($"Could not find CampaignUIManager.{methodName}.");
            return;
        }

        harmony.Patch(
            method,
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), postfixName));
        Logger?.LogInfo($"Patched CampaignUIManager.{methodName} for targeted campaign UI translation.");
    }

    private static void PatchUnitCardLayout(Harmony harmony)
    {
        var initializeMethod = AccessTools.Method(
            typeof(UICardInstanceButton), nameof(UICardInstanceButton.Initialize));
        if (initializeMethod == null)
        {
            Logger?.LogWarning("Could not find UICardInstanceButton.Initialize for unit-card name layout.");
        }
        else
        {
            harmony.Patch(
                initializeMethod,
                postfix: new HarmonyMethod(
                    typeof(HarmonyXLocalizationPlugin), nameof(UnitCardInitializePostfix)));
        }

        var finalCardMethod = AccessTools.Method(
            typeof(ArmoryManager), nameof(ArmoryManager.UnitCardSet),
            new[] { typeof(UICardInstanceButton), typeof(UnitInstance), typeof(bool) });
        if (finalCardMethod == null)
        {
            Logger?.LogWarning("Could not find ArmoryManager.UnitCardSet for final unit-card layout.");
        }
        else
        {
            harmony.Patch(
                finalCardMethod,
                postfix: new HarmonyMethod(
                    typeof(HarmonyXLocalizationPlugin), nameof(ArmoryUnitCardSetPostfix)));
        }

        var loadArmoryMethod = AccessTools.Method(
            typeof(ArmoryManager), nameof(ArmoryManager.LoadArmory), Type.EmptyTypes);
        if (loadArmoryMethod == null)
        {
            Logger?.LogWarning("Could not find ArmoryManager.LoadArmory for final unit-library title layout.");
        }
        else
        {
            harmony.Patch(
                loadArmoryMethod,
                postfix: new HarmonyMethod(
                    typeof(HarmonyXLocalizationPlugin), nameof(ArmoryLoadPostfix)));
        }
        Logger?.LogInfo(
            "Patched armory load and unit-card population events for compact unit-library text.");
    }

    private static void UnitCardInitializePostfix(UICardInstanceButton __instance)
    {
        if (!TranslationsEnabled || __instance?.texts == null)
            return;

        foreach (var text in __instance.texts)
        {
            if (text == null)
                continue;
            TranslateCurrentComponent(text);
            ApplyUnitCardNameFontLayout(text, text.text);
        }
    }

    private static void ArmoryUnitCardSetPostfix(UICardInstanceButton __0)
    {
        if (!TranslationsEnabled || __0 == null)
            return;

        // UnitCardSet creates and fills crewMiniNew after Initialize has
        // returned. Apply the layout at that final producer boundary so the
        // game cannot overwrite it later; no periodic layout enforcement is
        // needed.
        foreach (var text in __0.gameObject.GetComponentsInChildren<TMP_Text>(true))
        {
            TranslateCurrentComponent(text);
            ApplyUnitCardNameFontLayout(text, text.text);
        }
        foreach (var text in __0.gameObject.GetComponentsInChildren<LegacyText>(true))
        {
            TranslateCurrentComponent(text);
            ApplyUnitCardNameFontLayout(text, text.text);
        }
    }

    private static void ArmoryLoadPostfix(ArmoryManager __instance)
    {
        if (!TranslationsEnabled || __instance == null)
            return;

        // LoadArmory is the final synchronous setup event for the library
        // heading. Re-run the normal translation/layout pipeline once here in
        // case Start or prefab setup restored its original metrics.
        TranslateHierarchy(__instance.gameObject);
    }

    private static void PatchArmoryModuleData(Harmony harmony)
    {
        var method = AccessTools.Method(
            typeof(ArmoryModule), nameof(ArmoryModule.DisplayModuleData),
            new[] { typeof(UnitModule) });
        if (method == null)
        {
            Logger?.LogWarning("Could not find ArmoryModule.DisplayModuleData for module-detail localization.");
            return;
        }

        harmony.Patch(
            method,
            postfix: new HarmonyMethod(
                typeof(HarmonyXLocalizationPlugin), nameof(ArmoryModuleDataPostfix)));
        Logger?.LogInfo("Patched ArmoryModule.DisplayModuleData for complete dynamic module-detail localization.");
    }

    private static void ArmoryModuleDataPostfix(ArmoryModule __instance)
    {
        if (!TranslationsEnabled || __instance?.moduleData == null)
            return;
        TranslateUiParamObject(__instance.moduleData, 0);
    }

    private static void PatchInventoryModuleData(Harmony harmony)
    {
        var postfix = new HarmonyMethod(
            typeof(HarmonyXLocalizationPlugin), nameof(InventoryModuleDataPostfix));
        var patched = 0;
        foreach (var method in new[]
                 {
                     AccessTools.Method(
                         typeof(InventoryItemModule), nameof(InventoryItemModule.SetModule),
                         new[] { typeof(UnitModule), typeof(float) }),
                     AccessTools.Method(
                         typeof(InventoryItemModule), nameof(InventoryItemModule.SetModuleInstance),
                         new[] { typeof(ModuleInstance), typeof(float) })
                 })
        {
            if (method == null)
                continue;
            harmony.Patch(method, postfix: postfix);
            patched++;
        }
        Logger?.LogInfo(
            $"Patched {patched} InventoryItemModule setup methods for module-card detail localization.");
    }

    private static void InventoryModuleDataPostfix(InventoryItemModule __instance)
    {
        if (!TranslationsEnabled || __instance?.uiParams == null)
            return;
        TranslateUiParamObject(__instance.uiParams, 0);
    }

    private static void TranslateUiParamObject(UIParamObject ui, int depth)
    {
        TranslateUiParamObject(ui, depth, ContainsReplenishmentModuleUi(ui, depth));
    }

    private static void TranslateUiParamObject(
        UIParamObject ui,
        int depth,
        bool replenishmentContext)
    {
        if (ui == null || depth > 4)
            return;

        if (ui.texts != null)
        {
            foreach (var text in ui.texts)
            {
                if (text == null)
                    continue;
                var beforeTranslation = text.text;
                var isCandidate = IsModuleDetailTextCandidate(beforeTranslation);
                if (isCandidate && ModuleDetailTextLoggedIds.Add(text.GetInstanceID()))
                {
                    Logger?.LogInfo(
                        $"Module detail text captured: text={PlainText(beforeTranslation)}, path={BuildTransformPath(text.transform)}");
                }
                if (replenishmentContext &&
                    ReplenishmentClassTranslations.TryGetValue(
                        PlainText(beforeTranslation), out var translatedClass))
                {
                    SetComponentText(text, beforeTranslation, translatedClass);
                }
                else
                {
                    TranslateCurrentComponent(text);
                }
            }
        }
        if (ui.uiParamObjects == null)
            return;
        foreach (var child in ui.uiParamObjects)
            TranslateUiParamObject(child, depth + 1, replenishmentContext);
    }

    private static bool ContainsReplenishmentModuleUi(UIParamObject ui, int depth)
    {
        if (ui == null || depth > 4)
            return false;
        if (ui.texts != null)
        {
            foreach (var text in ui.texts)
            {
                if (text == null)
                    continue;
                var plain = PlainText(text.text);
                if (plain.Contains("MEDICAL MATERIALS", StringComparison.Ordinal) ||
                    plain.Contains("REPAIR MATERIALS", StringComparison.Ordinal) ||
                    plain.Contains("医疗物资", StringComparison.Ordinal) ||
                    plain.Contains("维修物资", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        if (ui.uiParamObjects == null)
            return false;
        foreach (var child in ui.uiParamObjects)
        {
            if (ContainsReplenishmentModuleUi(child, depth + 1))
                return true;
        }
        return false;
    }

    private static bool IsModuleDetailTextCandidate(string value)
    {
        if (ModuleUiTokenRegex.IsMatch(value) || DismountCountTokenRegex.IsMatch(value))
            return true;
        return ReplenishmentClassTokenRegex.IsMatch(value) &&
               (value.Contains("医疗物资", StringComparison.Ordinal) ||
                value.Contains("维修物资", StringComparison.Ordinal) ||
                value.Contains("弹药补给", StringComparison.Ordinal));
    }

    private static void CampaignTimePostfix(CampaignUIManager __instance)
    {
        if (!TranslationsEnabled || __instance == null)
            return;

        TranslateCampaignDayCounters(__instance.districtUI);
        TranslateCampaignDayCounters(__instance.mapUI);
        TranslateCampaignDayCounters(__instance.districtBasicInfo?.gameObject);
        TranslateCampaignDayCounters(__instance.zoomedOutUI?.gameObject);
        TranslateCampaignDayCounters(__instance.zoomedInUI?.gameObject);
        TranslateCampaignDayCounters(__instance.actions?.gameObject);
        TranslateDistrictDateFormat(__instance.districtUI);
    }

    private static void CampaignMissionDataPostfix(CampaignUIManager __instance)
    {
        if (!TranslationsEnabled || __instance == null)
            return;

        CacheAndTranslateContractDetailTexts(__instance.gameObject);
    }

    private static void CampaignNodeDataPostfix(CampaignUIManager __instance)
    {
        if (!TranslationsEnabled || __instance == null || __instance.nodeData == null)
            return;

        CacheAndTranslateContractDetailTexts(__instance.gameObject);
    }

    private static void CampaignMissionListNodePostfix(CampaignUIManager __instance)
    {
        if (!TranslationsEnabled || __instance?.allMissionData == null)
            return;

        foreach (var text in __instance.allMissionData.gameObject.GetComponentsInChildren<TMP_Text>(true))
            ApplyMissionListTextFont(text);
    }

    private static void CampaignLateUpdatePostfix(CampaignUIManager __instance)
    {
        if (!TranslationsEnabled || __instance == null || CachedContractDetailText == null)
            return;

        // CampaignUIManager.LateUpdate is the native producer that formats the
        // live danger value directly into TMP's rendered buffer. Translate at
        // that exact write boundary rather than polling from our own Update.
        var text = CachedContractDetailText;
        if (text != null && text.gameObject.activeInHierarchy)
            TranslateContractDangerValue(text, text.text);
    }

    private static void ApplyMissionListTextFont(TMP_Text? text)
    {
        if (!TranslationsEnabled || text == null ||
            !HasAncestorNamed(text.transform, "allMissionData", 14))
            return;

        var instanceId = text.GetInstanceID();
        var isMissionCard = HasAncestorContaining(text.transform, "MissionMiniCard", 5);
        var isBodyColumn = isMissionCard &&
                           (text.name.Equals("days", StringComparison.OrdinalIgnoreCase) ||
                            text.name.Equals("time", StringComparison.OrdinalIgnoreCase) ||
                            text.name.Equals("missionType", StringComparison.OrdinalIgnoreCase));
        if (MissionListFontLoggedIds.Add(instanceId))
        {
            Logger?.LogInfo(
                $"All-mission-data text: target={isBodyColumn}, text={PlainText(text.text)}, font={text.fontSize:0.##}, autoSize={text.enableAutoSizing}, path={BuildTransformPath(text.transform)}");
        }
        if (!isBodyColumn)
            return;

        if (!MissionListFontStates.TryGetValue(instanceId, out var state))
        {
            state = new MissionListFontState(
                text.fontSize,
                text.enableAutoSizing,
                text.fontSizeMin,
                text.fontSizeMax);
            MissionListFontStates[instanceId] = state;
        }

        text.fontSize = MissionListFontSize;
        if (state.AutoSizing)
        {
            var scale = MissionListFontSize / state.FontSize;
            text.fontSizeMin = state.FontSizeMin * scale;
            text.fontSizeMax = state.FontSizeMax * scale;
        }
    }

    private static void CacheAndTranslateContractDetailTexts(GameObject root)
    {
        if (CachedContractDetailText != null)
        {
            TranslateContractDangerValue(CachedContractDetailText, CachedContractDetailText.text);
            return;
        }

        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!IsContractDetailTextPath(text))
                continue;
            CachedContractDetailText = text;
            TranslateContractDangerValue(text, text.text);
            break;
        }
    }

    private static void PatchSceneLoaded(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(SceneManager), "Internal_SceneLoaded"),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(SceneLoadedPostfix)));
        Logger?.LogInfo("Patched SceneManager.Internal_SceneLoaded for one-shot scene translation.");
    }

    private static void SceneLoadedPostfix()
    {
        ActiveSceneName = SceneManager.GetActiveScene().name;
        ClearSceneState();
        SceneScanRequested = true;
        Logger?.LogInfo($"Scene loaded: {ActiveSceneName}");
    }

    private static void GameObjectSetActivePostfix(GameObject __instance, bool __0)
    {
        if (!__0 || !TranslationsEnabled || PanelActivationTranslationInProgress ||
            __instance == null || !__instance.activeInHierarchy ||
            !IsLikelyUiActivation(__instance.transform))
            return;
        PanelActivationTranslationInProgress = true;
        try
        {
            TranslateHierarchy(__instance);
        }
        finally
        {
            PanelActivationTranslationInProgress = false;
        }
    }

    private static bool IsLikelyUiActivation(Transform transform)
    {
        if (transform is RectTransform)
            return true;

        // Some of the game's TMP menus are world-space objects with ordinary
        // Transforms (not RectTransforms). Retain the cheap activation filter,
        // but admit known UI/menu hierarchies so those panels are translated
        // before their first rendered frame.
        Transform? current = transform;
        var depth = 0;
        while (current != null && depth++ < 12)
        {
            var name = current.name;
            if (name.Equals("UI", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("UI", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Canvas", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Menu", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static bool TranslateHierarchy(GameObject root)
    {
        var hasText = false;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            hasText = true;
            TranslateCurrentComponent(text);
        }
        foreach (var text in root.GetComponentsInChildren<LegacyText>(true))
        {
            hasText = true;
            TranslateCurrentComponent(text);
        }
        foreach (var text in root.GetComponentsInChildren<TextMesh>(true))
        {
            hasText = true;
            TranslateCurrentComponent(text);
        }
        return hasText;
    }

    private static void TranslateCampaignDayCounters(GameObject? root)
    {
        if (root == null)
            return;

        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            TranslateCampaignDayCounter(text, text.text);
        foreach (var text in root.GetComponentsInChildren<LegacyText>(true))
            TranslateCampaignDayCounter(text, text.text);
        foreach (var text in root.GetComponentsInChildren<TextMesh>(true))
            TranslateCampaignDayCounter(text, text.text);
    }

    private static void TranslateCampaignDayCounter(Component component, string value)
    {
        var plain = PlainText(value);
        var match = CampaignDayCounterRegex.Match(plain);
        string replacement;
        if (match.Success)
        {
            replacement = $"第{match.Groups[1].Value}天";
        }
        else
        {
            var formatMatch = CampaignDayFormatRegex.Match(plain);
            if (!formatMatch.Success)
                return;
            replacement = $"第{formatMatch.Groups[1].Value}天";
        }
        var translated = value.Contains(plain, StringComparison.Ordinal)
            ? value.Replace(plain, replacement, StringComparison.Ordinal)
            : replacement;
        LogCampaignDynamicTargetOnce("day", component, plain, translated);
        SetComponentText(component, value, translated);
    }

    private static void TranslateContractDangerValue(Component component, string value)
    {
        var source = value;
        if (component is TMP_Text tmpText)
        {
            // TMP's formatted writers can leave .text pointing at the source
            // template while the actually rendered character buffer already
            // contains values such as HIGH. GetParsedText reads that buffer.
            var parsed = tmpText.GetParsedText();
            if (!string.IsNullOrEmpty(parsed) &&
                (ContractLowValueRegex.IsMatch(parsed) ||
                 ContractMediumValueRegex.IsMatch(parsed) ||
                 ContractHighValueRegex.IsMatch(parsed) ||
                 ContractExtremeValueRegex.IsMatch(parsed)))
            {
                source = parsed;
            }
        }

        if (!ContractLowValueRegex.IsMatch(source) &&
            !ContractMediumValueRegex.IsMatch(source) &&
            !ContractHighValueRegex.IsMatch(source) &&
            !ContractExtremeValueRegex.IsMatch(source))
            return;

        var translated = ContractExtremeValueRegex.Replace(source, "极高");
        translated = ContractHighValueRegex.Replace(translated, "高");
        translated = ContractMediumValueRegex.Replace(translated, "中");
        translated = ContractLowValueRegex.Replace(translated, "低");
        if (string.Equals(source, translated, StringComparison.Ordinal))
            return;

        LogCampaignDynamicTargetOnce("danger", component, PlainText(source), PlainText(translated));
        SetComponentText(component, value, translated);
    }

    private static void LogCampaignDynamicTargetOnce(
        string kind,
        Component component,
        string source,
        string translated)
    {
        var key = $"{kind}:{component.GetInstanceID()}";
        if (CampaignDynamicTargetsLogged.Add(key))
        {
            Logger?.LogInfo(
                $"Campaign dynamic target: kind={kind}, text={source}->{translated}, path={BuildTransformPath(component.transform)}");
        }
    }

    private static string BuildTransformPath(Transform transform)
    {
        var names = new List<string>();
        Transform? current = transform;
        var depth = 0;
        while (current != null && depth++ < 20)
        {
            names.Add(current.name);
            current = current.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static bool IsDistrictDateNumber(TMP_Text text)
    {
        return text.name.Equals("day", StringComparison.OrdinalIgnoreCase) &&
               HasAncestorNamed(text.transform, "environmentData", 4) &&
               HasAncestorContaining(text.transform, "DistrictUI", 8);
    }

    private static bool IsDistrictDateLabel(TMP_Text text)
    {
        if (!HasAncestorNamed(text.transform, "environmentData", 4) ||
            !HasAncestorContaining(text.transform, "DistrictUI", 8))
            return false;
        var plain = PlainText(text.text);
        return plain.Equals("DAY:", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("DAY：", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("日:", StringComparison.Ordinal) ||
               plain.Equals("日：", StringComparison.Ordinal) ||
               plain.Equals("第", StringComparison.Ordinal);
    }

    private static void TranslateDistrictDateFormat(GameObject? root)
    {
        if (root == null)
            return;

        foreach (var label in root.GetComponentsInChildren<TMP_Text>(true).Where(IsDistrictDateLabel))
        {
            if (string.Equals(PlainText(label.text), "第", StringComparison.Ordinal))
                continue;
            SetComponentText(label, label.text, "第");
            LogCampaignDynamicTargetOnce("district-date-label", label, "日：", "第");
        }

        foreach (var number in root.GetComponentsInChildren<TMP_Text>(true).Where(IsDistrictDateNumber))
        {
            var plain = PlainText(number.text);
            var digitCount = CountLeadingDigits(plain);
            if (digitCount == 0)
                continue;
            var replacement = $"{plain[..digitCount]}天";
            if (string.Equals(plain, replacement, StringComparison.Ordinal))
                continue;
            SetComponentText(number, number.text, replacement);
            LogCampaignDynamicTargetOnce("district-date-number", number, plain, replacement);
        }
    }

    private static bool IsContractDetailTextPath(Component component)
    {
        return component.name.Equals("contractText", StringComparison.OrdinalIgnoreCase) &&
               HasAncestorNamed(component.transform, "Rectangle", 4) &&
               HasAncestorNamed(component.transform, "missionDataUI", 6);
    }

    private static bool HasAncestorNamed(Transform transform, string name, int maxDepth)
    {
        Transform? current = transform;
        var depth = 0;
        while (current != null && depth++ < maxDepth)
        {
            if (current.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
            current = current.parent;
        }
        return false;
    }

    private static bool HasAncestorContaining(Transform transform, string value, int maxDepth)
    {
        Transform? current = transform;
        var depth = 0;
        while (current != null && depth++ < maxDepth)
        {
            if (current.name.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
            current = current.parent;
        }
        return false;
    }

    private static int CountLeadingDigits(string value)
    {
        var count = 0;
        while (count < value.Length && char.IsDigit(value[count]))
            count++;
        return count;
    }

    private static void ClearSceneState()
    {
        AppliedStates.Clear();
        ObjectiveFontStates.Clear();
        StatusOverlayFontStates.Clear();
        PauseMenuButtonFontStates.Clear();
        OptionsMenuFontStates.Clear();
        MissionListFontStates.Clear();
        MissionListFontLoggedIds.Clear();
        OptionsMenuTextIds.Clear();
        OptionsMenuTmpAlignments.Clear();
        OptionsMenuLegacyAlignments.Clear();
        OptionsMenuTextMeshStates.Clear();
        OptionsMenuTextMeshAnchors.Clear();
        OptionsMenuTextMeshAlignments.Clear();
        MenuTextMeshFontStates.Clear();
        PanelTitleLayoutStates.Clear();
        UnitActionButtonLayoutStates.Clear();
        MotorPoolTitleFontStates.Clear();
        UnitCardNameFontStates.Clear();
        MotorPoolTitlePositions.Clear();
        ModuleDetailTextLoggedIds.Clear();
        CachedContractDetailText = null;
        StatusOverlayTextCache.Clear();
        StatusOverlayTextCacheOrder.Clear();
        StatusOverlayComponentIds.Clear();
        LastStatusOverlaySources.Clear();
        LastProcessedTexts.Clear();
        InternalTextWrites.Clear();
        FrontLayoutLoggedIds.Clear();
        UiContextLoggedIds.Clear();
        ExitContextLoggedIds.Clear();
        PanelTitleContextLoggedIds.Clear();
        PauseMenuLayoutLoggedIds.Clear();
        CampaignDynamicTargetsLogged.Clear();
        ImGuiCandidatesLogged.Clear();
        ImGuiPanelTitlesLogged.Clear();
        ImGuiMenuCommandsLogged.Clear();
        UiToolkitMenuCommandsLogged.Clear();
        MenuTranslationEntryTracesLogged.Clear();
        MenuDisplayComponentsLogged.Clear();
        FragmentedTranslationsLogged.Clear();
        UntranslatedCombatChatterLogged.Clear();
        CandidateLogged = false;
    }

    private static void PatchCombatChatter(Harmony harmony)
    {
        var method = AccessTools.Method(
            typeof(UIStream),
            "GetRandomChatterLine",
            new[] { typeof(CrewMemberTemplate), typeof(string) });
        if (method == null)
        {
            Logger?.LogWarning("Could not find UIStream.GetRandomChatterLine for combat chatter localization.");
            return;
        }

        harmony.Patch(
            method,
            postfix: new HarmonyMethod(
                typeof(HarmonyXLocalizationPlugin), nameof(CombatChatterPostfix)));
        Logger?.LogInfo("Patched UIStream.GetRandomChatterLine before chatter placeholder expansion.");
    }

    private static void CombatChatterPostfix(string __1, ref string __result)
    {
        if (!TranslationsEnabled || string.IsNullOrWhiteSpace(__result))
            return;

        // One upstream YAML entry has a trailing space. Normalize only the
        // lookup source; the translated radio line does not need that padding.
        var source = __result.TrimEnd();
        if (TryTranslate(source, out var translated))
        {
            __result = translated;
            return;
        }

        if (ContainsVisibleLatinLetter(source) && UntranslatedCombatChatterLogged.Add(source))
        {
            Logger?.LogWarning(
                $"Untranslated combat chatter: type={__1}, source={source}");
        }
    }

    private static void TranslateCurrentComponent(Component component)
    {
        var current = component switch
        {
            TMP_Text tmp => tmp.text,
            LegacyText legacy => legacy.text,
            TextMesh textMesh => textMesh.text,
            _ => string.Empty
        };
        var instanceId = component.GetInstanceID();
        if (!TranslationsEnabled)
        {
            RestoreIfDisabled(instanceId, component);
            return;
        }
        if (string.IsNullOrEmpty(current))
            return;
        ApplyKnownStatusOverlayFontLayout(component, current);
        ApplyKnownCampaignPanelTitleLayout(component, current);
        ApplyUnitActionButtonLayout(component, current);
        ApplyMotorPoolTitleFontLayout(component, current);
        ApplyUnitCardNameFontLayout(component, current);
        // Inactive submenu labels can already have been translated by the
        // initial include-inactive scan.  Apply their layout before the
        // last-value fast path so opening the submenu still enlarges every
        // pause-menu button, not just the label translated while visible.
        ApplyPauseMenuButtonFontLayout(component, current);
        if (IsOptionsLeftLabel(current))
            RegisterAndApplyOptionsMenuFont(component);
        else
            ApplyOptionsMenuFontLayout(component);
        if (LastProcessedTexts.TryGetValue(instanceId, out var last) &&
            string.Equals(current, last, StringComparison.Ordinal))
            return;
        LastProcessedTexts[instanceId] = current;
        if (!TryTranslateForDisplay(component, current, out var translated))
            return;
        SetComponentText(component, current, translated);
    }

    private static void PatchUnitStatusWriters(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(Unit), nameof(Unit.GetOtherStatusText)),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(UnitOtherStatusPostfix)));
        harmony.Patch(
            AccessTools.Method(typeof(Unit), nameof(Unit.GetCurrentStatus)),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(UnitCurrentStatusPostfix)));
        Logger?.LogInfo("Patched Unit.GetOtherStatusText and Unit.GetCurrentStatus at the status-string sources.");
    }

    private static void UnitOtherStatusPostfix(ref string __result)
    {
        if (!TranslationsEnabled || string.IsNullOrEmpty(__result))
            return;
        if (TryTranslate(__result, out var translated))
            __result = translated;
    }

    private static void UnitCurrentStatusPostfix(ref Il2CppSystem.ValueTuple<string, Color> __result)
    {
        if (!TranslationsEnabled || __result == null || string.IsNullOrEmpty(__result.Item1))
            return;
        if (TryTranslate(__result.Item1, out var translated))
            __result.Item1 = translated;
    }

    private static void PatchScreenStatusWriters(Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ScreenStatusTextPrefix));
        harmony.Patch(
            AccessTools.Method(typeof(UIManager), nameof(UIManager.SetScreenStatus), new[] { typeof(Unit) }),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(UnitScreenStatusPostfix)));
        harmony.Patch(
            AccessTools.Method(typeof(UIManager), nameof(UIManager.ManageScreenStatuses)),
            prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ManageScreenStatusesPrefix)),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ManageScreenStatusesPostfix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(UIManager),
                nameof(UIManager.CheckAndStatusObject),
                new[]
                {
                    typeof(MapObject), typeof(string), typeof(Il2CppSystem.Func<bool>),
                    typeof(Il2CppSystem.Action), typeof(bool)
                }),
            prefix: prefix);

        var setTextStatus = AccessTools.Method(
            typeof(InventoryItemCrew),
            nameof(InventoryItemCrew.SetTextStatus),
            new[] { typeof(Color), typeof(string) });
        if (setTextStatus != null)
        {
            harmony.Patch(setTextStatus, prefix: prefix);
            Logger?.LogInfo("Patched InventoryItemCrew.SetTextStatus(Color, string) for status overlays.");
        }
        else
        {
            Logger?.LogWarning("Could not find InventoryItemCrew.SetTextStatus(Color, string).");
        }

        foreach (var method in typeof(UIManager).GetMethods().Where(method =>
                     method.Name == nameof(UIManager.SetScreenOverlay) &&
                     method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string))))
        {
            harmony.Patch(method, prefix: prefix);
        }
        Logger?.LogInfo("Patched UIManager status/overlay string entry points before native UI objects are created.");
    }

    private static void PatchUiToolkitTextWriters(Harmony harmony)
    {
        var patched = 0;
        var textSetter = AccessTools.PropertySetter(typeof(TextElement), nameof(TextElement.text));
        if (textSetter != null)
        {
            harmony.Patch(
                textSetter,
                prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(UiToolkitTextSetterPrefix)));
            patched++;
        }

        var labelConstructor = AccessTools.Constructor(typeof(Label), new[] { typeof(string) });
        if (labelConstructor != null)
        {
            harmony.Patch(
                labelConstructor,
                prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(UiToolkitLabelConstructorPrefix)),
                postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(UiToolkitLabelConstructorPostfix)));
            patched++;
        }
        Logger?.LogInfo($"Patched {patched} UI Toolkit text writers.");
    }

    private static void UiToolkitTextSetterPrefix(TextElement __instance, ref string __0)
    {
        var source = __0;
        if (TranslationsEnabled && !string.IsNullOrEmpty(__0) && TryTranslate(__0, out var translated))
            __0 = translated;
        ApplyUiToolkitMenuCommandFont(__instance, source, __0);
    }

    private static void UiToolkitLabelConstructorPrefix(ref string __0, ref string __state)
    {
        __state = __0;
        if (TranslationsEnabled && !string.IsNullOrEmpty(__0) && TryTranslate(__0, out var translated))
            __0 = translated;
    }

    private static void UiToolkitLabelConstructorPostfix(Label __instance, string __0, string __state)
    {
        ApplyUiToolkitMenuCommandFont(__instance, __state, __0);
    }

    private static void ApplyUiToolkitMenuCommandFont(TextElement element, string source, string translated)
    {
        if (element == null || !IsSettingsMenuCommandSource(source))
            return;

        element.style.fontSize = MenuCommandButtonFontSize;
        var plain = PlainText(translated);
        if (UiToolkitMenuCommandsLogged.Add(plain))
            Logger?.LogInfo(
                $"UI Toolkit menu command layout: text={plain}, font={MenuCommandButtonFontSize:0.##}, name={element.name}");
    }

    private static void UnitScreenStatusPostfix(UIManager __instance, Unit __0)
    {
        if (!TranslationsEnabled || __instance == null || __0 == null)
            return;

        var overlays = __instance.unitsWithStatus;
        if (overlays == null || !overlays.TryGetValue(__0, out var overlay) || overlay == null)
            return;

        TranslateStatusOverlay(overlay);
    }

    private static void ManageScreenStatusesPostfix(UIManager __instance)
    {
        if (!TranslationsEnabled || __instance == null || __instance.unitsWithStatus == null)
            return;

        // This is the exact native refresh boundary that overwrites the status
        // labels. Work only on already-known unit overlays, after their English
        // values have been written and before Unity renders the frame.
        foreach (var pair in __instance.unitsWithStatus)
        {
            if (pair.Value != null)
                TranslateStatusOverlay(pair.Value);
        }
    }

    private static void ManageScreenStatusesPrefix(UIManager __instance)
    {
        if (!TranslationsEnabled || __instance == null || __instance.unitsWithStatus == null)
            return;

        // Never carry the previous translated state into the game's per-frame
        // SetText call. Do not rewrite the prefab's STATUS template here: the
        // native writer consumes only live slots, so restoring unused slots
        // would expose those placeholders on screen.
        foreach (var pair in __instance.unitsWithStatus)
        {
            if (pair.Value == null)
                continue;
            var texts = GetStatusOverlayTexts(pair.Value);
            foreach (var text in texts.TmpTexts)
                ClearStatusOverlayTranslationState(text);
            foreach (var text in texts.LegacyTexts)
                ClearStatusOverlayTranslationState(text);
        }
    }

    private static void TranslateStatusOverlay(GameObject overlay)
    {
        var texts = GetStatusOverlayTexts(overlay);

        foreach (var text in texts.TmpTexts)
        {
            if (text != null)
                TranslateStatusOverlayTmpText(text);
        }
        foreach (var text in texts.LegacyTexts)
        {
            if (text != null)
            {
                ApplyStatusOverlayFontLayout(text, text.text, text.text, force: true);
                TranslateCurrentComponent(text);
            }
        }
    }

    private static StatusOverlayTexts GetStatusOverlayTexts(GameObject overlay)
    {
        var overlayId = overlay.GetInstanceID();
        if (StatusOverlayTextCache.TryGetValue(overlayId, out var cached))
            return cached;

        while (StatusOverlayTextCache.Count >= 128 && StatusOverlayTextCacheOrder.Count > 0)
            StatusOverlayTextCache.Remove(StatusOverlayTextCacheOrder.Dequeue());

        var texts = new StatusOverlayTexts(
            overlay.GetComponentsInChildren<TMP_Text>(true),
            overlay.GetComponentsInChildren<LegacyText>(true));
        StatusOverlayTextCache[overlayId] = texts;
        StatusOverlayTextCacheOrder.Enqueue(overlayId);
        RegisterStatusOverlayTexts(texts);
        return texts;
    }

    private static void RegisterStatusOverlayTexts(StatusOverlayTexts texts)
    {
        foreach (var text in texts.TmpTexts)
        {
            if (text == null)
                continue;
            var instanceId = text.GetInstanceID();
            StatusOverlayComponentIds.Add(instanceId);
        }
        foreach (var text in texts.LegacyTexts)
        {
            if (text == null)
                continue;
            var instanceId = text.GetInstanceID();
            StatusOverlayComponentIds.Add(instanceId);
        }
    }

    private static void ClearStatusOverlayTranslationState(Component? component)
    {
        if (component == null)
            return;
        var instanceId = component.GetInstanceID();
        AppliedStates.Remove(instanceId);
        LastProcessedTexts.Remove(instanceId);
    }

    private static void TranslateStatusOverlayTmpText(TMP_Text text)
    {
        // ManageScreenStatuses uses TMP's formatted native writer. That writer
        // updates the pending input immediately, but GetParsedText otherwise
        // still exposes the previous frame's mesh. Synchronize this one known
        // status component at the producer boundary before reading it; without
        // this, English and Chinese become an every-other-frame ping-pong.
        text.ForceMeshUpdate(false, false);
        var current = text.text;
        var source = current;

        // The native status writer updates TMP's rendered character buffer
        // without updating .text. GetParsedText therefore carries both the
        // newly formatted state and the restored STATUS template used to clear
        // expired states; ignoring the latter would leave the previous Chinese
        // status stuck in .text.
        var parsed = text.GetParsedText();
        if (!string.IsNullOrEmpty(parsed) &&
            !string.Equals(parsed, current, StringComparison.Ordinal))
        {
            source = parsed;
        }

        var sourceWithPlaceholders = source;
        var strippedPlaceholders = TryStripStatusPlaceholderLines(source, out var withoutPlaceholders);
        if (strippedPlaceholders)
        {
            if (withoutPlaceholders.Length == 0)
            {
                SetComponentText(text, source, string.Empty);
                return;
            }
            source = withoutPlaceholders;
        }

        // This component was reached through UIManager.unitsWithStatus, so it
        // is a confirmed world-space status label. Composite blocks may end in
        // an ammunition line or an already translated state and therefore do
        // not reliably match the text-based heuristic used elsewhere.
        ApplyStatusOverlayFontLayout(text, source, source, force: true);
        LogStatusOverlaySourceChange(text, source);

        if (string.Equals(source, current, StringComparison.Ordinal))
        {
            TranslateCurrentComponent(text);
            return;
        }

        ApplyKnownStatusOverlayFontLayout(text, source);
        if (!TryTranslateForDisplay(text, source, out var translated))
        {
            if (strippedPlaceholders)
                SetComponentText(text, sourceWithPlaceholders, source);
            return;
        }

        translated = ApplyRenderedStatusLineColors(text, translated);
        LogCampaignDynamicTargetOnce("status-buffer", text, PlainText(source), PlainText(translated));
        SetComponentText(text, sourceWithPlaceholders, translated);
    }

    private static string ApplyRenderedStatusLineColors(TMP_Text text, string translated)
    {
        var normalized = translated.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var textInfo = text.textInfo;
        if (textInfo == null || textInfo.lineCount <= 0 || lines.Length == 0)
            return translated;

        var lineCount = Mathf.Min(lines.Length, textInfo.lineCount);
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            if (lines[lineIndex].Length == 0)
                continue;

            var lineInfo = textInfo.lineInfo[lineIndex];
            if (lineInfo.visibleCharacterCount <= 0)
                continue;
            var characterIndex = lineInfo.firstVisibleCharacterIndex;
            if (characterIndex < 0 || characterIndex >= textInfo.characterCount)
                continue;

            var color = textInfo.characterInfo[characterIndex].color;
            var colorHex = ColorUtility.ToHtmlStringRGBA(color);
            lines[lineIndex] = $"<color=#{colorHex}>{lines[lineIndex]}</color>";
        }
        return string.Join("\n", lines);
    }

    private static void LogStatusOverlaySourceChange(TMP_Text text, string source)
    {
        var plain = PlainText(source);
        if (plain.Length == 0 || plain.Equals("STATUS", StringComparison.Ordinal) ||
            IsScreenStatusTemplateText(source))
        {
            return;
        }

        var instanceId = text.GetInstanceID();
        if (LastStatusOverlaySources.TryGetValue(instanceId, out var previous) &&
            string.Equals(previous, plain, StringComparison.Ordinal))
        {
            return;
        }

        LastStatusOverlaySources[instanceId] = plain;
        Logger?.LogInfo(
            $"Status overlay source changed: id={instanceId}, text={plain.Replace("\n", "\\n", StringComparison.Ordinal)}");
    }

    private static bool ContainsDynamicStatusToken(string value)
    {
        return BrokenStatusTokenRegex.IsMatch(value) ||
               DamagedStatusTokenRegex.IsMatch(value) ||
               FailureStatusTokenRegex.IsMatch(value) ||
               ReducedStatusTokenRegex.IsMatch(value) ||
               UnsafeStatusTokenRegex.IsMatch(value) ||
               RoutedStatusTokenRegex.IsMatch(value) ||
               BleedoutStatusTokenRegex.IsMatch(value) ||
               StunnedStatusTokenRegex.IsMatch(value);
    }

    private static bool TryStripStatusPlaceholderLines(string value, out string stripped)
    {
        if (!value.Contains("STATUS", StringComparison.Ordinal) &&
            !value.Contains("状态", StringComparison.Ordinal))
        {
            stripped = value;
            return false;
        }

        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);
        var removed = false;
        foreach (var line in lines)
        {
            var plain = PlainText(line);
            if (plain.Equals("STATUS", StringComparison.Ordinal) ||
                plain.Equals("状态", StringComparison.Ordinal))
            {
                removed = true;
                continue;
            }
            kept.Add(line);
        }

        stripped = string.Join("\n", kept).Trim('\n');
        return removed;
    }

    private static bool IsStatusOverlayUpdate(string value)
    {
        var plain = PlainText(value);
        return ContainsDynamicStatusToken(value) ||
               IsStatusOverlayLabel(value) ||
               plain.Equals("SUPPRESSED", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("PINNED DOWN", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("ROUTED", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("BLEEDOUT", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("STUNNED", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("受压制", StringComparison.Ordinal) ||
               plain.Equals("被压制", StringComparison.Ordinal) ||
               plain.Equals("溃逃", StringComparison.Ordinal) ||
               plain.Equals("失血", StringComparison.Ordinal) ||
               plain.Equals("眩晕", StringComparison.Ordinal);
    }

    private static void ScreenStatusTextPrefix(object[] __args)
    {
        if (!TranslationsEnabled)
            return;
        for (var index = 0; index < __args.Length; index++)
        {
            if (__args[index] is not string text || string.IsNullOrEmpty(text))
                continue;
            if (TryTranslate(text, out var translated))
                __args[index] = translated;
        }
    }

    private static void PatchImGuiTextMethods(Harmony harmony)
    {
        var patched = 0;
        foreach (var type in new[] { typeof(GUI), typeof(GUILayout), typeof(GUIStyle) })
        {
            foreach (var method in type.GetMethods().Where(method =>
                         (method.Name is "Label" or "Button" or "Box" or "Draw" or "Internal_Draw") &&
                         method.GetParameters().Any(parameter =>
                             parameter.ParameterType == typeof(string) ||
                             parameter.ParameterType == typeof(GUIContent))))
            {
                var parameters = method.GetParameters();
                var hasString = parameters.Any(parameter => parameter.ParameterType == typeof(string));
                var hasContent = parameters.Any(parameter => parameter.ParameterType == typeof(GUIContent));
                var hasStyle = parameters.Any(parameter => parameter.ParameterType == typeof(GUIStyle));
                try
                {
                    if (type != typeof(GUIStyle) && hasStyle && (hasString || hasContent))
                    {
                        harmony.Patch(
                            method,
                            prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ImGuiStyledPrefix)),
                            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ImGuiStyledPostfix)));
                    }
                    else if (type == typeof(GUIStyle) && hasContent)
                    {
                        harmony.Patch(
                            method,
                            prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ImGuiStylePrefix)),
                            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ImGuiStylePostfix)));
                    }
                    else if (hasString)
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ImGuiStringPrefix)));
                    else if (hasContent)
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(ImGuiContentPrefix)));
                    patched++;
                }
                catch (System.Exception ex)
                {
                    Logger?.LogWarning($"Could not patch IMGUI method {type.Name}.{method}: {ex.Message}");
                }
            }
        }
        Logger?.LogInfo($"Patched {patched} Unity IMGUI text methods (GUI/GUILayout Label, Button, Box; GUIStyle.Draw).");
    }

    private static void ImGuiStyledPrefix(object[] __args, ref ImGuiStyleState __state)
    {
        __state = default;
        string? source = null;
        GUIContent? content = null;
        GUIStyle? style = null;
        var stringIndex = -1;
        for (var index = 0; index < __args.Length; index++)
        {
            if (__args[index] is GUIStyle candidateStyle)
                style = candidateStyle;
            else if (__args[index] is GUIContent candidateContent)
            {
                content = candidateContent;
                source = candidateContent.text;
            }
            else if (__args[index] is string candidateText)
            {
                source = candidateText;
                stringIndex = index;
            }
        }

        if (string.IsNullOrEmpty(source))
            return;
        var translated = source;
        TranslateImGuiText(ref translated);
        if (content != null)
            content.text = translated;
        else if (stringIndex >= 0)
            __args[stringIndex] = translated;

        if (style != null)
            ApplyImGuiLayout(style, source, translated, ref __state);
    }

    private static void ImGuiStyledPostfix(object[] __args, ImGuiStyleState __state)
    {
        if (!__state.IsApplied)
            return;
        foreach (var argument in __args)
        {
            if (argument is not GUIStyle style)
                continue;
            RestoreImGuiLayout(style, __state);
            return;
        }
    }

    private static void ImGuiStringPrefix(ref string text)
    {
        TranslateImGuiText(ref text);
    }

    private static void ImGuiContentPrefix(GUIContent content)
    {
        if (content == null)
            return;
        var text = content.text;
        TranslateImGuiText(ref text);
        content.text = text;
    }

    private static void ImGuiStylePrefix(GUIStyle __instance, GUIContent content, ref ImGuiStyleState __state)
    {
        __state = default;
        if (__instance == null || content == null)
            return;

        var source = content.text;
        var text = source;
        TranslateImGuiText(ref text);
        content.text = text;
        ApplyImGuiLayout(__instance, source, text, ref __state);
    }

    private static void ImGuiStylePostfix(GUIStyle __instance, ImGuiStyleState __state)
    {
        if (__instance != null && __state.IsApplied)
        {
            RestoreImGuiLayout(__instance, __state);
        }
    }

    private static void ApplyImGuiLayout(
        GUIStyle style,
        string source,
        string translated,
        ref ImGuiStyleState state)
    {
        var isManualGuide = IsManualGuideLabel(translated);
        var isCampaignPanelTitle = IsPanelTitleLayoutTarget(source) || IsPanelTitleLayoutTarget(translated);
        var isMenuCommand = IsSettingsMenuCommandSource(source);
        var isOptionsLabel = IsOptionsLeftLabel(source) || IsOptionsLeftLabel(translated);
        if ((!isManualGuide && !isCampaignPanelTitle && !isMenuCommand && !isOptionsLabel) ||
            !ActiveManualGuideStyles.Add(style))
            return;

        var originalFontSize = style.fontSize;
        if (originalFontSize <= 0 && !isMenuCommand && !isOptionsLabel)
        {
            ActiveManualGuideStyles.Remove(style);
            return;
        }

        state = new ImGuiStyleState(originalFontSize, style.contentOffset, style.alignment);
        if (isOptionsLabel)
        {
            style.fontSize = originalFontSize > 0
                ? Mathf.Max(originalFontSize + 1, Mathf.RoundToInt(originalFontSize * OptionsMenuFontScale))
                : 14;
            style.alignment = TextAnchor.MiddleRight;
            var plain = PlainText(translated);
            if (ImGuiMenuCommandsLogged.Add($"options-label:{plain}"))
                Logger?.LogInfo(
                    $"Options label layout: type=IMGUI, text={plain}, font={originalFontSize}->{style.fontSize}, align=Right");
        }
        else if (isMenuCommand)
        {
            style.fontSize = Mathf.RoundToInt(MenuCommandButtonFontSize);
            var plain = PlainText(translated);
            if (ImGuiMenuCommandsLogged.Add(plain))
                Logger?.LogInfo($"IMGUI menu command layout: text={plain}, font={originalFontSize}->{style.fontSize}");
        }
        else if (isCampaignPanelTitle)
        {
            style.fontSize = Mathf.Max(10, Mathf.RoundToInt(originalFontSize * CampaignPanelTitleFontScale));
            // IMGUI uses screen coordinates, where positive Y moves content down.
            style.contentOffset = state.ContentOffset +
                                  Vector2.up * originalFontSize * CampaignPanelTitleDownShiftScale;
            var plain = PlainText(translated);
            if (ImGuiPanelTitlesLogged.Add(plain))
                Logger?.LogInfo($"IMGUI campaign title layout: text={plain}, font={originalFontSize}->{style.fontSize}");
        }
        else
        {
            style.fontSize = Mathf.Max(
                originalFontSize + 1,
                Mathf.RoundToInt(originalFontSize * ManualGuideImGuiFontScale));
        }
    }

    private static void RestoreImGuiLayout(GUIStyle style, ImGuiStyleState state)
    {
        style.fontSize = state.FontSize;
        style.contentOffset = state.ContentOffset;
        style.alignment = state.Alignment;
        ActiveManualGuideStyles.Remove(style);
    }

    private static bool IsManualGuideLabel(string text)
    {
        var plain = PlainText(text);
        plain = plain.TrimEnd(':', '：').Trim();
        return ManualGuideLabels.Contains(plain);
    }

    private static void TranslateImGuiText(ref string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if ((text.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("paused", StringComparison.OrdinalIgnoreCase)) &&
            ImGuiCandidatesLogged.Add(text))
        {
            Logger?.LogInfo($"IMGUI text candidate: {text.Replace("\n", "\\n", StringComparison.Ordinal)}");
        }
        var plain = PlainText(text);
        if (IsCampaignScene() && plain.Equals("Exit", StringComparison.OrdinalIgnoreCase))
            text = "退出";
        else if (TryTranslate(text, out var translated))
            text = translated;
        text = AppendMainMenuVersionCredit(text);
    }

    private static bool IsCampaignScene()
    {
        return ActiveSceneName.Contains("Campaign", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCampaignPanelTitle(string text)
    {
        var plain = PlainText(text);
        return CampaignPanelTitles.Contains(plain);
    }

    private static bool IsPanelTitleLayoutTarget(string text)
    {
        var plain = PlainText(text);
        return (IsCampaignScene() && CampaignPanelTitles.Contains(plain)) ||
               (IsMainMenuScene() && MainMenuPanelTitles.Contains(plain));
    }

    private static bool IsContractTitleLayoutTarget(Component component, string text)
    {
        var plain = PlainText(text);
        return IsCampaignScene() &&
               component.name.Equals("title", StringComparison.OrdinalIgnoreCase) &&
               (plain.Equals("CONTRACT", StringComparison.OrdinalIgnoreCase) ||
                 plain.Equals("合约", StringComparison.Ordinal));
    }

    private static bool IsAsperaNameLayoutTarget(Component component, string text)
    {
        var plain = PlainText(text);
        return IsCampaignScene() &&
               component.name.Equals("asperaName", StringComparison.OrdinalIgnoreCase) &&
               (plain.Equals("Aspera", StringComparison.OrdinalIgnoreCase) ||
                plain.Equals("阿斯佩拉", StringComparison.Ordinal));
    }

    private static bool TryGetCompactTitleVerticalOffsetTag(
        Component component,
        string text,
        out string offsetTag)
    {
        var plain = PlainText(text);
        if (IsAsperaNameLayoutTarget(component, plain))
        {
            offsetTag = AsperaNameVerticalOffsetTag;
            return true;
        }

        var isContractTitle = IsContractTitleLayoutTarget(component, plain);
        if (isContractTitle)
        {
            offsetTag = ContractTitleVerticalOffsetTag;
            return true;
        }

        offsetTag = string.Empty;
        return false;
    }

    private static string ApplyCompactTitleVerticalOffset(string value, string offsetTag)
    {
        return value.Contains("<voffset", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{offsetTag}{value}</voffset>";
    }

    private static bool IsContractDetailLabelText(string text)
    {
        var plain = PlainText(text);
        var englishLabels = plain.Contains("REGION", StringComparison.OrdinalIgnoreCase) &&
                            plain.Contains("PAY %", StringComparison.OrdinalIgnoreCase) &&
                            plain.Contains("AREA", StringComparison.OrdinalIgnoreCase) &&
                            plain.Contains("TIME", StringComparison.OrdinalIgnoreCase) &&
                            plain.Contains("DANGER", StringComparison.OrdinalIgnoreCase);
        var chineseLabels = plain.Contains("区域", StringComparison.Ordinal) &&
                            plain.Contains("报酬", StringComparison.Ordinal) &&
                            plain.Contains("地区", StringComparison.Ordinal) &&
                            plain.Contains("时间", StringComparison.Ordinal) &&
                            plain.Contains("危险度", StringComparison.Ordinal);
        return IsCampaignScene() && plain.Contains('\n') && (englishLabels || chineseLabels);
    }

    private static string ApplyContractDetailLabelLineHeight(string value)
    {
        return value.Contains("<line-height", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{ContractDetailLabelLineHeightTag}{value}</line-height>";
    }

    private static string ApplyContractDangerValueTranslation(string value)
    {
        var plain = PlainText(value);
        if (!IsCampaignScene() || !plain.Contains('\n') || !ContractCoordinateRegex.IsMatch(plain))
            return value;

        var translated = ContractExtremeValueRegex.Replace(value, "极高");
        translated = ContractHighValueRegex.Replace(translated, "高");
        translated = ContractMediumValueRegex.Replace(translated, "中");
        return ContractLowValueRegex.Replace(translated, "低");
    }

    private static bool IsMainMenuScene()
    {
        return ActiveSceneName.Equals("0StartView", StringComparison.OrdinalIgnoreCase);
    }

    private static string PlainText(string value)
    {
        return value.IndexOf('<') >= 0
            ? HtmlTagRegex.Replace(value, string.Empty).Trim()
            : value.Trim();
    }

    private static void TextPrefix(Component component, ref string value)
    {
        var instanceId = component.GetInstanceID();
        if (InternalTextWrites.Contains(instanceId))
            return;

        if (!TranslationsEnabled)
        {
            if (AppliedStates.TryGetValue(instanceId, out var state) &&
                string.Equals(value, state.Translated, StringComparison.Ordinal))
            {
                value = state.Original;
                LastProcessedTexts[instanceId] = state.Original;
            }
            return;
        }

        if (StatusOverlayComponentIds.Contains(instanceId) &&
            TryStripStatusPlaceholderLines(value, out var withoutStatusPlaceholders))
        {
            value = withoutStatusPlaceholders;
            if (value.Length == 0)
            {
                LastProcessedTexts[instanceId] = value;
                return;
            }
        }

        // SetTextStatus translates the producer argument before the native UI
        // component receives it.  If the setter therefore sees the already
        // translated value, still apply the status-overlay font layout here.
        ApplyKnownStatusOverlayFontLayout(component, value);
        ApplyKnownCampaignPanelTitleLayout(component, value);
        ApplyUnitActionButtonLayout(component, value);
        ApplyMotorPoolTitleFontLayout(component, value);
        ApplyUnitCardNameFontLayout(component, value);
        ApplyPauseMenuButtonFontLayout(component, value);
        if (IsOptionsLeftLabel(value))
            RegisterAndApplyOptionsMenuFont(component);

        var source = value;
        if (AppliedStates.TryGetValue(instanceId, out var previous))
        {
            // This is the plugin's own value coming back through another
            // writer (TMP.text, SetText, or a native IL2CPP update).  It is
            // already final, so do not run the mapping list a second time.
            if (string.Equals(value, previous.Translated, StringComparison.Ordinal))
            {
                LastProcessedTexts[instanceId] = value;
                return;
            }

            if (string.Equals(value, previous.Original, StringComparison.Ordinal))
                source = previous.Original;
            else if (IsMixedLanguageText(value) &&
                     ContainsCjk(previous.Translated) &&
                     !StatusOverlayComponentIds.Contains(instanceId) &&
                     !IsStatusOverlayUpdate(value))
            {
                // A native writer can expose the control between two writes,
                // after another hook has already translated only part of the
                // string. Never promote that mixed-language value to a new
                // source state in ordinary UI. Status overlays are excluded:
                // their legitimate next state is frequently a mixed block,
                // and retaining the previous translation would freeze it.
                value = previous.Translated;
                LastProcessedTexts[instanceId] = value;
                return;
            }
        }

        if (!TryTranslateForDisplay(component, source, out var translated))
            return;

        AppliedStates[instanceId] = new TextState(source, translated);
        LastProcessedTexts[instanceId] = translated;
        value = translated;
    }

    private static void SetComponentText(Component component, string original, string translated)
    {
        if (string.Equals(original, translated, StringComparison.Ordinal))
            return;

        var instanceId = component.GetInstanceID();
        AppliedStates[instanceId] = new TextState(original, translated);
        LastProcessedTexts[instanceId] = translated;
        InternalTextWrites.Add(instanceId);
        try
        {
            if (component is TMP_Text tmpText &&
                !string.Equals(tmpText.text, translated, StringComparison.Ordinal))
            {
                tmpText.text = translated;
            }
            else if (component is LegacyText legacyText &&
                     !string.Equals(legacyText.text, translated, StringComparison.Ordinal))
            {
                legacyText.text = translated;
            }
            else if (component is TextMesh textMesh &&
                     !string.Equals(textMesh.text, translated, StringComparison.Ordinal))
            {
                textMesh.text = translated;
            }
        }
        finally
        {
            InternalTextWrites.Remove(instanceId);
        }
    }

    private static bool TryTranslateForDisplay(Component component, string source, out string translated)
    {
        translated = source;
        var plainSource = PlainText(source);
        if (plainSource.Equals("Exit", StringComparison.OrdinalIgnoreCase) && IsCampaignScene())
        {
            translated = "退出";
        }
        else if (plainSource.Equals("Exit", StringComparison.OrdinalIgnoreCase) &&
            IsExitMenuControl(component, out var exitPath))
        {
            translated = "退出";
            var instanceId = component.GetInstanceID();
            if (ExitContextLoggedIds.Add(instanceId))
                Logger?.LogInfo($"EXIT context target: menu=true, path={exitPath}");
        }
        else if (TryTranslate(source, out var mapped))
            translated = ApplyContextLayout(component, source, mapped);
        else if (component is TMP_Text &&
                 TryGetCompactTitleVerticalOffsetTag(component, source, out var offsetTag))
            translated = ApplyCompactTitleVerticalOffset(source, offsetTag);

        ApplyPauseMenuButtonFontLayout(component, source);
        translated = ApplyContractDangerValueTranslation(translated);
        if (component is TMP_Text && IsContractDetailLabelText(source))
            translated = ApplyContractDetailLabelLineHeight(translated);

        translated = AppendMainMenuVersionCredit(translated);
        TraceMenuDisplayComponent(component, source, translated);
        return !string.Equals(source, translated, StringComparison.Ordinal);
    }

    private static void TraceMenuDisplayComponent(Component component, string source, string translated)
    {
        var plainSource = PlainText(source);
        var plainTranslated = PlainText(translated);
        var isTarget = plainSource.Contains("save and exit", StringComparison.OrdinalIgnoreCase) ||
                       plainTranslated.Equals("保存并退出", StringComparison.Ordinal) ||
                       (plainSource.Contains("exit", StringComparison.OrdinalIgnoreCase) &&
                        plainTranslated.Equals("退出", StringComparison.Ordinal));
        if (!isTarget || !MenuDisplayComponentsLogged.Add(component.GetInstanceID()))
            return;

        var metrics = component switch
        {
            TMP_Text tmp => $"TMP font={tmp.fontSize:0.##}, auto={tmp.enableAutoSizing}, min={tmp.fontSizeMin:0.##}, max={tmp.fontSizeMax:0.##}",
            LegacyText legacy => $"LegacyText font={legacy.fontSize}, bestFit={legacy.resizeTextForBestFit}, min={legacy.resizeTextMinSize}, max={legacy.resizeTextMaxSize}",
            TextMesh textMesh => $"TextMesh font={textMesh.fontSize}, characterSize={textMesh.characterSize:0.###}",
            _ => component.GetType().FullName ?? component.GetType().Name
        };
        Logger?.LogInfo(
            $"MENU DISPLAY COMPONENT: source={plainSource}, translated={plainTranslated}, type={component.GetType().FullName}, metrics={metrics}, path={BuildTransformPath(component.transform)}");
    }

    private static bool IsExitMenuControl(Component component, out string path)
    {
        var names = new List<string>();
        var current = component.transform;
        var depth = 0;
        while (current != null && depth++ < 16)
        {
            names.Add(current.name);
            var name = current.name;
            if (name.Contains("objective", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("mission", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("extract", StringComparison.OrdinalIgnoreCase))
            {
                path = string.Join("/", names);
                return false;
            }

            if (name.Contains("pause", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("menu", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("option", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("setting", StringComparison.OrdinalIgnoreCase))
            {
                path = string.Join("/", names);
                return true;
            }
            current = current.parent;
        }

        path = string.Join("/", names);
        return false;
    }

    private static string AppendMainMenuVersionCredit(string value)
    {
        if (!string.Equals(ActiveSceneName, MainMenuSceneName, StringComparison.OrdinalIgnoreCase) ||
            LocalizationCreditRegex.IsMatch(value))
            return value;

        var match = MainMenuVersionRegex.Match(value);
        return match.Success
            ? value.Insert(match.Index + match.Length, "（InstantComet汉化）")
            : value;
    }

    private static bool IsMixedLanguageText(string value)
    {
        var hasCjk = false;
        var hasLatin = false;
        foreach (var character in value)
        {
            if ((character >= '\u2E80' && character <= '\u9FFF') ||
                (character >= '\uF900' && character <= '\uFAFF') ||
                (character >= '\uFF00' && character <= '\uFFEF'))
            {
                hasCjk = true;
            }
            else if ((character >= 'A' && character <= 'Z') ||
                     (character >= 'a' && character <= 'z'))
            {
                hasLatin = true;
            }

            if (hasCjk && hasLatin)
                return true;
        }

        return false;
    }

    private static bool ContainsCjk(string value)
    {
        foreach (var character in value)
        {
            if ((character >= '\u2E80' && character <= '\u9FFF') ||
                (character >= '\uF900' && character <= '\uFAFF') ||
                (character >= '\uFF00' && character <= '\uFFEF'))
            {
                return true;
            }
        }

        return false;
    }

    private static string ApplyContextLayout(Component component, string source, string translated)
    {
        var plainSource = PlainText(source);
        ApplyStatusOverlayFontLayout(component, source, translated);
        ApplyManualPauseFontLayout(component, plainSource);
        ApplyObjectiveFontLayout(component, plainSource);
        ApplyCampaignPanelTitleLayout(component, plainSource);
        ApplyUnitActionButtonLayout(component, plainSource);
        ApplyMotorPoolTitleFontLayout(component, plainSource);
        ApplyUnitCardNameFontLayout(component, translated);
        if (component is TMP_Text &&
            TryGetCompactTitleVerticalOffsetTag(component, plainSource, out var offsetTag))
            return ApplyCompactTitleVerticalOffset(translated, offsetTag);
        if (string.Equals(plainSource, "UI", StringComparison.OrdinalIgnoreCase))
        {
            var soundUi = IsSoundSettingsUi(component, out var uiPath, out var uiY);
            var uiInstanceId = component.GetInstanceID();
            if (UiContextLoggedIds.Add(uiInstanceId))
            {
                Logger?.LogInfo(
                    $"UI context target: sound={soundUi}, y={uiY:0.0}/{Screen.height}, path={uiPath}");
            }
            return soundUi ? "界面音效" : "界面";
        }

        if (!string.Equals(plainSource, "FRONT", StringComparison.OrdinalIgnoreCase))
            return translated;

        var turretFront = IsTurretFront(component, out var path, out var screenX);
        var instanceId = component.GetInstanceID();
        if (FrontLayoutLoggedIds.Add(instanceId))
        {
            Logger?.LogInfo(
                $"FRONT layout target: turret={turretFront}, x={screenX:0.0}/{Screen.width}, path={path}");
        }

        return turretFront ? translated + "<color=#00000000>F</color>" : translated;
    }

    private static void ApplyKnownStatusOverlayFontLayout(Component component, string value)
    {
        if (IsStatusOverlayLabel(value))
            ApplyStatusOverlayFontLayout(component, value, value);
    }

    private static void ApplyKnownCampaignPanelTitleLayout(Component component, string value)
    {
        ApplyCampaignPanelTitleLayout(component, PlainText(value));
    }

    private static void ApplyStatusOverlayFontLayout(
        Component component,
        string source,
        string translated,
        bool force = false)
    {
        if (!force && !IsStatusOverlayLabel(source) && !IsStatusOverlayLabel(translated))
            return;

        var instanceId = component.GetInstanceID();
        if (component is TMP_Text tmp)
        {
            if (!StatusOverlayFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(tmp.fontSize, null);
                StatusOverlayFontStates[instanceId] = state;
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = Mathf.Max(10f, state.TmpFontSize!.Value * StatusOverlayFontScale);
        }
        else if (component is LegacyText legacy)
        {
            if (!StatusOverlayFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(null, legacy.fontSize);
                StatusOverlayFontStates[instanceId] = state;
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.Max(10, Mathf.RoundToInt(state.LegacyFontSize!.Value * StatusOverlayFontScale));
        }
    }

    private static bool IsStatusOverlayLabel(string value)
    {
        var plain = PlainText(value);
        return plain.EndsWith(" BROKEN", StringComparison.OrdinalIgnoreCase) ||
               plain.EndsWith(" DAMAGED", StringComparison.OrdinalIgnoreCase) ||
               plain.EndsWith(" REDUCED", StringComparison.OrdinalIgnoreCase) ||
               plain.StartsWith("INSUFFICIENT ", StringComparison.OrdinalIgnoreCase) ||
               plain.StartsWith("LOW ", StringComparison.OrdinalIgnoreCase) ||
               plain.EndsWith("损坏", StringComparison.Ordinal) ||
               plain.EndsWith("受损", StringComparison.Ordinal) ||
               plain.EndsWith("降低", StringComparison.Ordinal) ||
               plain.StartsWith("发动机功率不足", StringComparison.Ordinal) ||
               plain.StartsWith("发动机扭矩过低", StringComparison.Ordinal);
    }

    private static void ApplyManualPauseFontLayout(Component component, string plainSource)
    {
        if (!ManualPauseLabels.Contains(plainSource))
            return;
        var instanceId = component.GetInstanceID();
        if (component is TMP_Text tmp)
        {
            if (!ObjectiveFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(tmp.fontSize, null);
                ObjectiveFontStates[instanceId] = state;
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = Mathf.Max(10f, state.TmpFontSize!.Value * 0.75f);
        }
        else if (component is LegacyText legacy)
        {
            if (!ObjectiveFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(null, legacy.fontSize);
                ObjectiveFontStates[instanceId] = state;
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.Max(10, Mathf.RoundToInt(state.LegacyFontSize!.Value * 0.75f));
        }
    }

    private static void ApplyPauseMenuButtonFontLayout(
        Component component,
        string value,
        bool knownPauseMenuTarget = false)
    {
        var isMenuCommandButton = IsMenuCommandComponent(component) && IsMenuCommandButtonLabel(value);
        var isConvoyMenuButton = IsConvoyMenuCommandComponent(component) && IsMenuCommandButtonLabel(value);
        var inPauseMenu = knownPauseMenuTarget || IsPauseMenuComponent(component);
        if (!isMenuCommandButton && !isConvoyMenuButton &&
            (!inPauseMenu ||
             (!IsPauseMenuButtonLabel(value) && !IsPauseMenuButtonTextPath(component))))
            return;

        var instanceId = component.GetInstanceID();
        if (component is TMP_Text tmp)
        {
            if (!PauseMenuButtonFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(tmp.fontSize, null);
                PauseMenuButtonFontStates[instanceId] = state;
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = isMenuCommandButton || isConvoyMenuButton
                ? MenuCommandButtonFontSize
                : Mathf.Max(10f, state.TmpFontSize!.Value * PauseMenuButtonFontScale);
            if (PauseMenuLayoutLoggedIds.Add(instanceId))
            {
                Logger?.LogInfo(
                    $"{(isConvoyMenuButton ? "Convoy" : isMenuCommandButton ? "Command" : "Pause")} menu button layout: text={PlainText(value)}, font={state.TmpFontSize!.Value:0.##}->{tmp.fontSize:0.##}, path={BuildTransformPath(component.transform)}");
            }
        }
        else if (component is LegacyText legacy)
        {
            if (!PauseMenuButtonFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(null, legacy.fontSize);
                PauseMenuButtonFontStates[instanceId] = state;
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = isMenuCommandButton || isConvoyMenuButton
                ? Mathf.RoundToInt(MenuCommandButtonFontSize)
                : Mathf.Max(10, Mathf.RoundToInt(state.LegacyFontSize!.Value * PauseMenuButtonFontScale));
            if (PauseMenuLayoutLoggedIds.Add(instanceId))
            {
                Logger?.LogInfo(
                    $"{(isConvoyMenuButton ? "Convoy" : isMenuCommandButton ? "Command" : "Pause")} menu button layout: text={PlainText(value)}, font={state.LegacyFontSize!.Value}->{legacy.fontSize}, path={BuildTransformPath(component.transform)}");
            }
        }
        else if (component is TextMesh textMesh)
        {
            if (!MenuTextMeshFontStates.TryGetValue(instanceId, out var state))
            {
                state = new TextMeshFontLayoutState(textMesh.fontSize, textMesh.characterSize);
                MenuTextMeshFontStates[instanceId] = state;
            }
            textMesh.characterSize = state.CharacterSize * PauseMenuButtonFontScale;
            if (PauseMenuLayoutLoggedIds.Add(instanceId))
            {
                Logger?.LogInfo(
                    $"TextMesh menu button layout: text={PlainText(value)}, font={state.FontSize}, characterSize={state.CharacterSize:0.###}->{textMesh.characterSize:0.###}, path={BuildTransformPath(component.transform)}");
            }
        }
    }

    private static bool IsMenuCommandButtonLabel(string value)
    {
        var plain = PlainText(value);
        return EqualsIgnoringWhitespace(plain, "OPTIONS", StringComparison.OrdinalIgnoreCase) ||
               EqualsIgnoringWhitespace(plain, "SAVEANDEXIT", StringComparison.OrdinalIgnoreCase) ||
               EqualsIgnoringWhitespace(plain, "EXIT", StringComparison.OrdinalIgnoreCase) ||
               EqualsIgnoringWhitespace(plain, "选项", StringComparison.Ordinal) ||
               EqualsIgnoringWhitespace(plain, "保存并退出", StringComparison.Ordinal) ||
               EqualsIgnoringWhitespace(plain, "退出", StringComparison.Ordinal);
    }

    private static bool IsSettingsMenuCommandSource(string value)
    {
        var plain = PlainText(value);
        return EqualsIgnoringWhitespace(plain, "SAVEANDEXIT", StringComparison.OrdinalIgnoreCase) ||
               EqualsIgnoringWhitespace(plain, "EXIT", StringComparison.Ordinal);
    }

    private static bool IsMenuCommandComponent(Component component)
    {
        return HasAncestorNamed(component.transform, "Menu", 14);
    }

    private static bool IsConvoyMenuCommandComponent(Component component)
    {
        return HasAncestorNamed(component.transform, "ConvoyUI", 8) &&
               (HasAncestorNamed(component.transform, "savexit", 4) ||
                HasAncestorNamed(component.transform, "return", 4));
    }

    private static bool IsPauseMenuButtonLabel(string value)
    {
        return IsMenuCommandButtonLabel(value);
    }

    private static bool EqualsIgnoringWhitespace(
        string value,
        string expected,
        StringComparison comparison)
    {
        var valueIndex = 0;
        var expectedIndex = 0;
        while (true)
        {
            while (valueIndex < value.Length && char.IsWhiteSpace(value[valueIndex]))
                valueIndex++;
            while (expectedIndex < expected.Length && char.IsWhiteSpace(expected[expectedIndex]))
                expectedIndex++;

            if (valueIndex >= value.Length || expectedIndex >= expected.Length)
                return valueIndex >= value.Length && expectedIndex >= expected.Length;

            if (!value.AsSpan(valueIndex, 1).Equals(
                    expected.AsSpan(expectedIndex, 1), comparison))
                return false;

            valueIndex++;
            expectedIndex++;
        }
    }

    private static bool IsPauseMenuComponent(Component component)
    {
        var menu = UIMainMenu.instance;
        if (menu == null || menu.mainMenu == null)
            return false;
        var root = menu.mainMenu.transform;
        return component.transform == root || component.transform.IsChildOf(root);
    }

    private static bool IsPauseMenuButtonTextPath(Component component)
    {
        var current = component.transform;
        var depth = 0;
        var hasButtonsAncestor = false;
        while (current != null && depth++ < 8)
        {
            if (current.name.Equals("buttons", StringComparison.OrdinalIgnoreCase))
                hasButtonsAncestor = true;
            if (current.name.Equals("Menu", StringComparison.OrdinalIgnoreCase))
                return hasButtonsAncestor;
            current = current.parent;
        }
        return false;
    }

    private static void ApplyObjectiveFontLayout(Component component, string plainSource)
    {
        var normalizedSource = ObjectiveCounterRegex.Replace(plainSource, string.Empty).Trim();
        var scale = ObjectiveTitles.Contains(normalizedSource)
            ? 0.72f
            : ObjectiveDetails.Contains(normalizedSource) ? 1.45f : 0f;
        if (scale <= 0f)
            return;

        var instanceId = component.GetInstanceID();
        if (component is TMP_Text tmp)
        {
            if (!ObjectiveFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(tmp.fontSize, null);
                ObjectiveFontStates[instanceId] = state;
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = Mathf.Max(10f, state.TmpFontSize!.Value * scale);
        }
        else if (component is LegacyText legacy)
        {
            if (!ObjectiveFontStates.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(null, legacy.fontSize);
                ObjectiveFontStates[instanceId] = state;
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.Max(10, Mathf.RoundToInt(state.LegacyFontSize!.Value * scale));
        }
    }

    private static void ApplyCampaignPanelTitleLayout(Component component, string plainSource)
    {
        if (!IsPanelTitleLayoutTarget(plainSource) &&
            !IsContractTitleLayoutTarget(component, plainSource) &&
            !IsAsperaNameLayoutTarget(component, plainSource))
            return;

        var instanceId = component.GetInstanceID();
        if (PanelTitleContextLoggedIds.Add(instanceId))
            Logger?.LogInfo($"Campaign panel title layout: text={plainSource}, object={component.name}");
        if (component is TMP_Text tmp)
        {
            var rectTransform = component.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;
            if (!PanelTitleLayoutStates.TryGetValue(instanceId, out var state))
            {
                state = new PanelTitleLayoutState(tmp.fontSize, null, rectTransform.anchoredPosition);
                PanelTitleLayoutStates[instanceId] = state;
                Logger?.LogInfo(
                    $"TMP campaign title metrics: text={plainSource}, font={tmp.fontSize:0.##}->{tmp.fontSize * CampaignPanelTitleFontScale:0.##}, y={state.AnchoredPosition.y:0.##}");
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = Mathf.Max(10f, state.TmpFontSize!.Value * CampaignPanelTitleFontScale);
            rectTransform.anchoredPosition = state.AnchoredPosition +
                                             Vector2.down * state.TmpFontSize.Value * CampaignPanelTitleDownShiftScale;
        }
        else if (component is LegacyText legacy)
        {
            var rectTransform = component.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;
            if (!PanelTitleLayoutStates.TryGetValue(instanceId, out var state))
            {
                state = new PanelTitleLayoutState(null, legacy.fontSize, rectTransform.anchoredPosition);
                PanelTitleLayoutStates[instanceId] = state;
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.Max(10, Mathf.RoundToInt(state.LegacyFontSize!.Value * CampaignPanelTitleFontScale));
            rectTransform.anchoredPosition = state.AnchoredPosition +
                                             Vector2.down * state.LegacyFontSize.Value * CampaignPanelTitleDownShiftScale;
        }
        else if (component is TextMesh textMesh)
        {
            if (!PanelTitleLayoutStates.TryGetValue(instanceId, out var state))
            {
                state = new PanelTitleLayoutState(
                    null, null, default, textMesh.characterSize, textMesh.transform.localPosition);
                PanelTitleLayoutStates[instanceId] = state;
                Logger?.LogInfo(
                    $"TextMesh campaign title metrics: text={plainSource}, font={textMesh.fontSize}, characterSize={textMesh.characterSize:0.###}");
            }
            textMesh.characterSize = state.TextMeshCharacterSize!.Value * CampaignPanelTitleFontScale;
            textMesh.transform.localPosition = state.LocalPosition!.Value +
                                               Vector3.down * state.TextMeshCharacterSize.Value * CampaignPanelTitleDownShiftScale;
        }
    }

    private static bool IsUnitActionButtonLayoutTarget(string value)
    {
        var plain = PlainText(value);
        return plain.Equals("STRIP", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("SCUTTLE", StringComparison.OrdinalIgnoreCase) ||
               plain.Equals("拆卸", StringComparison.Ordinal) ||
               plain.Equals("自毁", StringComparison.Ordinal);
    }

    private static void ApplyUnitActionButtonLayout(Component component, string value)
    {
        if (!IsUnitActionButtonLayoutTarget(value))
            return;

        var instanceId = component.GetInstanceID();
        if (component is TMP_Text tmp)
        {
            var rectTransform = component.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;
            if (!UnitActionButtonLayoutStates.TryGetValue(instanceId, out var state))
            {
                state = new PanelTitleLayoutState(tmp.fontSize, null, rectTransform.anchoredPosition);
                UnitActionButtonLayoutStates[instanceId] = state;
                Logger?.LogInfo(
                    $"Unit action button layout: text={PlainText(value)}, type=TMP, font={tmp.fontSize:0.##}->{tmp.fontSize * UnitActionButtonFontScale:0.##}, path={BuildTransformPath(component.transform)}");
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = Mathf.Max(8f, state.TmpFontSize!.Value * UnitActionButtonFontScale);
            rectTransform.anchoredPosition = state.AnchoredPosition +
                                             Vector2.down * state.TmpFontSize.Value * UnitActionButtonDownShiftScale;
        }
        else if (component is LegacyText legacy)
        {
            var rectTransform = component.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;
            if (!UnitActionButtonLayoutStates.TryGetValue(instanceId, out var state))
            {
                state = new PanelTitleLayoutState(null, legacy.fontSize, rectTransform.anchoredPosition);
                UnitActionButtonLayoutStates[instanceId] = state;
                Logger?.LogInfo(
                    $"Unit action button layout: text={PlainText(value)}, type=Legacy, font={legacy.fontSize}->{legacy.fontSize * UnitActionButtonFontScale:0.##}, path={BuildTransformPath(component.transform)}");
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.Max(8, Mathf.RoundToInt(state.LegacyFontSize!.Value * UnitActionButtonFontScale));
            rectTransform.anchoredPosition = state.AnchoredPosition +
                                             Vector2.down * state.LegacyFontSize.Value * UnitActionButtonDownShiftScale;
        }
        else if (component is TextMesh textMesh)
        {
            if (!UnitActionButtonLayoutStates.TryGetValue(instanceId, out var state))
            {
                state = new PanelTitleLayoutState(
                    null, null, default, textMesh.characterSize, textMesh.transform.localPosition);
                UnitActionButtonLayoutStates[instanceId] = state;
                Logger?.LogInfo(
                    $"Unit action button layout: text={PlainText(value)}, type=TextMesh, characterSize={textMesh.characterSize:0.###}->{textMesh.characterSize * UnitActionButtonFontScale:0.###}, path={BuildTransformPath(component.transform)}");
            }
            textMesh.characterSize = state.TextMeshCharacterSize!.Value * UnitActionButtonFontScale;
            textMesh.transform.localPosition = state.LocalPosition!.Value +
                                               Vector3.down * state.TextMeshCharacterSize.Value * UnitActionButtonDownShiftScale;
        }
    }

    private static void ApplyMotorPoolTitleFontLayout(Component component, string value)
    {
        var plain = PlainText(value);
        if (!plain.Equals("MOTOR POOL", StringComparison.OrdinalIgnoreCase) &&
            !plain.Equals("单位库", StringComparison.Ordinal))
            return;

        ApplyCompactFontLayout(
            component,
            MotorPoolTitleFontStates,
            MotorPoolTitleFontScale,
            8f,
            "Motor-pool title");

        var instanceId = component.GetInstanceID();
        if (component.transform is RectTransform rect)
        {
            if (!MotorPoolTitlePositions.TryGetValue(instanceId, out var originalPosition))
            {
                originalPosition = rect.anchoredPosition;
                MotorPoolTitlePositions[instanceId] = originalPosition;
            }
            var originalFontSize = MotorPoolTitleFontStates[instanceId].TmpFontSize ??
                                   MotorPoolTitleFontStates[instanceId].LegacyFontSize ?? 0f;
            rect.anchoredPosition = originalPosition +
                                    Vector2.down * originalFontSize * MotorPoolTitleDownShiftScale;
        }
    }

    private static void ApplyUnitCardNameFontLayout(Component component, string value)
    {
        if (!IsUnitCardCrewName(component))
            return;

        ApplyCompactFontLayout(
            component,
            UnitCardNameFontStates,
            UnitCardNameFontScale,
            7f,
            "Unit-card white name");
    }

    private static bool IsUnitCardCrewName(Component component)
    {
        if (!component.name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
            !HasAncestorContaining(component.transform, "crewMiniNew", 3))
            return false;

        Transform? current = component.transform;
        var depth = 0;
        while (current != null && depth++ < 8)
        {
            var unitCard = current.GetComponent<UICardInstanceButton>();
            if (unitCard != null && unitCard.unitInstance != null)
                return true;
            current = current.parent;
        }
        return false;
    }

    private static void ApplyCompactFontLayout(
        Component component,
        Dictionary<int, FontLayoutState> states,
        float scale,
        float minimum,
        string logLabel)
    {
        var instanceId = component.GetInstanceID();
        if (component is TMP_Text tmp)
        {
            if (!states.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(tmp.fontSize, null);
                states[instanceId] = state;
                Logger?.LogInfo(
                    $"{logLabel} layout: text={PlainText(tmp.text)}, font={tmp.fontSize:0.##}->{tmp.fontSize * scale:0.##}, path={BuildTransformPath(tmp.transform)}");
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = Mathf.Max(minimum, state.TmpFontSize!.Value * scale);
        }
        else if (component is LegacyText legacy)
        {
            if (!states.TryGetValue(instanceId, out var state))
            {
                state = new FontLayoutState(null, legacy.fontSize);
                states[instanceId] = state;
            }
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.Max(
                Mathf.RoundToInt(minimum),
                Mathf.RoundToInt(state.LegacyFontSize!.Value * scale));
        }
    }

    private static bool IsSoundSettingsUi(Component component, out string path, out float screenY)
    {
        var names = new List<string>();
        var transform = component.transform;
        var current = transform;
        var depth = 0;
        while (current != null && depth++ < 12)
        {
            names.Add(current.name);
            if (current.name.Contains("sound", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("master", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("music", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("sfx", StringComparison.OrdinalIgnoreCase))
            {
                path = string.Join("/", names);
                screenY = GetScreenY(transform);
                return true;
            }
            if (current.name.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("visual", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("graphics", StringComparison.OrdinalIgnoreCase))
            {
                path = string.Join("/", names);
                screenY = GetScreenY(transform);
                return false;
            }
            current = current.parent;
        }

        path = string.Join("/", names);
        screenY = GetScreenY(transform);
        return screenY > Screen.height * 0.5f;
    }

    private static float GetScreenY(Transform transform)
    {
        var camera = Camera.main;
        if (camera != null)
        {
            var projected = camera.WorldToScreenPoint(transform.position);
            if (projected.z > 0f)
                return projected.y;
        }
        return transform.position.y;
    }

    private static bool IsTurretFront(Component component, out string path, out float screenX)
    {
        var names = new List<string>();
        var transform = component.transform;
        var current = transform;
        var depth = 0;
        while (current != null && depth++ < 12)
        {
            names.Add(current.name);
            if (current.name.Contains("turret", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("topfrontal", StringComparison.OrdinalIgnoreCase))
            {
                path = string.Join("/", names);
                screenX = transform.position.x;
                return true;
            }
            if (current.name.Contains("body", StringComparison.OrdinalIgnoreCase) ||
                current.name.Equals("frontal", StringComparison.OrdinalIgnoreCase))
            {
                path = string.Join("/", names);
                screenX = transform.position.x;
                return false;
            }
            current = current.parent;
        }

        path = string.Join("/", names);
        screenX = transform.position.x;
        return screenX > Screen.width * 0.5f;
    }

    private static bool TryTranslate(string value, out string translated)
    {
        TraceMenuTranslationEntry(value);
        if (IsScreenStatusTemplateText(value))
        {
            // The world-space warning prefab uses STATUS on every line as a
            // sentinel. The game replaces the block only while those markers
            // are intact, so translating them during activation prevents the
            // real warning from ever being populated until Alt+T restores it.
            translated = value;
            return false;
        }
        if (TranslationCache.TryGetValue(value, out var cached))
        {
            translated = cached;
            return !string.Equals(value, cached, StringComparison.Ordinal);
        }
        if (TryNormalizeAmmunitionWarningTokens(value, out translated))
            return true;
        if (TryNormalizeEngineWarningTokens(value, out translated))
            return true;
        if (TryNormalizeDynamicStatusTokens(value, out translated))
            return true;
        if (value.Contains("DAY", StringComparison.OrdinalIgnoreCase) || value.Contains('日'))
        {
            var campaignDayFormat = CampaignDayFormatRegex.Match(PlainText(value));
            if (campaignDayFormat.Success)
            {
                translated = $"第{campaignDayFormat.Groups[1].Value}天";
                CacheTranslation(value, translated);
                return true;
            }
            var campaignDayCounter = CampaignDayCounterRegex.Match(value);
            if (campaignDayCounter.Success)
            {
                translated = $"第{campaignDayCounter.Groups[1].Value}天";
                CacheTranslation(value, translated);
                return true;
            }
        }
        if (TryTranslateModuleStatLabel(value, out translated))
        {
            CacheTranslation(value, translated);
            return true;
        }
        if (ExactMappingsOrdinal.TryGetValue(value, out var exact) ||
            ExactMappingsIgnoreCase.TryGetValue(value, out exact))
        {
            translated = exact;
            CacheTranslation(value, translated);
            return !string.Equals(value, translated, StringComparison.Ordinal);
        }

        var plainWarning = value;
        if (value.IndexOf('<') >= 0 &&
            (value.Contains("ENGINE", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("AMMUNITION", StringComparison.OrdinalIgnoreCase)))
        {
            plainWarning = PlainText(value);
        }
        if (plainWarning.StartsWith("LOW", StringComparison.OrdinalIgnoreCase) ||
            plainWarning.StartsWith("INSUFFICIENT", StringComparison.OrdinalIgnoreCase))
        {
            var lowEngineTorque = LowEngineTorqueRegex.Match(plainWarning);
            if (lowEngineTorque.Success)
            {
                translated = $"发动机扭矩过低：{lowEngineTorque.Groups[1].Value}";
                CacheTranslation(value, translated);
                return true;
            }
            if (LowEnginePowerRegex.IsMatch(plainWarning))
            {
                translated = "发动机功率不足";
                CacheTranslation(value, translated);
                return true;
            }
        }
        if (value.StartsWith("REQUIRES", StringComparison.OrdinalIgnoreCase))
        {
            var requiresRole = RequiresRoleRegex.Match(value);
            if (requiresRole.Success)
            {
                var role = requiresRole.Groups[1].Value.Trim();
                if (!CrewRoleTranslations.TryGetValue(role, out var translatedRole) &&
                    !TryTranslate(role, out translatedRole))
                    translatedRole = role;
                translated = $"需要{translatedRole}职务";
                CacheTranslation(value, translated);
                return true;
            }
        }
        if (value.StartsWith("NO", StringComparison.OrdinalIgnoreCase))
        {
            var noAmmoFor = NoAmmoForRegex.Match(value);
            if (noAmmoFor.Success)
            {
                var weapon = noAmmoFor.Groups[1].Value.Trim();
                if (!TryTranslate(weapon, out var translatedWeapon))
                    translatedWeapon = weapon;
                translated = $"{translatedWeapon}无弹药";
                CacheTranslation(value, translated);
                return true;
            }
        }
        if (value.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase))
        {
            var missingCrew = MissingCrewRegex.Match(value);
            if (missingCrew.Success)
            {
                var role = missingCrew.Groups[1].Value.Trim();
                if (!CrewRoleTranslations.TryGetValue(role, out var translatedRole) &&
                    !TryTranslate(role, out translatedRole))
                    translatedRole = role;
                translated = $"缺少乘员：{translatedRole}";
                CacheTranslation(value, translated);
                return true;
            }
        }
        if (value.StartsWith("repairs", StringComparison.OrdinalIgnoreCase))
        {
            var repairEta = RepairEtaRegex.Match(value);
            if (repairEta.Success)
            {
                translated = $"维修将在 {repairEta.Groups[1].Value} 天后完成";
                CacheTranslation(value, translated);
                return true;
            }
        }

        // Run broad module-token replacement only after complete dynamic
        // warnings, so LOW ENGINE TORQUE/POWER retain their dedicated wording.
        if (TryNormalizeModuleUiTokens(value, out translated))
            return true;

        if (!ContainsVisibleLatinLetter(value))
        {
            translated = value;
            CacheTranslation(value, value);
            return false;
        }

        var result = value;
        var changed = false;
        foreach (var mapping in Mappings)
        {
            if (mapping.FirstToken.Length > value.Length)
                continue;
            if (value.IndexOf(mapping.FirstToken, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            result = ReplaceSegment(result, mapping, ref changed);
        }
        CacheTranslation(value, result);
        if (changed && ChineseCharacterRegex.IsMatch(result) && LongEnglishRunRegex.IsMatch(result) &&
            FragmentedTranslationsLogged.Add(value))
        {
            Logger?.LogWarning(
                $"Possible fragmented translation; add an exact full-string mapping. source={value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)} | result={result.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}");
        }
        translated = result;
        return changed;
    }

    private static bool TryNormalizeAmmunitionWarningTokens(string value, out string translated)
    {
        var hasLowAmmunition = value.Contains("LOW", StringComparison.OrdinalIgnoreCase) &&
                               (value.Contains("AMMUNITION", StringComparison.OrdinalIgnoreCase) ||
                                value.Contains("弹药", StringComparison.Ordinal));
        var hasNoAmmoFor = value.Contains("NO AMMO FOR", StringComparison.OrdinalIgnoreCase);
        if (!hasLowAmmunition && !hasNoAmmoFor)
        {
            translated = value;
            return false;
        }

        var normalized = value;
        if (hasNoAmmoFor)
        {
            normalized = NoAmmoForTokenRegex.Replace(normalized, match =>
            {
                var weapon = match.Groups["weapon"].Value.Trim();
                if (!TryTranslate(weapon, out var translatedWeapon))
                    translatedWeapon = weapon;
                return $"{translatedWeapon}无弹药";
            });
        }
        if (hasLowAmmunition)
        {
            normalized = LowAmmunitionWarningTokenRegex.Replace(normalized, match =>
            {
                var ammunition = match.Groups["ammo"].Value.Trim();
                if (!TryTranslate(ammunition, out var translatedAmmunition))
                    translatedAmmunition = ammunition;
                var category = match.Groups["category"].Success ? "\n[弹药]" : string.Empty;
                return $"弹药不足：{translatedAmmunition}{category}";
            });
        }
        if (string.Equals(value, normalized, StringComparison.Ordinal))
        {
            translated = value;
            return false;
        }

        if (!TryTranslate(normalized, out translated))
            translated = normalized;
        CacheTranslation(value, translated);
        return true;
    }

    private static bool TryNormalizeEngineWarningTokens(string value, out string translated)
    {
        var normalized = value;
        if (normalized.Contains("TORQUE", StringComparison.OrdinalIgnoreCase))
        {
            normalized = EngineTorqueWarningTokenRegex.Replace(
                normalized,
                match => $"发动机扭矩过低：{match.Groups[1].Value}");
            normalized = PartiallyTranslatedEngineTorqueWarningRegex.Replace(
                normalized,
                match => $"发动机扭矩过低：{match.Groups[1].Value}");
        }
        if (normalized.Contains("POWER", StringComparison.OrdinalIgnoreCase))
        {
            normalized = EnginePowerWarningTokenRegex.Replace(normalized, "发动机功率不足");
            normalized = PartiallyTranslatedEnginePowerWarningRegex.Replace(normalized, "发动机功率不足");
        }

        if (string.Equals(value, normalized, StringComparison.Ordinal))
        {
            translated = value;
            return false;
        }

        // Continue translating the other warnings that share this text block.
        if (!TryTranslate(normalized, out translated))
            translated = normalized;
        CacheTranslation(value, translated);
        return true;
    }

    private static bool IsScreenStatusTemplateText(string value)
    {
        if (!value.Contains('\n') || !value.Contains("STATUS", StringComparison.Ordinal))
            return false;

        var lines = PlainText(value).Split('\n');
        var statusLineCount = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (!trimmed.Equals("STATUS", StringComparison.Ordinal))
                return false;
            statusLineCount++;
        }
        return statusLineCount >= 2;
    }

    private static bool TryNormalizeDynamicStatusTokens(string value, out string translated)
    {
        var normalized = value;
        if (normalized.Contains("JAMMER", StringComparison.Ordinal))
            normalized = JammerStatusTokenRegex.Replace(normalized, "干扰机");
        if (normalized.Contains("SMOKE", StringComparison.Ordinal))
            normalized = SmokeStatusTokenRegex.Replace(normalized, "烟幕装置");
        if (normalized.Contains("烟幕", StringComparison.Ordinal))
            normalized = PartiallyTranslatedSmokeStatusRegex.Replace(normalized, "烟幕装置");
        if (normalized.Contains("BROKEN", StringComparison.Ordinal))
            normalized = BrokenStatusTokenRegex.Replace(normalized, "损坏");
        if (normalized.Contains("DAMAGED", StringComparison.Ordinal))
            normalized = DamagedStatusTokenRegex.Replace(normalized, "受损");
        if (normalized.Contains("FAILURE", StringComparison.Ordinal))
            normalized = FailureStatusTokenRegex.Replace(normalized, "故障");
        if (normalized.Contains("REDUCED", StringComparison.Ordinal))
            normalized = ReducedStatusTokenRegex.Replace(normalized, "降低");
        if (normalized.Contains("UNSAFE", StringComparison.Ordinal))
            normalized = UnsafeStatusTokenRegex.Replace(normalized, "危险");
        if (normalized.Contains("ROUTED", StringComparison.Ordinal))
            normalized = RoutedStatusTokenRegex.Replace(normalized, "溃逃");
        if (normalized.Contains("BLEEDOUT", StringComparison.Ordinal))
            normalized = BleedoutStatusTokenRegex.Replace(normalized, "失血");
        if (normalized.Contains("STUNNED", StringComparison.Ordinal))
            normalized = StunnedStatusTokenRegex.Replace(normalized, "眩晕");

        if (string.Equals(value, normalized, StringComparison.Ordinal))
        {
            translated = value;
            return false;
        }

        // Continue through the normal pipeline so a composite such as
        // "SMOKE BROKEN" becomes fully localized, not merely "SMOKE 损坏".
        if (!TryTranslate(normalized, out translated))
            translated = normalized;
        CacheTranslation(value, translated);
        return true;
    }

    private static bool TryNormalizeModuleUiTokens(string value, out string translated)
    {
        var normalized = ModuleUiTokenRegex.Replace(
            value,
            match => ModuleUiTokenTranslations[match.Value]);
        normalized = DismountCountTokenRegex.Replace(
            normalized,
            match => $"{match.Groups["count"].Value}人制下车步兵");

        // The replenishment module presents these three ammunition classes in
        // the same dynamic multiline block. Keep LIGHT context-sensitive so it
        // does not collide with damage severities or amount labels elsewhere.
        var replenishmentBlock =
            value.Contains("MEDICAL MATERIALS", StringComparison.Ordinal) ||
            value.Contains("REPAIR MATERIALS", StringComparison.Ordinal) ||
            value.Contains("AMMUNITION REPLENISHMENT", StringComparison.Ordinal) ||
            value.Contains("AMMO REPLENISHMENT", StringComparison.Ordinal) ||
            value.Contains("医疗物资", StringComparison.Ordinal) ||
            value.Contains("维修物资", StringComparison.Ordinal) ||
            value.Contains("弹药补给", StringComparison.Ordinal);
        if (replenishmentBlock)
        {
            normalized = ReplenishmentClassTokenRegex.Replace(
                normalized,
                match => ReplenishmentClassTranslations[match.Value]);
        }

        if (string.Equals(value, normalized, StringComparison.Ordinal))
        {
            translated = value;
            return false;
        }

        // Preserve model names and numeric values while allowing any remaining
        // exact mappings in the multiline block to run normally.
        if (!TryTranslate(normalized, out translated))
            translated = normalized;
        CacheTranslation(value, translated);
        return true;
    }

    private static void TraceMenuTranslationEntry(string value)
    {
        if (!IsSettingsMenuCommandSource(value) || MenuTranslationEntryTracesLogged.Count >= 8)
            return;

        var stack = Environment.StackTrace
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " <= ", StringComparison.Ordinal);
        var key = $"{value}|{stack}";
        if (!MenuTranslationEntryTracesLogged.Add(key))
            return;

        Logger?.LogInfo(
            $"MENU TRANSLATION TRACE: source={PlainText(value)}, scene={ActiveSceneName}, frame={Time.frameCount}, stack={stack}");
    }

    private static bool TryTranslateModuleStatLabel(string value, out string translated)
    {
        var plain = PlainText(value);
        if (plain.Length == 0 || plain.Length > 32)
        {
            translated = string.Empty;
            return false;
        }

        Span<char> normalized = stackalloc char[plain.Length];
        var length = 0;
        foreach (var character in plain)
        {
            if (char.IsLetter(character))
                normalized[length++] = char.ToUpperInvariant(character);
        }
        var key = normalized[..length];
        foreach (var pair in ModuleStatLabels)
        {
            if (!key.SequenceEqual(pair.Key.AsSpan()))
                continue;
            translated = pair.Value;
            return true;
        }

        translated = string.Empty;
        return false;
    }

    private static bool ContainsVisibleLatinLetter(string value)
    {
        var insideTag = false;
        foreach (var character in value)
        {
            if (character == '<')
            {
                insideTag = true;
                continue;
            }
            if (character == '>')
            {
                insideTag = false;
                continue;
            }
            if (insideTag)
                continue;
            if ((character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z'))
                return true;
        }
        return false;
    }

    private static void CacheTranslation(string source, string result)
    {
        if (TranslationCache.ContainsKey(source))
        {
            TranslationCache[source] = result;
            return;
        }
        while (TranslationCache.Count >= TranslationCacheCapacity && TranslationCacheOrder.Count > 0)
            TranslationCache.Remove(TranslationCacheOrder.Dequeue());
        TranslationCache[source] = result;
        TranslationCacheOrder.Enqueue(source);
    }

    private static void StartMappingWatcher()
    {
        var directory = Path.GetDirectoryName(MappingPath);
        var fileName = Path.GetFileName(MappingPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) ||
            !Directory.Exists(directory))
        {
            Logger?.LogWarning("External localization directory does not exist; live mapping reload is disabled.");
            return;
        }

        try
        {
            MappingWatcher = new FileSystemWatcher(directory, fileName)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            MappingWatcher.Changed += MappingFileChanged;
            MappingWatcher.Created += MappingFileChanged;
            MappingWatcher.Deleted += MappingFileChanged;
            MappingWatcher.Renamed += MappingFileRenamed;
            MappingWatcher.EnableRaisingEvents = true;
            Logger?.LogInfo("Watching the external localization map for file-change events.");
        }
        catch (System.Exception ex)
        {
            MappingWatcher?.Dispose();
            MappingWatcher = null;
            Logger?.LogWarning($"Could not watch external localization mappings: {ex.Message}");
        }
    }

    private static void MappingFileChanged(object sender, FileSystemEventArgs args)
    {
        MappingReloadRequested = true;
    }

    private static void MappingFileRenamed(object sender, RenamedEventArgs args)
    {
        MappingReloadRequested = true;
    }

    private static void ReloadMappingsIfChanged(bool force = false)
    {
        DateTime timestamp;
        try
        {
            timestamp = File.Exists(MappingPath) ? File.GetLastWriteTimeUtc(MappingPath) : DateTime.MinValue;
        }
        catch (System.Exception ex)
        {
            Logger?.LogWarning($"Could not inspect external localization mappings: {ex.Message}");
            return;
        }
        if (!force && MappingsLoaded && timestamp == MappingTimestampUtc)
            return;

        var rawMappings = new Dictionary<string, RawMapping>(StringComparer.Ordinal);
        var duplicateCount = 0;
        var conflictingDuplicateCount = 0;
        var order = 0;
        if (File.Exists(MappingPath))
        {
            try
            {
                foreach (var rawLine in File.ReadLines(MappingPath, new System.Text.UTF8Encoding(false)))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    var separator = FindSeparator(line);
                    if (separator <= 0)
                        continue;
                    var original = Unescape(line[..separator]);
                    var translation = Unescape(line[(separator + 1)..]);
                    if (original.Length > 0)
                    {
                        if (rawMappings.TryGetValue(original, out var previous))
                        {
                            duplicateCount++;
                            if (!string.Equals(previous.Value, translation, StringComparison.Ordinal))
                                conflictingDuplicateCount++;
                        }
                        rawMappings[original] = new RawMapping(original, translation, order++);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger?.LogWarning($"Could not read external localization mappings: {ex.Message}");
                // A translation file can briefly be unavailable while an
                // editor or deployment process replaces it. Keep the last
                // complete map and retry after the check interval instead of
                // collapsing the live UI to the tiny built-in fallback map.
                if (MappingsLoaded)
                    return;
            }
        }

        if (rawMappings.Count == 0)
        {
            rawMappings[FirstOriginal] = new RawMapping(FirstOriginal, FirstTranslation, order++);
            var normalizedFirst = FirstOriginal.Replace("  ", " ", StringComparison.Ordinal);
            rawMappings[normalizedFirst] = new RawMapping(normalizedFirst, FirstTranslation, order++);
            rawMappings[SecondOriginal] = new RawMapping(SecondOriginal, SecondTranslation, order++);
        }

        var next = new List<MappingEntry>(rawMappings.Count);
        var nextExactOrdinal = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextExactIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawMappings.Values.OrderBy(item => item.Order))
        {
            next.Add(new MappingEntry(raw.Key, raw.Value));
            if (CaseSensitiveKeys.Contains(raw.Key.Trim()))
                nextExactOrdinal[raw.Key] = raw.Value;
            else
                nextExactIgnoreCase[raw.Key] = raw.Value;
        }
        next.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
        Mappings.Clear();
        Mappings.AddRange(next);
        ExactMappingsOrdinal.Clear();
        foreach (var pair in nextExactOrdinal)
            ExactMappingsOrdinal[pair.Key] = pair.Value;
        ExactMappingsIgnoreCase.Clear();
        foreach (var pair in nextExactIgnoreCase)
            ExactMappingsIgnoreCase[pair.Key] = pair.Value;
        TranslationCache.Clear();
        TranslationCacheOrder.Clear();
        // Existing controls may still contain unchanged English text that was
        // cached before a newly added mapping existed. Revisit them once after
        // a mapping reload so hot-added translations appear without Alt+T.
        LastProcessedTexts.Clear();
        SceneScanRequested = true;
        MappingTimestampUtc = timestamp;
        MappingsLoaded = true;
        Logger?.LogInfo(
            $"Loaded {Mappings.Count} unique localization mappings from {MappingPath} " +
            $"({duplicateCount} duplicate rows ignored, {conflictingDuplicateCount} conflicting rows resolved by last entry).");
    }

    private static string Unescape(string value)
    {
        return value.Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\=", "=", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static int FindSeparator(string line)
    {
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '=')
                return index;
        }

        return -1;
    }

    private static string ReplaceSegment(string value, MappingEntry mapping, ref bool changed)
    {
        var replaced = mapping.Pattern.Replace(value, mapping.Value);
        if (replaced != value)
            changed = true;
        return replaced;
    }

    private sealed class MappingEntry
    {
        private readonly string _patternSource;
        private readonly RegexOptions _options;
        private Regex? _pattern;

        public MappingEntry(string key, string value)
        {
            Key = key;
            Value = value;
            var trimmedKey = key.Trim();
            var rawWords = trimmedKey.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var words = rawWords.Select(Regex.Escape).ToArray();
            if (words.Length == 0)
                throw new ArgumentException("Mapping key is empty.", nameof(key));

            FirstToken = rawWords[0];
            var pattern = string.Join(@"\s+", words);
            var trimmedValue = value.Trim();
            var fragmentOnly = trimmedValue.StartsWith("……", StringComparison.Ordinal) ||
                               trimmedValue.EndsWith("……", StringComparison.Ordinal);
            if (ExactUiOnlyKeys.Contains(trimmedKey) || fragmentOnly)
                pattern = $@"^\s*{pattern}\s*$";
            else
            {
                if (IsAsciiLetterOrDigit(trimmedKey[0]))
                    pattern = $@"(?<![A-Za-z0-9]){pattern}";
                if (IsAsciiLetterOrDigit(trimmedKey[^1]))
                    pattern = $@"{pattern}(?![A-Za-z0-9])";
            }
            _options = RegexOptions.CultureInvariant;
            if (!CaseSensitiveKeys.Contains(trimmedKey))
                _options |= RegexOptions.IgnoreCase;
            _patternSource = pattern;
        }

        public string Key { get; }
        public string Value { get; }
        public string FirstToken { get; }
        public Regex Pattern => _pattern ??= new Regex(_patternSource, _options);

        private static bool IsAsciiLetterOrDigit(char character) =>
            (character >= 'A' && character <= 'Z') ||
            (character >= 'a' && character <= 'z') ||
            (character >= '0' && character <= '9');
    }

    private sealed class RawMapping
    {
        public RawMapping(string key, string value, int order)
        {
            Key = key;
            Value = value;
            Order = order;
        }

        public string Key { get; }
        public string Value { get; }
        public int Order { get; }
    }

    private static bool HandleToggleHotkey()
    {
        var altDown = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (!altDown || !Input.GetKeyDown(KeyCode.T))
            return false;

        TranslationsEnabled = !TranslationsEnabled;
        LastProcessedTexts.Clear();
        Logger?.LogInfo($"HarmonyX translations {(TranslationsEnabled ? "enabled" : "disabled")} by Alt+T.");
        return true;
    }

    private static void RestoreIfDisabled(int instanceId, Component component)
    {
        if (PanelTitleLayoutStates.TryGetValue(instanceId, out var panelTitleState))
        {
            if (component is TMP_Text panelTmp && panelTitleState.TmpFontSize.HasValue)
                panelTmp.fontSize = panelTitleState.TmpFontSize.Value;
            else if (component is LegacyText panelLegacy && panelTitleState.LegacyFontSize.HasValue)
                panelLegacy.fontSize = panelTitleState.LegacyFontSize.Value;
            else if (component is TextMesh panelTextMesh && panelTitleState.TextMeshCharacterSize.HasValue)
            {
                panelTextMesh.characterSize = panelTitleState.TextMeshCharacterSize.Value;
                if (panelTitleState.LocalPosition.HasValue)
                    panelTextMesh.transform.localPosition = panelTitleState.LocalPosition.Value;
            }
            if (component.transform is RectTransform panelRect)
                panelRect.anchoredPosition = panelTitleState.AnchoredPosition;
            PanelTitleLayoutStates.Remove(instanceId);
        }

        if (UnitActionButtonLayoutStates.TryGetValue(instanceId, out var actionButtonState))
        {
            if (component is TMP_Text actionTmp && actionButtonState.TmpFontSize.HasValue)
                actionTmp.fontSize = actionButtonState.TmpFontSize.Value;
            else if (component is LegacyText actionLegacy && actionButtonState.LegacyFontSize.HasValue)
                actionLegacy.fontSize = actionButtonState.LegacyFontSize.Value;
            else if (component is TextMesh actionTextMesh && actionButtonState.TextMeshCharacterSize.HasValue)
            {
                actionTextMesh.characterSize = actionButtonState.TextMeshCharacterSize.Value;
                if (actionButtonState.LocalPosition.HasValue)
                    actionTextMesh.transform.localPosition = actionButtonState.LocalPosition.Value;
            }
            if (component.transform is RectTransform actionRect)
                actionRect.anchoredPosition = actionButtonState.AnchoredPosition;
            UnitActionButtonLayoutStates.Remove(instanceId);
        }

        if (MotorPoolTitlePositions.TryGetValue(instanceId, out var motorPoolTitlePosition) &&
            component.transform is RectTransform motorPoolTitleRect)
        {
            motorPoolTitleRect.anchoredPosition = motorPoolTitlePosition;
        }
        MotorPoolTitlePositions.Remove(instanceId);
        RestoreCompactFontLayout(instanceId, component, MotorPoolTitleFontStates);
        RestoreCompactFontLayout(instanceId, component, UnitCardNameFontStates);

        if (StatusOverlayFontStates.TryGetValue(instanceId, out var statusFontState))
        {
            if (component is TMP_Text statusTmp && statusFontState.TmpFontSize.HasValue)
                statusTmp.fontSize = statusFontState.TmpFontSize.Value;
            else if (component is LegacyText statusLegacy && statusFontState.LegacyFontSize.HasValue)
                statusLegacy.fontSize = statusFontState.LegacyFontSize.Value;
            StatusOverlayFontStates.Remove(instanceId);
        }

        if (PauseMenuButtonFontStates.TryGetValue(instanceId, out var pauseMenuFontState))
        {
            if (component is TMP_Text pauseMenuTmp && pauseMenuFontState.TmpFontSize.HasValue)
                pauseMenuTmp.fontSize = pauseMenuFontState.TmpFontSize.Value;
            else if (component is LegacyText pauseMenuLegacy && pauseMenuFontState.LegacyFontSize.HasValue)
                pauseMenuLegacy.fontSize = pauseMenuFontState.LegacyFontSize.Value;
            PauseMenuButtonFontStates.Remove(instanceId);
        }

        if (OptionsMenuFontStates.TryGetValue(instanceId, out var optionsFontState))
        {
            if (component is TMP_Text optionsTmp && optionsFontState.TmpFontSize.HasValue)
            {
                optionsTmp.fontSize = optionsFontState.TmpFontSize.Value;
                if (OptionsMenuTmpAlignments.TryGetValue(instanceId, out var alignment))
                    optionsTmp.horizontalAlignment = alignment;
            }
            else if (component is LegacyText optionsLegacy && optionsFontState.LegacyFontSize.HasValue)
            {
                optionsLegacy.fontSize = optionsFontState.LegacyFontSize.Value;
                if (OptionsMenuLegacyAlignments.TryGetValue(instanceId, out var alignment))
                    optionsLegacy.alignment = alignment;
            }
            OptionsMenuFontStates.Remove(instanceId);
            OptionsMenuTmpAlignments.Remove(instanceId);
            OptionsMenuLegacyAlignments.Remove(instanceId);
        }

        if (OptionsMenuTextMeshStates.TryGetValue(instanceId, out var optionsTextMeshState) &&
            component is TextMesh optionsTextMesh)
        {
            optionsTextMesh.fontSize = optionsTextMeshState.FontSize;
            optionsTextMesh.characterSize = optionsTextMeshState.CharacterSize;
            if (OptionsMenuTextMeshAnchors.TryGetValue(instanceId, out var anchor))
                optionsTextMesh.anchor = anchor;
            if (OptionsMenuTextMeshAlignments.TryGetValue(instanceId, out var alignment))
                optionsTextMesh.alignment = alignment;
            OptionsMenuTextMeshStates.Remove(instanceId);
            OptionsMenuTextMeshAnchors.Remove(instanceId);
            OptionsMenuTextMeshAlignments.Remove(instanceId);
        }

        if (MenuTextMeshFontStates.TryGetValue(instanceId, out var menuTextMeshState) &&
            component is TextMesh menuTextMesh)
        {
            menuTextMesh.fontSize = menuTextMeshState.FontSize;
            menuTextMesh.characterSize = menuTextMeshState.CharacterSize;
            MenuTextMeshFontStates.Remove(instanceId);
        }

        if (ObjectiveFontStates.TryGetValue(instanceId, out var fontState))
        {
            if (component is TMP_Text tmpText && fontState.TmpFontSize.HasValue)
                tmpText.fontSize = fontState.TmpFontSize.Value;
            else if (component is LegacyText legacy && fontState.LegacyFontSize.HasValue)
                legacy.fontSize = fontState.LegacyFontSize.Value;
            ObjectiveFontStates.Remove(instanceId);
        }

        if (!AppliedStates.TryGetValue(instanceId, out var state))
            return;

        if (component is TMP_Text tmp)
        {
            if (!string.Equals(tmp.text, state.Original, StringComparison.Ordinal))
                tmp.text = state.Original;
            LastProcessedTexts[instanceId] = state.Original;
        }
        else if (component is LegacyText legacy)
        {
            if (!string.Equals(legacy.text, state.Original, StringComparison.Ordinal))
                legacy.text = state.Original;
            LastProcessedTexts[instanceId] = state.Original;
        }
        else if (component is TextMesh textMesh)
        {
            if (!string.Equals(textMesh.text, state.Original, StringComparison.Ordinal))
                textMesh.text = state.Original;
            LastProcessedTexts[instanceId] = state.Original;
        }

        AppliedStates.Remove(instanceId);
    }

    private static void RestoreCompactFontLayout(
        int instanceId,
        Component component,
        Dictionary<int, FontLayoutState> states)
    {
        if (!states.TryGetValue(instanceId, out var state))
            return;

        if (component is TMP_Text tmp && state.TmpFontSize.HasValue)
            tmp.fontSize = state.TmpFontSize.Value;
        else if (component is LegacyText legacy && state.LegacyFontSize.HasValue)
            legacy.fontSize = state.LegacyFontSize.Value;
        states.Remove(instanceId);
    }

    private sealed class TextState
    {
        public TextState(string original, string translated)
        {
            Original = original;
            Translated = translated;
        }

        public string Original { get; }
        public string Translated { get; }
    }

    private sealed class FontLayoutState
    {
        public FontLayoutState(float? tmpFontSize, int? legacyFontSize)
        {
            TmpFontSize = tmpFontSize;
            LegacyFontSize = legacyFontSize;
        }

        public float? TmpFontSize { get; }
        public int? LegacyFontSize { get; }
    }

    private sealed class TextMeshFontLayoutState
    {
        public TextMeshFontLayoutState(int fontSize, float characterSize)
        {
            FontSize = fontSize;
            CharacterSize = characterSize;
        }

        public int FontSize { get; }
        public float CharacterSize { get; }
    }

    private sealed class MissionListFontState
    {
        public MissionListFontState(float fontSize, bool autoSizing, float fontSizeMin, float fontSizeMax)
        {
            FontSize = fontSize;
            AutoSizing = autoSizing;
            FontSizeMin = fontSizeMin;
            FontSizeMax = fontSizeMax;
        }

        public float FontSize { get; }
        public bool AutoSizing { get; }
        public float FontSizeMin { get; }
        public float FontSizeMax { get; }
    }

    private sealed class PanelTitleLayoutState
    {
        public PanelTitleLayoutState(
            float? tmpFontSize,
            int? legacyFontSize,
            Vector2 anchoredPosition,
            float? textMeshCharacterSize = null,
            Vector3? localPosition = null)
        {
            TmpFontSize = tmpFontSize;
            LegacyFontSize = legacyFontSize;
            AnchoredPosition = anchoredPosition;
            TextMeshCharacterSize = textMeshCharacterSize;
            LocalPosition = localPosition;
        }

        public float? TmpFontSize { get; }
        public int? LegacyFontSize { get; }
        public Vector2 AnchoredPosition { get; }
        public float? TextMeshCharacterSize { get; }
        public Vector3? LocalPosition { get; }
    }

    private readonly struct ImGuiStyleState
    {
        public ImGuiStyleState(int fontSize, Vector2 contentOffset, TextAnchor alignment)
        {
            IsApplied = true;
            FontSize = fontSize;
            ContentOffset = contentOffset;
            Alignment = alignment;
        }

        public bool IsApplied { get; }
        public int FontSize { get; }
        public Vector2 ContentOffset { get; }
        public TextAnchor Alignment { get; }
    }

    private sealed class StatusOverlayTexts
    {
        public StatusOverlayTexts(TMP_Text[] tmpTexts, LegacyText[] legacyTexts)
        {
            TmpTexts = tmpTexts;
            LegacyTexts = legacyTexts;
        }

        public TMP_Text[] TmpTexts { get; }
        public LegacyText[] LegacyTexts { get; }
    }

    private sealed class RefreshComponent : MonoBehaviour
    {
        private bool _firstPass = true;

        private void Update()
        {
            if (MappingReloadRequested)
            {
                MappingReloadRequested = false;
                ReloadMappingsIfChanged(force: true);
            }
            var toggled = HandleToggleHotkey();

            var globalScanRequested = _firstPass || toggled || SceneScanRequested;
            if (!globalScanRequested)
                return;
            _firstPass = false;
            SceneScanRequested = false;
            ScanActiveSceneTexts();
        }
    }

    private static void ScanActiveSceneTexts()
    {
        foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!CandidateLogged && text.text.Contains("Celeres", StringComparison.OrdinalIgnoreCase))
            {
                CandidateLogged = true;
                Logger?.LogInfo($"Found TMP text containing Celeres (length {text.text.Length}).");
            }
            TranslateCurrentComponent(text);
        }
        foreach (var text in UnityEngine.Object.FindObjectsByType<LegacyText>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!CandidateLogged && text.text.Contains("SQUAD", StringComparison.OrdinalIgnoreCase))
            {
                CandidateLogged = true;
                Logger?.LogInfo($"Found legacy text containing SQUAD (length {text.text.Length}).");
            }
            TranslateCurrentComponent(text);
        }
        foreach (var text in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            TranslateCurrentComponent(text);
    }

}
