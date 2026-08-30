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

namespace Armaphract.HarmonyXUnitIntro;

[BepInPlugin(Guid, Name, Version)]
public sealed class HarmonyXUnitIntroPlugin : BasePlugin
{
    public const string Guid = "armaphract.harmonyx.unitintro";
    public const string Name = "Armaphract HarmonyX Localization";
    public const string Version = "1.8.13";

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
    private static readonly HashSet<string> ImGuiCandidatesLogged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ExactUiRoleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMMAND", "DRIVER", "GUNNER", "LOADER"
    };
    private static DateTime MappingTimestampUtc;
    private static bool MappingsLoaded;
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
        ReloadMappingsIfChanged();
        var harmony = new Harmony(Guid);
        harmony.Patch(
            AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text)),
            prefix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(TmpTextPrefix)));
        harmony.Patch(
            AccessTools.PropertySetter(typeof(LegacyText), nameof(LegacyText.text)),
            prefix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(LegacyTextPrefix)));
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

    private static void PatchTmpStringWriters(Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(TmpStringWriterPrefix));
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
            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(GameObjectSetActivePostfix)));
        Logger?.LogInfo("Patched GameObject.SetActive for event-driven panel translation.");
    }

    private static void PatchSceneLoaded(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(SceneManager), "Internal_SceneLoaded"),
            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(SceneLoadedPostfix)));
        Logger?.LogInfo("Patched SceneManager.Internal_SceneLoaded for one-shot scene translation.");
    }

    private static void SceneLoadedPostfix()
    {
        SceneScanRequested = true;
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
            _ => string.Empty
        };
        var instanceId = component.GetInstanceID();
        if (LastProcessedTexts.TryGetValue(instanceId, out var last) &&
            string.Equals(current, last, StringComparison.Ordinal))
            return;
        if (string.IsNullOrEmpty(current) || !TryTranslate(current, out var translated))
            return;
        translated = ApplyContextLayout(component, current, translated);
        SetComponentText(component, current, translated);
    }

    private static void PatchUnitStatusWriters(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(Unit), nameof(Unit.GetOtherStatusText)),
            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(UnitOtherStatusPostfix)));
        harmony.Patch(
            AccessTools.Method(typeof(Unit), nameof(Unit.GetCurrentStatus)),
            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(UnitCurrentStatusPostfix)));
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
        var prefix = new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(ScreenStatusTextPrefix));
        harmony.Patch(
            AccessTools.Method(typeof(UIManager), nameof(UIManager.SetScreenStatus), new[] { typeof(Unit) }),
            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(UnitScreenStatusPostfix)));
        harmony.Patch(
            AccessTools.Method(typeof(UIManager), nameof(UIManager.ManageScreenStatuses)),
            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(ManageScreenStatusesPostfix)));
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
                try
                {
                    if (type == typeof(GUIStyle) && hasContent)
                    {
                        harmony.Patch(
                            method,
                            prefix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(ImGuiStylePrefix)),
                            postfix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(ImGuiStylePostfix)));
                    }
                    else if (hasString)
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(ImGuiStringPrefix)));
                    else if (hasContent)
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(HarmonyXUnitIntroPlugin), nameof(ImGuiContentPrefix)));
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

    private static void ImGuiStylePrefix(GUIStyle __instance, GUIContent content, ref int __state)
    {
        __state = 0;
        if (__instance == null || content == null)
            return;

        var text = content.text;
        TranslateImGuiText(ref text);
        content.text = text;
        if (!IsManualGuideLabel(text) || !ActiveManualGuideStyles.Add(__instance))
            return;

        var originalFontSize = __instance.fontSize;
        if (originalFontSize <= 0)
        {
            ActiveManualGuideStyles.Remove(__instance);
            return;
        }

        __state = originalFontSize;
        __instance.fontSize = Mathf.Max(
            originalFontSize + 1,
            Mathf.RoundToInt(originalFontSize * ManualGuideImGuiFontScale));
    }

    private static void ImGuiStylePostfix(GUIStyle __instance, int __state)
    {
        if (__instance != null && __state > 0)
        {
            __instance.fontSize = __state;
            ActiveManualGuideStyles.Remove(__instance);
        }
    }

    private static bool IsManualGuideLabel(string text)
    {
        var plain = Regex.Replace(text, "<[^>]+>", string.Empty).Trim();
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
        if (TryTranslate(text, out var translated))
            text = translated;
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

        if (TryTranslate(source, out var translated))
        {
            translated = ApplyContextLayout(component, source, translated);
            AppliedStates[instanceId] = new TextState(source, translated);
            LastProcessedTexts[instanceId] = translated;
            value = translated;
        }
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
        }
        finally
        {
            InternalTextWrites.Remove(instanceId);
        }
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
        var plainSource = Regex.Replace(source, "<[^>]+>", string.Empty).Trim();
        ApplyStatusOverlayFontLayout(component, source, translated);
        ApplyManualPauseFontLayout(component, plainSource);
        ApplyObjectiveFontLayout(component, plainSource);
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
        var plain = Regex.Replace(value, "<[^>]+>", string.Empty).Trim();
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
        var normalizedSource = Regex.Replace(plainSource, @"\s*\[\d+\s*/\s*\d+\]\s*$", string.Empty).Trim();
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
        translated = result;
        return changed;
    }

    private static void ReloadMappingsIfChanged()
    {
        var timestamp = File.Exists(MappingPath) ? File.GetLastWriteTimeUtc(MappingPath) : DateTime.MinValue;
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
            if (ExactUiRoleKeys.Contains(key.Trim()))
                pattern = $@"^\s*{pattern}\s*$";
            else if (Regex.IsMatch(key.Trim(), @"^[A-Za-z0-9]+$"))
                pattern = $@"(?<![A-Za-z0-9]){pattern}(?![A-Za-z0-9])";
            Pattern = new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        public string Key { get; }
        public string Value { get; }
        public string FirstToken { get; }
        public Regex Pattern { get; }
    }

    private static string Normalize(string value)
    {
        while (value.Contains("  ", StringComparison.Ordinal))
            value = value.Replace("  ", " ", StringComparison.Ordinal);
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
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
                    if (TryTranslate(current, out var translated))
                    {
                        translated = ApplyContextLayout(text, current, translated);
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
                    if (TryTranslate(current, out var translated))
                    {
                        translated = ApplyContextLayout(text, current, translated);
                        SetComponentText(text, current, translated);
                    }
                }
            }
        }

    }
}
