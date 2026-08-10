#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    [InitializeOnLoad]
    internal static class FeishuSourceValidationOnStartup {
        private const string SessionValidationKey = "MgDataKit.FeishuSourceValidation.Completed";

        static FeishuSourceValidationOnStartup() {
            EditorApplication.delayCall += ValidateOncePerUnitySession;
        }

        private static void ValidateOncePerUnitySession() {
            EditorApplication.delayCall -= ValidateOncePerUnitySession;
            if (SessionState.GetBool(SessionValidationKey, false))
                return;

            SessionState.SetBool(SessionValidationKey, true);
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(out MgDataKitAssetCatalog catalog, out string catalogError)) {
                Debug.LogWarning($"[MgDataKit] 飞书来源启动校验跳过：{catalogError}");
                return;
            }

            var adapter = new FeishuDataSourceAdapter();
            var invalidBindings = new List<string>();
            for (var typeIndex = 0; typeIndex < catalog.Entries.Count; typeIndex++) {
                MgDataKitAssetTypeEntry typeEntry = catalog.Entries[typeIndex];
                if (typeEntry == null ||
                    !string.Equals(typeEntry.SourceId, adapter.SourceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (var assetIndex = 0; assetIndex < typeEntry.Assets.Count; assetIndex++) {
                    MgDataKitAssetEntry entry = typeEntry.Assets[assetIndex];
                    if (entry?.Asset == null || adapter.TryValidate(entry, out string errorMessage))
                        continue;

                    invalidBindings.Add($"{typeEntry.AssetType?.Name}/{entry.Asset.name}: {errorMessage}");
                }
            }

            if (invalidBindings.Count > 0) {
                Debug.LogWarning(
                    "[MgDataKit] 飞书来源启动校验发现无效绑定：\n" +
                    string.Join("\n", invalidBindings));
            }
        }
    }
}

#endif
