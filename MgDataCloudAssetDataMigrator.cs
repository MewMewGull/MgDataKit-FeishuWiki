#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MgDataKit;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
/// <summary>
/// 将带有「资产数据」Tag 的 MgData 表迁移到飞书知识库 MgDataKit 云文档，并切换为 Feishu 数据源。
/// </summary>
public static class MgDataCloudAssetDataMigrator {
    public const string AssetDataTagName = "资产数据";

    public static IReadOnlyList<MigrationResult> MigrateAllAssetDataTables(bool showDialog = false) {
        var results = new List<MigrationResult>();
        var tableTypes = TypeCache.GetTypesDerivedFrom<MgDataBase>()
            .Where(t => !t.IsAbstract && MgDataKitAssetCatalogProvider.HasTag(t, AssetDataTagName))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        if (tableTypes.Count == 0) {
            Debug.LogWarning("[MgDataFeishuMigrator] 未找到带有「资产数据」Tag 的 MgData 表。");
            return results;
        }

        LarkProjectConfig settings = LarkProjectConfigStore.GetOrNull();
        if (settings == null) {
            Debug.LogError("[MgDataFeishuMigrator] 未找到 LarkProjectConfig。请先创建飞书模块配置。");
            return results;
        }

        Dictionary<string, WikiNodeInfo> existingByTitle;
        try {
            existingByTitle = MgDataCloudWikiProvisioner.ListChildSheets(
                settings.WikiSpaceId,
                settings.WikiParentNodeToken,
                settings.WikiHost);
        } catch (Exception ex) {
            var message = $"无法读取知识库子节点：{ex.Message}";
            Debug.LogError($"[MgDataFeishuMigrator] {message}");
            if (showDialog)
                EditorUtility.DisplayDialog("迁移失败", message, "确定");
            return results;
        }

        for (var i = 0; i < tableTypes.Count; i++) {
            Type tableType = tableTypes[i];
            try {
                results.AddRange(MigrateTypeAssets(tableType, existingByTitle, settings));
            } catch (Exception ex) {
                results.Add(MigrationResult.Failed(tableType.Name, ex.Message));
                Debug.LogError($"[MgDataFeishuMigrator] {tableType.Name} 迁移失败：{ex}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LogSummary(results);
        if (showDialog)
            EditorUtility.DisplayDialog("迁移完成", BuildSummaryText(results), "确定");

        return results;
    }

    static IReadOnlyList<MigrationResult> MigrateTypeAssets(
        Type tableType,
        Dictionary<string, WikiNodeInfo> existingByTitle,
        LarkProjectConfig settings) {
        List<MgDataBase> assets = FindAssets(tableType);
        if (assets.Count == 0)
            throw new InvalidOperationException($"未找到 {tableType.Name} 的 ScriptableObject Asset。");

        var results = new List<MigrationResult>();
        var bindings = new Dictionary<MgDataBase, MgDataFeishuBinding>();
        var hasFailure = false;
        try {
            for (var i = 0; i < assets.Count; i++) {
                MgDataBase asset = assets[i];
                string assetName = asset.name;
                EditorUtility.DisplayProgressBar(
                    "MgDataKit 云迁移",
                    assetName,
                    (float)i / assets.Count);

                try {
                    if (!MgDataKitAssetCatalogProvider.TryGetEntry(
                            asset,
                            out MgDataKitAssetEntry entry))
                        throw new InvalidOperationException($"{assetName} 未注册到 MgDataKit Asset Catalog。");

                    if (!string.IsNullOrWhiteSpace(FeishuBindingUtility.Read(entry)?.source)) {
                        bindings[asset] = CloneFeishuBinding(FeishuBindingUtility.Read(entry));
                        results.Add(MigrationResult.Succeeded(assetName, FeishuBindingUtility.Read(entry).source));
                        continue;
                    }

                    string excelPath = ResolveExcelPath(entry);
                    if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
                        throw new InvalidOperationException($"未找到本地 Excel：{excelPath ?? "(空)"}");

                    string sheetTitle = GetMigrationSheetTitle(tableType, asset, assets.Count);
                    WikiNodeInfo wikiNode;
                    if (existingByTitle.TryGetValue(sheetTitle, out var existing)) {
                        wikiNode = existing;
                        Debug.Log($"[MgDataFeishuMigrator] {assetName} 复用已有 wiki 节点：{existing.WikiUrl}");
                    } else {
                        wikiNode = MgDataCloudWikiProvisioner.ImportExcelAndMoveToWiki(
                            excelPath,
                            sheetTitle,
                            settings.WikiSpaceId,
                            settings.WikiParentNodeToken,
                            settings.WikiHost);
                        existingByTitle[sheetTitle] = wikiNode;
                        Debug.Log($"[MgDataFeishuMigrator] {assetName} 已上传并移入知识库：{wikiNode.WikiUrl}");
                    }

                    bindings[asset] = new MgDataFeishuBinding { source = wikiNode.WikiUrl };
                    results.Add(MigrationResult.Succeeded(assetName, wikiNode.WikiUrl));
                } catch (Exception ex) {
                    hasFailure = true;
                    results.Add(MigrationResult.Failed(assetName, ex.Message));
                    Debug.LogError($"[MgDataFeishuMigrator] {assetName} 迁移失败：{ex}");
                }
            }

            if (!hasFailure)
                ApplyFeishuBindings(tableType, assets, bindings);
            else
                Debug.LogWarning(
                    $"[MgDataFeishuMigrator] {tableType.Name} 存在迁移失败项，" +
                    "已保留该类型原来源配置，未切换 Asset 来源。");

            return results;
        } finally {
            EditorUtility.ClearProgressBar();
        }
    }

    static List<MgDataBase> FindAssets(Type tableType) {
        var result = new List<MgDataBase>();
        string[] guids = AssetDatabase.FindAssets($"t:{tableType.Name}");
        for (var i = 0; i < guids.Length; i++) {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            MgDataBase asset = AssetDatabase.LoadAssetAtPath(path, tableType) as MgDataBase;
            if (asset != null && asset.GetType() == tableType)
                result.Add(asset);
        }

        result.Sort((left, right) => string.Compare(
            AssetDatabase.GetAssetPath(left),
            AssetDatabase.GetAssetPath(right),
            StringComparison.Ordinal));
        return result;
    }

    static string GetMigrationSheetTitle(Type tableType, MgDataBase asset, int assetCount) {
        if (assetCount <= 1)
            return tableType.Name;

        return string.IsNullOrWhiteSpace(asset?.name) ? tableType.Name : asset.name;
    }

    static string ResolveExcelPath(MgDataKitAssetEntry entry) {
        if (entry == null || !string.Equals(entry.SourceId, "excel", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(entry.SourceData))
            return null;

        var path = entry.SourceData;
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(LarkCliPathResolver.GetProjectRootPath(), path));
    }

    static void ApplyFeishuBindings(
        Type tableType,
        IReadOnlyList<MgDataBase> assets,
        IReadOnlyDictionary<MgDataBase, MgDataFeishuBinding> bindings) {
        if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                out MgDataKitAssetCatalog catalog,
                out string catalogError))
            throw new InvalidOperationException(catalogError ?? "未找到 MgDataKit Asset Catalog。");

        MgDataKitAssetTypeEntry typeEntry = catalog.FindTypeEntry(tableType);
        if (typeEntry == null)
            throw new InvalidOperationException($"未找到 {tableType.Name} 的 Catalog 类型配置。");

        Undo.RecordObject(catalog, "Feishu 迁移类型配置");
        for (var i = 0; i < assets.Count; i++) {
            MgDataBase asset = assets[i];
            if (!bindings.TryGetValue(asset, out MgDataFeishuBinding binding))
                throw new InvalidOperationException($"{asset.name} 缺少迁移后的飞书绑定。");

            MgDataKitAssetEntry entry = catalog.FindEntry(asset) ?? catalog.AddEntry(asset);
            if (entry == null)
                throw new InvalidOperationException($"无法将 {asset.name} 注册到 Asset Catalog。");

            FeishuBindingUtility.Write(entry, CloneFeishuBinding(binding));
        }

        MgDataKitAssetCatalogProvider.Save(catalog);
    }

    static MgDataFeishuBinding CloneFeishuBinding(MgDataFeishuBinding binding) {
        return new MgDataFeishuBinding {
            source = binding?.source,
            sheetId = binding?.sheetId,
            sheetName = binding?.sheetName
        };
    }

    static void LogSummary(IReadOnlyList<MigrationResult> results) {
        Debug.Log(BuildSummaryText(results));
    }

    static string BuildSummaryText(IReadOnlyList<MigrationResult> results) {
        var builder = new StringBuilder();
        builder.AppendLine("[MgDataFeishuMigrator] 资产数据表迁移摘要");
        for (var i = 0; i < results.Count; i++) {
            MigrationResult result = results[i];
            builder.Append(result.Success ? "✓ " : "✗ ");
            builder.Append(result.TableName);
            if (!string.IsNullOrEmpty(result.WikiUrl))
                builder.Append(" → ").Append(result.WikiUrl);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                builder.Append(" （").Append(result.ErrorMessage).Append(')');
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public readonly struct MigrationResult {
        public readonly string TableName;
        public readonly string WikiUrl;
        public readonly string ErrorMessage;
        public bool Success => string.IsNullOrEmpty(ErrorMessage);

        MigrationResult(string tableName, string wikiUrl, string errorMessage) {
            TableName = tableName;
            WikiUrl = wikiUrl;
            ErrorMessage = errorMessage;
        }

        public static MigrationResult Succeeded(string tableName, string wikiUrl) {
            return new MigrationResult(tableName, wikiUrl, null);
        }

        public static MigrationResult Failed(string tableName, string errorMessage) {
            return new MigrationResult(tableName, null, errorMessage);
        }
    }

    public readonly struct WikiNodeInfo {
        public readonly string Title;
        public readonly string NodeToken;
        public readonly string WikiUrl;

        public WikiNodeInfo(string title, string nodeToken) {
            Title = title;
            NodeToken = nodeToken;
            WikiUrl = null;
        }

        public WikiNodeInfo(string title, string nodeToken, string wikiHost) {
            Title = title;
            NodeToken = nodeToken;
            var host = (wikiHost ?? string.Empty).TrimEnd('/') + "/";
            WikiUrl = string.IsNullOrWhiteSpace(nodeToken) ? null : host + nodeToken;
        }
    }
    }
}
#endif
