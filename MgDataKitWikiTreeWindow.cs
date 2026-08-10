#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MgDataKit.Editor {
    public sealed class MgDataKitWikiTreeWindow : EditorWindow {
        private Vector2 _scrollPosition;

        [MenuItem(FeishuEditorMenu.WikiTree, false, 201)]
        public static void OpenWindow() {
            var window = GetWindow<MgDataKitWikiTreeWindow>(false, "MgDataKit Wiki Tree");
            window.minSize = new Vector2(640f, 480f);
            window.Show();
            window.Focus();
        }

        private void OnProjectChange() {
            Repaint();
        }

        private void OnGUI() {
            using (EditorGUILayout.ScrollViewScope scroll = new(_scrollPosition, GUILayout.ExpandHeight(true))) {
                _scrollPosition = scroll.scrollPosition;
                EditorGUILayout.LabelField("MgDataKit Wiki Tree", EditorStyles.boldLabel);
                LarkProjectConfig settings = DrawWikiSource();
                DrawCatalog(settings);
            }
        }

        private static LarkProjectConfig DrawWikiSource() {
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Wiki Source", EditorStyles.boldLabel);
            LarkProjectConfig settings = LarkProjectConfigStore.GetOrNull();
            if (settings == null) {
                EditorGUILayout.HelpBox(
                    "未找到 LarkProjectConfig，请先打开“项目飞书应用配置”创建飞书模块配置。",
                    MessageType.Warning);
                return null;
            }

            EditorGUILayout.ObjectField("Feishu Config", settings, typeof(LarkProjectConfig), false);
            EditorGUILayout.LabelField("Wiki Host", settings.WikiHost);
            EditorGUILayout.LabelField("Wiki Space ID", settings.WikiSpaceId);
            EditorGUILayout.LabelField("根节点 Token", settings.WikiParentNodeToken);
            return settings;
        }

        private static void DrawCatalog(LarkProjectConfig settings) {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("飞书表目录", EditorStyles.boldLabel);

            if (!MgDataKitLarkTableCatalogProvider.TryGet(out var catalog, out var catalogError)) {
                EditorGUILayout.HelpBox(catalogError, MessageType.Warning);
                IReadOnlyList<string> paths = MgDataKitLarkTableCatalogProvider.GetAllAssetPaths();
                for (var i = 0; i < paths.Count; i++)
                    EditorGUILayout.LabelField(paths[i], EditorStyles.miniLabel);

                if (paths.Count == 0 &&
                    MgDataKitLarkTableCatalogProvider.CanCreate(out _) &&
                    GUILayout.Button("创建飞书表目录", GUILayout.Height(22))) {
                    if (!MgDataKitLarkTableCatalogProvider.TryCreate(out _, out var createError))
                        EditorUtility.DisplayDialog("创建飞书表目录失败", createError, "确定");
                }

                return;
            }

            EditorGUILayout.ObjectField("Catalog Asset", catalog, typeof(MgDataKitLarkTableCatalog), false);
            EditorGUILayout.LabelField("目录路径", AssetDatabase.GetAssetPath(catalog));
            EditorGUILayout.LabelField("目录 Space ID", catalog.WikiSpaceId);
            EditorGUILayout.LabelField("目录根节点", catalog.RootNodeToken);
            EditorGUILayout.LabelField(
                "节点统计",
                $"全部 {catalog.Nodes.Count}，电子表格 {catalog.SheetCount}，Sheet 子节点 {catalog.WorkbookSheetCount}");

            if (catalog.SheetCount > 0 && catalog.WorkbookSheetCount == 0) {
                EditorGUILayout.HelpBox(
                    "当前目录是旧快照，尚未包含工作簿 Sheet 子节点。请从飞书刷新完整目录树。",
                    MessageType.Warning);
            }

            if (settings != null &&
                (!string.Equals(catalog.WikiSpaceId, settings.WikiSpaceId, StringComparison.Ordinal) ||
                 !string.Equals(catalog.RootNodeToken, settings.WikiParentNodeToken, StringComparison.Ordinal))) {
                EditorGUILayout.HelpBox(
                    "目录来源与当前 Wiki Source 不一致。",
                    MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(settings == null);
            if (GUILayout.Button("从飞书刷新完整目录树", GUILayout.Height(22)))
                RefreshCatalog(settings, catalog);
            EditorGUI.EndDisabledGroup();

            DrawTree(catalog);
        }

        private static void RefreshCatalog(
            LarkProjectConfig settings,
            MgDataKitLarkTableCatalog catalog) {
            IReadOnlyList<MgDataLarkWikiNodeInfo> nodes = null;
            string error = null;
            var stopwatch = Stopwatch.StartNew();
            try {
                EditorUtility.DisplayProgressBar("MgDataKit", "正在读取飞书目录树...", 0.1f);
                nodes = MgDataCloudWikiProvisioner.ListNodeTree(
                    settings.WikiSpaceId,
                    settings.WikiParentNodeToken,
                    (count, title) => EditorUtility.DisplayProgressBar(
                        "MgDataKit",
                        $"已获取 {count} 个节点：{title}",
                        0.5f));
            } catch (Exception ex) {
                error = ex.Message;
            } finally {
                EditorUtility.ClearProgressBar();
            }

            if (error != null) {
                Debug.LogError($"[MgDataKit] 飞书目录树刷新失败，已保留旧目录: {error}");
                EditorUtility.DisplayDialog("飞书目录刷新失败", error, "确定");
                return;
            }

            Undo.RecordObject(catalog, "刷新飞书表目录");
            catalog.Replace(settings.WikiSpaceId, settings.WikiParentNodeToken, nodes);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            stopwatch.Stop();

            Debug.Log(
                $"[MgDataKit] 飞书目录树刷新完成: 节点={catalog.Nodes.Count}, " +
                $"电子表格={catalog.SheetCount}, Sheet={catalog.WorkbookSheetCount}, " +
                $"耗时={stopwatch.Elapsed.TotalSeconds:F2}s");
            EditorUtility.DisplayDialog(
                "飞书目录刷新完成",
                $"共获取 {catalog.Nodes.Count} 个节点，其中电子表格 {catalog.SheetCount} 个，" +
                $"Sheet 子节点 {catalog.WorkbookSheetCount} 个。",
                "确定");
        }

        private static void DrawTree(MgDataKitLarkTableCatalog catalog) {
            GUILayout.Space(4f);
            if (catalog.Nodes.Count == 0) {
                EditorGUILayout.HelpBox("目录为空。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField("名称", EditorStyles.miniBoldLabel, GUILayout.MinWidth(260f));
                EditorGUILayout.LabelField("类型", EditorStyles.miniBoldLabel, GUILayout.Width(90f));
                EditorGUILayout.LabelField("Wiki Source", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
                EditorGUILayout.LabelField("Sheet ID", EditorStyles.miniBoldLabel, GUILayout.Width(180f));
            }

            for (var i = 0; i < catalog.Nodes.Count; i++) {
                MgDataKitLarkTableNode node = catalog.Nodes[i];
                if (node == null)
                    continue;

                var title = string.IsNullOrWhiteSpace(node.Title) ? node.NodeToken : node.Title;
                var treeTitle = new string(' ', Mathf.Max(0, node.Depth) * 4) +
                                (node.HasChildren ? "+ " : "- ") + title;
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(treeTitle, GUILayout.MinWidth(260f));
                    EditorGUILayout.LabelField(
                        node.IsWorkbookSheet ? "sheet 子节点" : node.ObjectType,
                        GUILayout.Width(90f));
                    EditorGUILayout.SelectableLabel(
                        node.Source,
                        EditorStyles.textField,
                        GUILayout.Width(220f),
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (node.IsWorkbookSheet)
                        EditorGUILayout.SelectableLabel(
                            node.SheetId,
                            EditorStyles.textField,
                            GUILayout.Width(180f),
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }
        }
    }
}
#endif
