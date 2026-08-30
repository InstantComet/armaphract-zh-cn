using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using LegacyText = UnityEngine.UI.Text;

namespace Armaphract.HarmonyXLocalization;

[BepInPlugin(Guid, Name, Version)]
public sealed class HarmonyXLocalizationPlugin : BasePlugin
{
    public const string Guid = "armaphract.harmonyx.unitintro";
    public const string Name = "Armaphract HarmonyX Localization";
    public const string Version = "1.8.33";

    private static ManualLogSource? Logger;
    private static bool CandidateLogged;
    private static readonly string MappingPath = Path.Combine(
        Paths.BepInExRootPath,
        "Translation",
        "zh-CN",
        "Text",
        "armaphract_zh-CN.txt");
    private static readonly List<MappingEntry> Mappings = new();
    private static readonly Dictionary<int, TextState> AppliedStates = new();
    private static readonly Dictionary<int, FontLayoutState> ObjectiveFontStates = new();
    private static readonly Dictionary<int, FontLayoutState> StatusOverlayFontStates = new();
    private static readonly Dictionary<int, PanelTitleLayoutState> PanelTitleLayoutStates = new();
    private static readonly Dictionary<int, Component> PanelTitleComponents = new();
    private static readonly Dictionary<int, StatusOverlayTexts> StatusOverlayTextCache = new();
    private static readonly Dictionary<int, string> LastProcessedTexts = new();
    // Setting TMP_Text.text from the fallback scanner re-enters the patched
    // setter.  Keep that write out of the translation pipeline; otherwise a
    // producer can race the scanner and make the same control alternate
    // between the source and a partially translated value.
    private static readonly HashSet<int> InternalTextWrites = new();
    private static readonly Dictionary<string, string> TranslationCache = new(StringComparer.Ordinal);
    private static readonly HashSet<int> FrontLayoutLoggedIds = new();
    private static readonly HashSet<int> UiContextLoggedIds = new();
    private static readonly HashSet<int> ExitContextLoggedIds = new();
    private static readonly HashSet<int> PanelTitleContextLoggedIds = new();
    private static readonly HashSet<string> ImGuiCandidatesLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ImGuiPanelTitlesLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FragmentedTranslationsLogged = new(StringComparer.Ordinal);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjectiveCounterRegex = new(
        @"\s*\[\d+\s*/\s*\d+\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RepairEtaRegex = new(
        @"^\s*repairs\s+complete\s+in\s+(\d+)\s+days?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChineseCharacterRegex = new(
        @"[\u3400-\u9fff]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LongEnglishRunRegex = new(
        @"(?:[A-Za-z]+\s+){4,}[A-Za-z]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ExactUiOnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMMAND", "DRIVER", "GUNNER", "LOADER",
        "HIGH", "FAIR", "MEDIUM", "LOW", "WEAK", "FALLING",
        "STUNNED", "ROUTED", "BLEEDOUT"
    };
    private static readonly HashSet<string> CaseSensitiveKeys = new(StringComparer.Ordinal)
    {
        // Same English text is used for two different contexts. Preserve
        // title case for the extraction objective and uppercase for the menu.
        "Exit", "EXIT"
    };
    private static DateTime MappingTimestampUtc;
    private static DateTime NextMappingCheckUtc;
    private static bool MappingsLoaded;
    private static readonly TimeSpan MappingCheckInterval = TimeSpan.FromSeconds(1);
    private static bool TranslationsEnabled = true;
    private static bool PanelActivationTranslationInProgress;
    private static bool SceneScanRequested;
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
    private const float CampaignPanelTitleFontScale = 0.72f;
    private const float CampaignPanelTitleDownShiftScale = 0.07f;
    private const string MainMenuSceneName = "menu";
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
        ReloadMappingsIfChanged(force: true);
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
        PatchSceneLoaded(harmony);
        PatchUnitStatusWriters(harmony);
        PatchScreenStatusWriters(harmony);
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
        var prefix = new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(TmpStringWriterPrefix));
        var patched = 0;
        foreach (var method in typeof(TMP_Text).GetMethods().Where(method =>
                     method.Name == nameof(TMP_Text.SetText) &&
                     method.GetParameters().Length > 0 &&
                     method.GetParameters()[0].ParameterType == typeof(string)))
        {
            try
            {
                harmony.Patch(method, prefix: prefix);
                patched++;
            }
            catch (System.Exception ex)
            {
                Logger?.LogWarning($"Could not patch TMP string writer {method}: {ex.Message}");
            }
        }
        Logger?.LogInfo($"Patched {patched} TMP SetText string overloads.");
    }

    private static void TmpStringWriterPrefix(TMP_Text __instance, object[] __args)
    {
        if (__args.Length == 0 || __args[0] is not string value)
            return;
        TextPrefix(__instance, ref value);
        __args[0] = value;
    }

    private static void PatchPanelActivation(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) }),
            postfix: new HarmonyMethod(typeof(HarmonyXLocalizationPlugin), nameof(GameObjectSetActivePostfix)));
        Logger?.LogInfo("Patched GameObject.SetActive for event-driven panel translation.");
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
        SceneScanRequested = true;
        Logger?.LogInfo($"Scene loaded: {SceneManager.GetActiveScene().name}");
    }

    private static void GameObjectSetActivePostfix(GameObject __instance, bool __0)
    {
        if (!__0 || !TranslationsEnabled || PanelActivationTranslationInProgress ||
            __instance == null || !__instance.activeInHierarchy)
            return;
        PanelActivationTranslationInProgress = true;
        try
        {
            foreach (var text in __instance.GetComponentsInChildren<TMP_Text>(true))
                TranslateCurrentComponent(text);
            foreach (var text in __instance.GetComponentsInChildren<LegacyText>(true))
                TranslateCurrentComponent(text);
            foreach (var text in __instance.GetComponentsInChildren<TextMesh>(true))
                TranslateCurrentComponent(text);
        }
        finally
        {
            PanelActivationTranslationInProgress = false;
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
        if (LastProcessedTexts.TryGetValue(instanceId, out var last) &&
            string.Equals(current, last, StringComparison.Ordinal))
            return;
        if (string.IsNullOrEmpty(current) || !TryTranslateForDisplay(component, current, out var translated))
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

    private static void TranslateStatusOverlay(GameObject overlay)
    {
        var overlayId = overlay.GetInstanceID();
        if (!StatusOverlayTextCache.TryGetValue(overlayId, out var texts))
        {
            if (StatusOverlayTextCache.Count >= 128)
                StatusOverlayTextCache.Clear();
            texts = new StatusOverlayTexts(
                overlay.GetComponentsInChildren<TMP_Text>(true).ToList(),
                overlay.GetComponentsInChildren<LegacyText>(true).ToList());
            StatusOverlayTextCache[overlayId] = texts;
        }

        foreach (var text in texts.TmpTexts)
        {
            if (text != null)
                TranslateCurrentComponent(text);
        }
        foreach (var text in texts.LegacyTexts)
        {
            if (text != null)
                TranslateCurrentComponent(text);
        }
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
        if (__state.FontSize <= 0)
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
        if (__instance != null && __state.FontSize > 0)
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
        if ((!isManualGuide && !isCampaignPanelTitle) || !ActiveManualGuideStyles.Add(style))
            return;

        var originalFontSize = style.fontSize;
        if (originalFontSize <= 0)
        {
            ActiveManualGuideStyles.Remove(style);
            return;
        }

        state = new ImGuiStyleState(originalFontSize, style.contentOffset);
        if (isCampaignPanelTitle)
        {
            style.fontSize = Mathf.Max(10, Mathf.RoundToInt(originalFontSize * CampaignPanelTitleFontScale));
            // IMGUI uses screen coordinates, where positive Y moves content down.
            style.contentOffset = state.ContentOffset +
                                  Vector2.up * originalFontSize * CampaignPanelTitleDownShiftScale;
            var plain = HtmlTagRegex.Replace(translated, string.Empty).Trim();
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
        ActiveManualGuideStyles.Remove(style);
    }

    private static bool IsManualGuideLabel(string text)
    {
        var plain = HtmlTagRegex.Replace(text, string.Empty).Trim();
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
        var plain = HtmlTagRegex.Replace(text, string.Empty).Trim();
        if (IsCampaignScene() && plain.Equals("Exit", StringComparison.OrdinalIgnoreCase))
            text = "退出";
        else if (TryTranslate(text, out var translated))
            text = translated;
        text = AppendMainMenuVersionCredit(text);
    }

    private static bool IsCampaignScene()
    {
        return SceneManager.GetActiveScene().name.Contains("Campaign", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCampaignPanelTitle(string text)
    {
        var plain = HtmlTagRegex.Replace(text, string.Empty).Trim();
        return CampaignPanelTitles.Contains(plain);
    }

    private static bool IsPanelTitleLayoutTarget(string text)
    {
        var plain = HtmlTagRegex.Replace(text, string.Empty).Trim();
        return (IsCampaignScene() && CampaignPanelTitles.Contains(plain)) ||
               (IsMainMenuScene() && MainMenuPanelTitles.Contains(plain));
    }

    private static bool IsMainMenuScene()
    {
        return SceneManager.GetActiveScene().name.Equals("0StartView", StringComparison.OrdinalIgnoreCase);
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

        // SetTextStatus translates the producer argument before the native UI
        // component receives it.  If the setter therefore sees the already
        // translated value, still apply the status-overlay font layout here.
        ApplyKnownStatusOverlayFontLayout(component, value);
        ApplyKnownCampaignPanelTitleLayout(component, value);

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
            else if (IsMixedLanguageText(value) && ContainsCjk(previous.Translated))
            {
                // A native writer can expose the control between two writes,
                // after another hook has already translated only part of the
                // string.  Never promote that mixed-language value to a new
                // source state: keep the last complete translation visible.
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
        var plainSource = HtmlTagRegex.Replace(source, string.Empty).Trim();
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

        translated = AppendMainMenuVersionCredit(translated);
        return !string.Equals(source, translated, StringComparison.Ordinal);
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
        if (!string.Equals(SceneManager.GetActiveScene().name, MainMenuSceneName, StringComparison.OrdinalIgnoreCase) ||
            value.Contains("InstantComet", StringComparison.OrdinalIgnoreCase))
            return value;

        var match = Regex.Match(
            value,
            @"(?<![A-Za-z0-9])V0\.6\.3(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        var plainSource = HtmlTagRegex.Replace(source, string.Empty).Trim();
        ApplyStatusOverlayFontLayout(component, source, translated);
        ApplyManualPauseFontLayout(component, plainSource);
        ApplyObjectiveFontLayout(component, plainSource);
        ApplyCampaignPanelTitleLayout(component, plainSource);
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
        ApplyCampaignPanelTitleLayout(component, HtmlTagRegex.Replace(value, string.Empty).Trim());
    }

    private static void ApplyStatusOverlayFontLayout(Component component, string source, string translated)
    {
        if (!IsStatusOverlayLabel(source) && !IsStatusOverlayLabel(translated))
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
        var plain = HtmlTagRegex.Replace(value, string.Empty).Trim();
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
        if (!IsPanelTitleLayoutTarget(plainSource))
            return;

        var instanceId = component.GetInstanceID();
        PanelTitleComponents[instanceId] = component;
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
        ReloadMappingsIfChanged();
        var repairEta = RepairEtaRegex.Match(value);
        if (repairEta.Success)
        {
            translated = $"维修将在 {repairEta.Groups[1].Value} 天后完成";
            return true;
        }
        if (TranslationCache.TryGetValue(value, out var cached))
        {
            translated = cached;
            return !string.Equals(value, cached, StringComparison.Ordinal);
        }

        var result = value;
        var changed = false;
        foreach (var mapping in Mappings)
        {
            if (value.IndexOf(mapping.FirstToken, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            result = ReplaceSegment(result, mapping, ref changed);
        }
        if (TranslationCache.Count >= 8192)
            TranslationCache.Clear();
        TranslationCache[value] = result;
        if (changed && ChineseCharacterRegex.IsMatch(result) && LongEnglishRunRegex.IsMatch(result) &&
            FragmentedTranslationsLogged.Add(value))
        {
            Logger?.LogWarning(
                $"Possible fragmented translation; add an exact full-string mapping. source={value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)} | result={result.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}");
        }
        translated = result;
        return changed;
    }

    private static void ReloadMappingsIfChanged(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && MappingsLoaded && now < NextMappingCheckUtc)
            return;
        NextMappingCheckUtc = now + MappingCheckInterval;

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
        if (MappingsLoaded && timestamp == MappingTimestampUtc)
            return;

        var next = new List<MappingEntry>();
        if (File.Exists(MappingPath))
        {
            try
            {
                foreach (var rawLine in File.ReadAllLines(MappingPath, new System.Text.UTF8Encoding(false)))
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
                        next.Add(new MappingEntry(original, translation));
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

        if (next.Count == 0)
        {
            next.Add(new MappingEntry(FirstOriginal, FirstTranslation));
            next.Add(new MappingEntry(FirstOriginal.Replace("  ", " ", StringComparison.Ordinal), FirstTranslation));
            next.Add(new MappingEntry(SecondOriginal, SecondTranslation));
        }

        next.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
        Mappings.Clear();
        Mappings.AddRange(next);
        TranslationCache.Clear();
        // Existing controls may still contain unchanged English text that was
        // cached before a newly added mapping existed. Revisit them once after
        // a mapping reload so hot-added translations appear without Alt+T.
        LastProcessedTexts.Clear();
        MappingTimestampUtc = timestamp;
        MappingsLoaded = true;
        Logger?.LogInfo($"Loaded {Mappings.Count} localization mappings from {MappingPath}.");
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
        public MappingEntry(string key, string value)
        {
            Key = key;
            Value = value;
            var words = Regex.Split(key.Trim(), @"\s+")
                .Where(word => word.Length > 0)
                .Select(Regex.Escape)
                .ToArray();
            if (words.Length == 0)
                throw new ArgumentException("Mapping key is empty.", nameof(key));

            FirstToken = Regex.Unescape(words[0]);
            var pattern = string.Join(@"\s+", words);
            var trimmedValue = value.Trim();
            var fragmentOnly = trimmedValue.StartsWith("……", StringComparison.Ordinal) ||
                               trimmedValue.EndsWith("……", StringComparison.Ordinal);
            if (ExactUiOnlyKeys.Contains(key.Trim()) || fragmentOnly)
                pattern = $@"^\s*{pattern}\s*$";
            else if (Regex.IsMatch(key.Trim(), @"^[A-Za-z0-9]+$"))
                pattern = $@"(?<![A-Za-z0-9]){pattern}(?![A-Za-z0-9])";
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!CaseSensitiveKeys.Contains(key.Trim()))
                options |= RegexOptions.IgnoreCase;
            Pattern = new Regex(pattern, options);
        }

        public string Key { get; }
        public string Value { get; }
        public string FirstToken { get; }
        public Regex Pattern { get; }
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
            PanelTitleComponents.Remove(instanceId);
        }

        if (StatusOverlayFontStates.TryGetValue(instanceId, out var statusFontState))
        {
            if (component is TMP_Text statusTmp && statusFontState.TmpFontSize.HasValue)
                statusTmp.fontSize = statusFontState.TmpFontSize.Value;
            else if (component is LegacyText statusLegacy && statusFontState.LegacyFontSize.HasValue)
                statusLegacy.fontSize = statusFontState.LegacyFontSize.Value;
            StatusOverlayFontStates.Remove(instanceId);
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
        public ImGuiStyleState(int fontSize, Vector2 contentOffset)
        {
            FontSize = fontSize;
            ContentOffset = contentOffset;
        }

        public int FontSize { get; }
        public Vector2 ContentOffset { get; }
    }

    private sealed class StatusOverlayTexts
    {
        public StatusOverlayTexts(List<TMP_Text> tmpTexts, List<LegacyText> legacyTexts)
        {
            TmpTexts = tmpTexts;
            LegacyTexts = legacyTexts;
        }

        public List<TMP_Text> TmpTexts { get; }
        public List<LegacyText> LegacyTexts { get; }
    }

    private sealed class RefreshComponent : MonoBehaviour
    {
        private Il2CppSystem.Collections.IEnumerator Start() => Loop().WrapToIl2Cpp();

        private IEnumerator Loop()
        {
            var firstPass = true;
            while (true)
            {
                yield return null;
                var toggled = HandleToggleHotkey();
                if (TranslationsEnabled && (IsCampaignScene() || IsMainMenuScene()))
                    EnforceCampaignPanelTitleLayouts();
                // A one-shot pass handles serialized scene text and explicit Alt+T
                // toggles. Dynamic text must be translated at its producer; periodic
                // polling would make direct-field writers alternate languages.
                if (!firstPass && !toggled && !SceneScanRequested)
                    continue;
                firstPass = false;
                SceneScanRequested = false;

                foreach (var text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var instanceId = text.GetInstanceID();
                    var current = text.text;
                    if (!TranslationsEnabled)
                    {
                        RestoreIfDisabled(instanceId, text);
                        continue;
                    }
                    if (LastProcessedTexts.TryGetValue(instanceId, out var last) &&
                        string.Equals(current, last, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    LastProcessedTexts[instanceId] = current;
                    if (!CandidateLogged && current.Contains("Celeres", StringComparison.OrdinalIgnoreCase))
                    {
                        CandidateLogged = true;
                        Logger?.LogInfo($"Found TMP text containing Celeres (length {current.Length}).");
                    }
                    ApplyKnownStatusOverlayFontLayout(text, current);
                    ApplyKnownCampaignPanelTitleLayout(text, current);
                    if (TryTranslateForDisplay(text, current, out var translated))
                    {
                        SetComponentText(text, current, translated);
                    }
                }
                foreach (var text in FindObjectsByType<LegacyText>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var instanceId = text.GetInstanceID();
                    var current = text.text;
                    if (!TranslationsEnabled)
                    {
                        RestoreIfDisabled(instanceId, text);
                        continue;
                    }
                    if (LastProcessedTexts.TryGetValue(instanceId, out var last) &&
                        string.Equals(current, last, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    LastProcessedTexts[instanceId] = current;
                    if (!CandidateLogged && current.Contains("SQUAD", StringComparison.OrdinalIgnoreCase))
                    {
                        CandidateLogged = true;
                        Logger?.LogInfo($"Found legacy text containing SQUAD (length {current.Length}).");
                    }
                    ApplyKnownStatusOverlayFontLayout(text, current);
                    ApplyKnownCampaignPanelTitleLayout(text, current);
                    if (TryTranslateForDisplay(text, current, out var translated))
                    {
                        SetComponentText(text, current, translated);
                    }
                }
                foreach (var text in FindObjectsByType<TextMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var instanceId = text.GetInstanceID();
                    var current = text.text;
                    if (!TranslationsEnabled)
                    {
                        RestoreIfDisabled(instanceId, text);
                        continue;
                    }
                    if (LastProcessedTexts.TryGetValue(instanceId, out var last) &&
                        string.Equals(current, last, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    LastProcessedTexts[instanceId] = current;
                    ApplyKnownCampaignPanelTitleLayout(text, current);
                    if (TryTranslateForDisplay(text, current, out var translated))
                        SetComponentText(text, current, translated);
                }
            }
        }

    }

    private static void EnforceCampaignPanelTitleLayouts()
    {
        foreach (var pair in PanelTitleComponents.ToArray())
        {
            var component = pair.Value;
            if (component == null || !PanelTitleLayoutStates.TryGetValue(pair.Key, out var state))
            {
                PanelTitleComponents.Remove(pair.Key);
                PanelTitleLayoutStates.Remove(pair.Key);
                continue;
            }

            if (component is TMP_Text tmp && state.TmpFontSize.HasValue)
            {
                var tmpRect = component.GetComponent<RectTransform>();
                if (tmpRect == null)
                    continue;
                tmp.enableAutoSizing = false;
                tmp.fontSize = Mathf.Max(10f, state.TmpFontSize.Value * CampaignPanelTitleFontScale);
                tmpRect.anchoredPosition = state.AnchoredPosition +
                                           Vector2.down * state.TmpFontSize.Value * CampaignPanelTitleDownShiftScale;
            }
            else if (component is LegacyText legacy && state.LegacyFontSize.HasValue)
            {
                var legacyRect = component.GetComponent<RectTransform>();
                if (legacyRect == null)
                    continue;
                legacy.resizeTextForBestFit = false;
                legacy.fontSize = Mathf.Max(10, Mathf.RoundToInt(state.LegacyFontSize.Value * CampaignPanelTitleFontScale));
                legacyRect.anchoredPosition = state.AnchoredPosition +
                                              Vector2.down * state.LegacyFontSize.Value * CampaignPanelTitleDownShiftScale;
            }
            else if (component is TextMesh textMesh && state.TextMeshCharacterSize.HasValue)
            {
                textMesh.characterSize = state.TextMeshCharacterSize.Value * CampaignPanelTitleFontScale;
                if (state.LocalPosition.HasValue)
                    textMesh.transform.localPosition = state.LocalPosition.Value +
                                                       Vector3.down * state.TextMeshCharacterSize.Value * CampaignPanelTitleDownShiftScale;
            }
        }
    }
}
