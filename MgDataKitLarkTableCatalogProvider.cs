#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    [InitializeOnLoad]
    internal static class MgDataKitLarkTableCatalogProvider {
        public const string AssetPath = "Assets/MgDataKit/Project/MgDataKitLarkTableCatalog.asset";

        private static MgDataKitLarkTableCatalog _cachedInstance;
        private static List<string> _cachedPaths;

        static MgDataKitLarkTableCatalogProvider() {
            EditorApplication.projectChanged += InvalidateCache;
        }

        public static MgDataKitLarkTableCatalog GetOrNull() {
            return TryGet(out MgDataKitLarkTableCatalog catalog, out _) ? catalog : null;
        }

        public static bool TryGet(out MgDataKitLarkTableCatalog catalog, out string errorMessage) {
            RefreshCacheIfNeeded();
            catalog = null;
            errorMessage = null;

            if (_cachedPaths.Count == 0) {
                if (TryRecoverCanonicalAsset(out catalog, out bool foundBrokenCatalog, out string recoveryError))
                    return true;

                if (foundBrokenCatalog) {
                    errorMessage = recoveryError;
                    return false;
                }

                if (AssetDatabase.LoadMainAssetAtPath(AssetPath) != null) {
                    errorMessage =
                        $"目录路径被其他资产占用，且不能作为 MgDataKitLarkTableCatalog 恢复：{AssetPath}";
                    return false;
                }

                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) {
                    errorMessage = "Unity 脚本尚未成功编译，暂时无法确认或创建飞书表目录。";
                    return false;
                }

                errorMessage = "未找到 MgDataKitLarkTableCatalog。";
                return false;
            }

            if (_cachedPaths.Count > 1) {
                errorMessage = "检测到多个 MgDataKitLarkTableCatalog，项目中只能保留一个：\n" +
                               string.Join("\n", _cachedPaths);
                return false;
            }

            if (_cachedInstance == null)
                _cachedInstance = AssetDatabase.LoadAssetAtPath<MgDataKitLarkTableCatalog>(_cachedPaths[0]);

            if (_cachedInstance != null) {
                catalog = _cachedInstance;
                return true;
            }

            errorMessage = $"无法加载 MgDataKitLarkTableCatalog：{_cachedPaths[0]}";
            return false;
        }

        public static IReadOnlyList<string> GetAllAssetPaths() {
            RefreshCacheIfNeeded();
            return _cachedPaths;
        }

        public static bool CanCreate(out string reason) {
            reason = null;
            RefreshCacheIfNeeded();
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) {
                reason = "Unity 脚本尚未成功编译，暂时不能创建飞书表目录。";
                return false;
            }

            if (_cachedPaths.Count > 0) {
                reason = "项目中已存在 MgDataKitLarkTableCatalog。";
                return false;
            }

            if (File.Exists(AssetPath) || AssetDatabase.LoadMainAssetAtPath(AssetPath) != null) {
                reason = $"目标位置已有资产：{AssetPath}";
                return false;
            }

            return true;
        }

        public static bool TryCreate(out MgDataKitLarkTableCatalog catalog, out string errorMessage) {
            catalog = null;
            errorMessage = null;
            if (TryGet(out catalog, out errorMessage))
                return true;

            if (!CanCreate(out string createError)) {
                if (string.IsNullOrWhiteSpace(errorMessage) &&
                    !string.IsNullOrWhiteSpace(createError))
                    errorMessage = createError;
                return false;
            }

            EnsureAssetFolder(Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<MgDataKitLarkTableCatalog>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
            AssetDatabase.SaveAssetIfDirty(catalog);
            InvalidateCache();
            TryGet(out catalog, out _);
            return catalog != null;
        }

        public static void InvalidateCache() {
            _cachedInstance = null;
            _cachedPaths = null;
        }

        private static void RefreshCacheIfNeeded() {
            if (_cachedPaths != null)
                return;

            _cachedPaths = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:MgDataKitLarkTableCatalog");
            for (var i = 0; i < guids.Length; i++) {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrWhiteSpace(path))
                    _cachedPaths.Add(path);
            }

            _cachedPaths.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryRecoverCanonicalAsset(
            out MgDataKitLarkTableCatalog catalog,
            out bool foundBrokenCatalog,
            out string errorMessage) {
            catalog = AssetDatabase.LoadAssetAtPath<MgDataKitLarkTableCatalog>(AssetPath);
            foundBrokenCatalog = false;
            errorMessage = null;
            if (catalog != null) {
                _cachedInstance = catalog;
                return true;
            }

            if (!File.Exists(AssetPath))
                return false;

            string yaml;
            try {
                yaml = File.ReadAllText(AssetPath);
            } catch (Exception ex) {
                foundBrokenCatalog = true;
                errorMessage = $"无法读取飞书表目录资产：{ex.Message}";
                return false;
            }

            const string catalogAssetName = "  m_Name: MgDataKitLarkTableCatalog";
            if (!yaml.Contains(catalogAssetName, StringComparison.Ordinal) ||
                !yaml.Contains("  _nodes:", StringComparison.Ordinal))
                return false;

            foundBrokenCatalog = true;
            if (!TryGetCatalogScriptGuid(out string scriptGuid, out errorMessage))
                return false;

            const string scriptPrefix = "  m_Script:";
            int scriptReferenceIndex = yaml.IndexOf(scriptPrefix, StringComparison.Ordinal);
            if (scriptReferenceIndex < 0) {
                errorMessage = "飞书表目录资产缺少 m_Script 引用，无法恢复。";
                return false;
            }

            int scriptLineEnd = yaml.IndexOf('\n', scriptReferenceIndex);
            if (scriptLineEnd < 0)
                scriptLineEnd = yaml.Length;
            string currentScriptReference = yaml.Substring(
                scriptReferenceIndex,
                scriptLineEnd - scriptReferenceIndex);
            if (!TryReadScriptGuid(currentScriptReference, out string currentScriptGuid)) {
                errorMessage = "飞书表目录资产的 m_Script 引用格式无效，无法恢复。";
                return false;
            }

            if (string.Equals(currentScriptGuid, scriptGuid, StringComparison.OrdinalIgnoreCase)) {
                errorMessage = EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed
                    ? "飞书表目录资产的脚本引用正确，但 Unity 脚本尚未成功编译。请先处理 Console 编译错误。"
                    : "飞书表目录资产的脚本引用正确，但 Unity 暂时无法加载该类型。请重新导入脚本或检查 Console。";
                return false;
            }

            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) {
                errorMessage =
                    "检测到飞书表目录资产的脚本引用需要恢复，但 Unity 脚本尚未成功编译。" +
                    "请先处理 Console 编译错误。";
                return false;
            }

            string scriptReference =
                $"  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}";
            string repairedYaml = yaml.Substring(0, scriptReferenceIndex) +
                                  scriptReference +
                                  yaml.Substring(scriptLineEnd);
            try {
                // A missing MonoScript cannot be repaired through SerializedObject.
                // The constrained YAML update preserves the existing catalog snapshot.
                File.WriteAllText(AssetPath, repairedYaml, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            } catch (Exception ex) {
                errorMessage = $"修复飞书表目录资产失败：{ex.Message}";
                return false;
            }

            InvalidateCache();
            catalog = AssetDatabase.LoadAssetAtPath<MgDataKitLarkTableCatalog>(AssetPath);
            if (catalog == null) {
                errorMessage = "飞书表目录资产已修复脚本引用，但 Unity 未能重新加载该资产。";
                return false;
            }

            _cachedInstance = catalog;
            Debug.Log($"[MgDataKit] 已恢复飞书表目录资产：{AssetPath}");
            return true;
        }

        private static bool TryReadScriptGuid(string scriptReference, out string scriptGuid) {
            scriptGuid = null;
            if (string.IsNullOrWhiteSpace(scriptReference))
                return false;

            const string guidPrefix = "guid:";
            int guidIndex = scriptReference.IndexOf(guidPrefix, StringComparison.Ordinal);
            if (guidIndex < 0)
                return false;

            int valueStart = guidIndex + guidPrefix.Length;
            while (valueStart < scriptReference.Length && char.IsWhiteSpace(scriptReference[valueStart]))
                valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < scriptReference.Length &&
                   scriptReference[valueEnd] != ',' &&
                   scriptReference[valueEnd] != '}')
                valueEnd++;

            if (valueEnd <= valueStart)
                return false;

            scriptGuid = scriptReference.Substring(valueStart, valueEnd - valueStart).Trim();
            return !string.IsNullOrWhiteSpace(scriptGuid);
        }

        private static bool TryGetCatalogScriptGuid(out string scriptGuid, out string errorMessage) {
            scriptGuid = null;
            errorMessage = null;
            string[] guids = AssetDatabase.FindAssets($"{nameof(MgDataKitLarkTableCatalog)} t:MonoScript");
            for (var i = 0; i < guids.Length; i++) {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (script == null || script.GetClass() != typeof(MgDataKitLarkTableCatalog))
                    continue;

                scriptGuid = guids[i];
                return true;
            }

            errorMessage = "未找到 MgDataKitLarkTableCatalog 的脚本，无法恢复目录资产。";
            return false;
        }

        private static void EnsureAssetFolder(string folderPath) {
            if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
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
    }
}
#endif
