#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 读写项目级 <see cref="LarkProjectConfig"/> 资产。
/// </summary>
public static class LarkProjectConfigStore {
    public const string ConfigAssetPath = "Assets/MgDataKit/Project/LarkProjectConfig.asset";

    public static string GetConfigPath() {
        return ConfigAssetPath;
    }

    public static bool Exists() {
        return GetConfigAssetPaths().Count > 0;
    }

    public static LarkProjectConfig GetOrNull() {
        return TryLoadAsset(out LarkProjectConfig asset, out _) ? asset : null;
    }

    public static bool TryLoad(out LarkProjectConfigData config, out string errorMessage) {
        config = null;
        errorMessage = null;

        if (!TryLoadAsset(out var asset, out errorMessage)) {
            if (string.IsNullOrEmpty(errorMessage))
                errorMessage = BuildMissingConfigMessage();
            return false;
        }

        config = new LarkProjectConfigData {
            appId = asset.appId,
            appSecret = asset.appSecret,
            brand = asset.brand,
            wikiHost = asset.WikiHost,
            wikiSpaceId = asset.WikiSpaceId,
            wikiParentNodeToken = asset.WikiParentNodeToken,
            playBeforeFeishuSyncEnabled = asset.playBeforeFeishuSyncEnabled
        };
        return config.IsValid(out errorMessage);
    }

    public static bool TrySave(LarkProjectConfigData config, out string errorMessage) {
        errorMessage = null;
        if (config == null || !config.IsValid(out errorMessage))
            return false;

        if (!TryLoadAsset(out var asset, out errorMessage)) {
            if (!string.IsNullOrEmpty(errorMessage))
                return false;

            if (AssetDatabase.LoadMainAssetAtPath(ConfigAssetPath) != null) {
                errorMessage = $"目标位置已有其他资产：{ConfigAssetPath}";
                return false;
            }

            asset = ScriptableObject.CreateInstance<LarkProjectConfig>();
            EnsureAssetFolder("Assets/MgDataKit/Project");
            AssetDatabase.CreateAsset(asset, ConfigAssetPath);
        }

        asset.appId = config.appId;
        asset.appSecret = config.appSecret;
        asset.brand = config.brand;
        asset.wikiHost = config.WikiHost;
        asset.wikiSpaceId = config.WikiSpaceId;
        asset.wikiParentNodeToken = config.WikiParentNodeToken;
        asset.playBeforeFeishuSyncEnabled = config.playBeforeFeishuSyncEnabled;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        InvalidateCache();

        return true;
    }

    private static bool TryLoadAsset(out LarkProjectConfig asset, out string errorMessage) {
        asset = null;
        errorMessage = null;
        List<string> paths = GetConfigAssetPaths();
        if (paths.Count == 0)
            return false;

        if (paths.Count > 1) {
            errorMessage = "检测到多个 LarkProjectConfig，项目中只能保留一个：\n" +
                           string.Join("\n", paths);
            return false;
        }

        asset = AssetDatabase.LoadAssetAtPath<LarkProjectConfig>(paths[0]);
        if (asset != null)
            return true;

        errorMessage = $"无法加载 LarkProjectConfig：{paths[0]}";
        return false;
    }

    private static string BuildMissingConfigMessage() {
        return $"未找到 LarkProjectConfig：{ConfigAssetPath}\n" +
               "请通过「MgDataKit → 飞书 Lark CLI → 编辑项目飞书应用配置」创建。";
    }

    private static List<string> GetConfigAssetPaths() {
        var paths = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:LarkProjectConfig");
        for (var i = 0; i < guids.Length; i++) {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    private static void EnsureAssetFolder(string folderPath) {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (var i = 1; i < parts.Length; i++) {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void InvalidateCache() {
        AssetDatabase.Refresh();
    }
}
#endif
