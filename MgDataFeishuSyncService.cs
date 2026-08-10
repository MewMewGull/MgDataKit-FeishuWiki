#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MgDataKit;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MgDataKit.Editor {
    internal static class MgDataFeishuSyncService {
        public static bool GetPlayBeforeFeishuSyncEnabled() {
            return FeishuUserPreferencesStore.GetPlayBeforeFeishuSyncEnabled();
        }

        public static bool TryPreflightUserAuth(out string errorMessage) {
            errorMessage = null;
            if (!LarkCliPathResolver.TryResolve(out _, out var resolveError)) {
                errorMessage = resolveError;
                return false;
            }

            if (!LarkCliProcessRunner.TryRun("auth status --json", out var result) || result == null) {
                errorMessage = "无法启动 Lark CLI。";
                return false;
            }
            if (!result.Success) {
                errorMessage = LarkCliAuthHelper.FormatFailureMessage(result, "查询登录状态");
                return false;
            }

            if (!MgDataLarkCliOutputParser.TryResolveOutputText(result, out var jsonText, out errorMessage))
                return false;

            if (MgDataLarkCliOutputParser.TryParseUserAuthReady(jsonText, out errorMessage))
                return true;

            if (MgDataLarkCliOutputParser.TryGetUserAuthStatus(jsonText, out var status) &&
                string.Equals(status, "needs_refresh", StringComparison.OrdinalIgnoreCase)) {
                // `needs_refresh` is provisional: the following source-specific user API call
                // refreshes the token without requiring an unrelated Wiki scope.
                errorMessage = null;
                return true;
            }

            errorMessage =
                $"{errorMessage}\n\n请执行 MgDataKit → 飞书 Lark CLI → " +
                "应用项目配置到本机 / 登录飞书账号。";
            return false;
        }

        public static bool TrySyncAllWithPreflight(out string errorMessage) {
            errorMessage = null;
            List<MgDataBase> assets = FindAllFeishuAssets();
            if (assets.Count == 0)
                return TrySyncFeishuAssetList(assets, out errorMessage);

            if (!TryPreflightUserAuth(out errorMessage))
                return false;
            return TrySyncFeishuAssetList(assets, out errorMessage);
        }

        public static bool TrySyncFeishuAssets(IEnumerable<MgDataBase> assets, out string errorMessage) {
            var selectedAssets = new List<MgDataBase>();
            if (assets != null) {
                foreach (MgDataBase asset in assets) {
                    if (asset != null && string.Equals(MgDataKitAssetCatalogProvider.GetSourceId(asset.GetType()), "feishu", StringComparison.OrdinalIgnoreCase) &&
                        MgDataKitAssetCatalogProvider.TryGetEntry(asset, out MgDataKitAssetEntry entry) &&
                        !string.IsNullOrWhiteSpace(FeishuBindingUtility.Read(entry)?.source) &&
                        !selectedAssets.Contains(asset))
                        selectedAssets.Add(asset);
                }
            }

            return TrySyncFeishuAssetList(selectedAssets, out errorMessage);
        }

        private static bool TrySyncFeishuAssetList(IReadOnlyList<MgDataBase> assets, out string errorMessage) {
            var totalStopwatch = Stopwatch.StartNew();
            errorMessage = null;
            if (assets.Count == 0) {
                Debug.Log("[MgDataKit] 无飞书表需要同步。");
                return true;
            }

            // 按宿主注册的同步优先级排序，然后使用资产名称保持结果稳定。
            var orderedAssets = new List<MgDataBase>(assets);
            orderedAssets.Sort(CompareFeishuSyncOrder);

            var failures = new List<string>();
            var synced = 0;
            for (var i = 0; i < orderedAssets.Count; i++) {
                MgDataBase asset = orderedAssets[i];
                if (TrySyncAssetCore(asset, out var assetError))
                    synced++;
                else
                    failures.Add($"{asset.GetType().Name}/{asset.name}: {assetError}");
            }

            var lintStopwatch = Stopwatch.StartNew();
            // 飞书导入没有 Excel 行号引用，Lint 时不再读取本地 Excel。
            var lintSucceeded = MgDataScriptValidationGate.ValidateAllImportedTables(true, false);
            lintStopwatch.Stop();
            totalStopwatch.Stop();
            Debug.Log(
                $"[MgDataKit][Timing] FeishuSyncBatch Synced={synced}, Total={orderedAssets.Count}, " +
                $"LintMs={lintStopwatch.ElapsedMilliseconds}, TotalMs={totalStopwatch.ElapsedMilliseconds}");

            if (!lintSucceeded)
                failures.Add("MgDataKit Lint 未通过。");
            if (failures.Count == 0) {
                Debug.Log($"[MgDataKit] 飞书同步完成: 同步={synced}, 总数={orderedAssets.Count}");
                return true;
            }

            errorMessage = string.Join("\n", failures);
            return false;
        }

        private static int CompareFeishuSyncOrder(MgDataBase left, MgDataBase right) {
            var leftPriority = GetFeishuSyncPriority(left);
            var rightPriority = GetFeishuSyncPriority(right);
            if (leftPriority != rightPriority)
                return leftPriority.CompareTo(rightPriority);

            return string.Compare(
                left?.name,
                right?.name,
                StringComparison.Ordinal);
        }

        private static int GetFeishuSyncPriority(MgDataBase asset) {
            return MgDataKitExtensionRegistry.TryGetSyncPriority(asset, out int priority)
                ? priority
                : 3;
        }

        public static bool TrySyncAsset(MgDataBase asset, out string errorMessage) {
            var totalStopwatch = Stopwatch.StartNew();
            if (!TrySyncAssetCore(asset, out errorMessage))
                return false;

            var lintStopwatch = Stopwatch.StartNew();
            // 飞书导入没有 Excel 行号引用，Lint 时不再读取本地 Excel。
            var lintSucceeded = MgDataScriptValidationGate.ValidateAllImportedTables(true, false);
            lintStopwatch.Stop();
            totalStopwatch.Stop();
            Debug.Log(
                $"[MgDataKit][Timing] FeishuSyncLint Table={asset.GetType().Name}, " +
                $"LintMs={lintStopwatch.ElapsedMilliseconds}, TotalMs={totalStopwatch.ElapsedMilliseconds}");
            if (lintSucceeded)
                return true;

            errorMessage = "MgDataKit Lint 未通过。";
            return false;
        }

        public static List<MgDataBase> FindAllFeishuAssets() {
            var result = new List<MgDataBase>();
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(out var catalog, out _))
                return result;

            for (var typeIndex = 0; typeIndex < catalog.Entries.Count; typeIndex++) {
                MgDataKitAssetTypeEntry typeEntry = catalog.Entries[typeIndex];
                if (typeEntry == null)
                    continue;

                for (var assetIndex = 0; assetIndex < typeEntry.Assets.Count; assetIndex++) {
                    MgDataKitAssetEntry entry = typeEntry.Assets[assetIndex];
                    MgDataBase asset = entry?.Asset;
                    if (asset != null && string.Equals(MgDataKitAssetCatalogProvider.GetSourceId(asset.GetType()), "feishu", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(FeishuBindingUtility.Read(entry)?.source))
                        result.Add(asset);
                }
            }

            return result;
        }

        private static bool TrySyncAssetCore(MgDataBase asset, out string errorMessage) {
            errorMessage = null;
            if (asset == null) {
                errorMessage = "Asset 为空。";
                return false;
            }

            if (!string.Equals(MgDataKitAssetCatalogProvider.GetSourceId(asset.GetType()), "feishu", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!MgDataKitAssetCatalogProvider.TryGetEntry(asset, out MgDataKitAssetEntry entry) ||
                string.IsNullOrWhiteSpace(FeishuBindingUtility.Read(entry)?.source)) {
                errorMessage = "未绑定飞书来源。";
                return false;
            }

            var totalStopwatch = Stopwatch.StartNew();
            try {
                var importStopwatch = Stopwatch.StartNew();
                if (!MgDataImportService.Import(asset))
                    throw new InvalidOperationException("飞书数据导入失败，详细信息请查看 Console。");
                importStopwatch.Stop();

                var saveStopwatch = Stopwatch.StartNew();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                saveStopwatch.Stop();
                totalStopwatch.Stop();
                Debug.Log(
                    $"[MgDataKit][Timing] FeishuSync Table={asset.GetType().Name}, Asset={asset.name}, " +
                    $"ImportAndPostProcessMs={importStopwatch.ElapsedMilliseconds}, " +
                    $"SaveMs={saveStopwatch.ElapsedMilliseconds}, TotalMs={totalStopwatch.ElapsedMilliseconds}");
                return true;
            } catch (Exception ex) {
                totalStopwatch.Stop();
                errorMessage = ex.Message;
                Debug.LogError(
                    $"[MgDataKit] 飞书同步失败: {asset.GetType().Name}/{asset.name}, " +
                    $"ElapsedMs={totalStopwatch.ElapsedMilliseconds}\n{ex}");
                return false;
            }
        }
    }

}
#endif
