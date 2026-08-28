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
using LegacyText = UnityEngine.UI.Text;

namespace Armaphract.HarmonyXUnitIntro;

[BepInPlugin(Guid, Name, Version)]
public sealed class HarmonyXUnitIntroPlugin : BasePlugin
{
    public const string Guid = "armaphract.harmonyx.unitintro";
    public const string Name = "Armaphract HarmonyX Unit Intro";
    public const string Version = "1.2.0";

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
    private static readonly Dictionary<int, string> LastProcessedTexts = new();
    private static readonly Dictionary<string, string> TranslationCache = new(StringComparer.Ordinal);
    private static DateTime MappingTimestampUtc;
    private static bool MappingsLoaded;
    private static bool TranslationsEnabled = true;

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
        AddComponent<RefreshComponent>();
        Log.LogInfo("HarmonyX unit introduction patch loaded for TMP_Text and UnityEngine.UI.Text; Alt+T toggle enabled.");
    }

    private static void TmpTextPrefix(TMP_Text __instance, ref string value)
    {
        TextPrefix(__instance.GetInstanceID(), ref value);
    }

    private static void LegacyTextPrefix(LegacyText __instance, ref string value)
    {
        TextPrefix(__instance.GetInstanceID(), ref value);
    }

    private static void TextPrefix(int instanceId, ref string value)
    {
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

        var source = value;
        if (AppliedStates.TryGetValue(instanceId, out var previous) &&
            string.Equals(value, previous.Translated, StringComparison.Ordinal))
        {
            source = previous.Original;
        }

        if (TryTranslate(source, out var translated))
        {
            AppliedStates[instanceId] = new TextState(source, translated);
            LastProcessedTexts[instanceId] = translated;
            value = translated;
        }
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
                Logger?.LogWarning($"Could not read external unit-intro mappings: {ex.Message}");
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
        Logger?.LogInfo($"Loaded {Mappings.Count} unit-intro mappings from {MappingPath}.");
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
            Pattern = new Regex(
                string.Join(@"\s+", words),
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

    private static void HandleToggleHotkey()
    {
        var altDown = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (!altDown || !Input.GetKeyDown(KeyCode.T))
            return;

        TranslationsEnabled = !TranslationsEnabled;
        LastProcessedTexts.Clear();
        Logger?.LogInfo($"HarmonyX unit-intro translations {(TranslationsEnabled ? "enabled" : "disabled")} by Alt+T.");
    }

    private static void RestoreIfDisabled(int instanceId, Component component)
    {
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

    private sealed class RefreshComponent : MonoBehaviour
    {
        private int frameCounter;

        private Il2CppSystem.Collections.IEnumerator Start() => Loop().WrapToIl2Cpp();

        private IEnumerator Loop()
        {
            while (true)
            {
                yield return null;
                HandleToggleHotkey();
                if (++frameCounter % 60 != 0)
                    continue;
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
                    if (TryTranslate(current, out var translated))
                    {
                        AppliedStates[instanceId] = new TextState(current, translated);
                        LastProcessedTexts[instanceId] = translated;
                        text.text = translated;
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
                    if (TryTranslate(current, out var translated))
                    {
                        AppliedStates[instanceId] = new TextState(current, translated);
                        LastProcessedTexts[instanceId] = translated;
                        text.text = translated;
                    }
                }
            }
        }
    }
}
