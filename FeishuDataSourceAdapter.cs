#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using MgDataKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MgDataKit.Editor {
    [Serializable]
    internal sealed class MgDataFeishuBinding {
        public string source;
        public string sheetId;
        public string sheetName;
    }

    internal static class FeishuBindingUtility {
        public static MgDataFeishuBinding Read(MgDataKitAssetEntry entry) {
            if (entry == null || string.IsNullOrWhiteSpace(entry.SourceData))
                return new MgDataFeishuBinding();
            try {
                return JsonUtility.FromJson<MgDataFeishuBinding>(entry.SourceData) ?? new MgDataFeishuBinding();
            } catch {
                return new MgDataFeishuBinding();
            }
        }

        public static void Write(MgDataKitAssetEntry entry, MgDataFeishuBinding binding) {
            if (entry == null)
                return;
            entry.SourceId = "feishu";
            entry.SourceData = JsonUtility.ToJson(binding ?? new MgDataFeishuBinding());
        }
    }

    public sealed class FeishuDataSourceAdapter : IMgDataSourceAdapter, IMgDataSourceBatchImportAdapter {
        private const string AdapterSourceId = "feishu";

        public string SourceId => AdapterSourceId;
        public string DisplayName => "飞书";

        public bool CanHandle(MgDataKitAssetTypeEntry typeEntry) {
            return typeEntry != null && string.Equals(typeEntry.SourceId, SourceId, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryValidate(MgDataKitAssetEntry entry, out string errorMessage) {
            if (entry == null)
                return SetError(out errorMessage, "飞书 Catalog Entry 为空。");
            MgDataFeishuBinding binding = FeishuBindingUtility.Read(entry);
            if (string.IsNullOrWhiteSpace(binding?.source) && !string.IsNullOrWhiteSpace(entry.SourceData)) {
                try {
                    binding = JsonUtility.FromJson<MgDataFeishuBinding>(entry.SourceData);
                } catch (Exception) {
                    binding = FeishuBindingUtility.Read(entry);
                }
            }
            return TryValidateSource(binding?.source, out errorMessage);
        }

        public MgDataSourceReadResult Read(MgDataBase asset, MgDataKitAssetEntry entry) {
            return new FeishuDataSourceImporter().Read(asset, entry);
        }

        public bool TryInitializeBinding(MgDataKitAssetEntry entry, out string errorMessage) {
            errorMessage = null;
            return entry != null;
        }

        public void BuildBindingUI(MgDataSourceAdapterContext context, VisualElement container) {
            container.AddToClassList("mg-data-kit-source-row");
            DropdownField sourcePopup = new DropdownField("来源") {
                name = "mg-data-kit-feishu-popup"
            };
            sourcePopup.tooltip = "选择飞书 Wiki 中的电子表格来源";
            VisualElement sheetRow = new VisualElement { name = "mg-data-kit-feishu-sheet-row" };
            sheetRow.AddToClassList("mg-data-kit-feishu-sheet-row");
            DropdownField sheetPopup = new DropdownField("Sheet") {
                name = "mg-data-kit-feishu-sheet-popup"
            };
            sheetPopup.tooltip = "选择电子表格内的 Sheet";
            sheetRow.Add(sheetPopup);
            container.Add(sourcePopup);
            container.Add(sheetRow);

            sourcePopup.RegisterValueChangedCallback(_ => {
                MgDataFeishuBinding binding = ReadBinding(context.Entry);
                string source = ResolveSourceSelection(
                    binding.source,
                    sourcePopup.index,
                    sourcePopup.userData as SourcePopupState);
                ApplySourceBinding(context, binding, source);
            });
            sheetPopup.RegisterValueChangedCallback(_ => {
                if (!(sheetPopup.userData is SheetPopupState state) ||
                    sheetPopup.index < 0 || sheetPopup.index >= state.Nodes.Count)
                    return;

                MgDataKitLarkTableNode node = state.Nodes[sheetPopup.index];
                MgDataFeishuBinding binding = ReadBinding(context.Entry);
                ApplyBinding(context, binding.source, node.SheetId, node.SheetName);
            });
        }

        public void BindBindingUI(MgDataSourceAdapterContext context, VisualElement container) {
            DropdownField sourcePopup = container.Q<DropdownField>("mg-data-kit-feishu-popup");
            DropdownField sheetPopup = container.Q<DropdownField>("mg-data-kit-feishu-sheet-popup");
            if (sourcePopup == null || sheetPopup == null)
                return;

            MgDataFeishuBinding binding = ReadBinding(context.Entry);
            if (TryFindDefaultSheet(binding.source, out MgDataKitLarkTableNode defaultSheet) &&
                string.IsNullOrWhiteSpace(binding.sheetId) &&
                string.IsNullOrWhiteSpace(binding.sheetName)) {
                ApplyBinding(context, binding.source, defaultSheet.SheetId, defaultSheet.SheetName);
                binding = ReadBinding(context.Entry);
            }

            bool hasSourceOptions = TryBuildSourceOptions(
                    binding.source,
                    out IReadOnlyList<MgDataKitLarkTableNode> sourceNodes,
                    out string[] sourceNames,
                    out int sourceIndex,
                    out bool hasUnlistedSource);
            sourcePopup.style.display = DisplayStyle.Flex;
            if (!hasSourceOptions) {
                sourceNames = string.IsNullOrWhiteSpace(binding.source)
                    ? new[] { "(未绑定)" }
                    : new[] { "(未绑定)", $"(目录外) {binding.source}" };
                sourceNodes = Array.Empty<MgDataKitLarkTableNode>();
                sourceIndex = string.IsNullOrWhiteSpace(binding.source) ? 0 : 1;
                hasUnlistedSource = !string.IsNullOrWhiteSpace(binding.source);
            }

            sourcePopup.choices = new List<string>(sourceNames);
            sourcePopup.SetValueWithoutNotify(sourceNames[sourceIndex]);
            sourcePopup.userData = new SourcePopupState(
                sourceNodes,
                binding.source,
                hasUnlistedSource);
            sourcePopup.SetEnabled(hasSourceOptions || hasUnlistedSource);

            if (TryBuildSheetOptions(
                    binding.source,
                    binding.sheetId,
                    binding.sheetName,
                    out IReadOnlyList<MgDataKitLarkTableNode> sheetNodes,
                    out string[] sheetNames,
                    out int sheetIndex)) {
                GetSheetRow(sheetPopup).style.display = DisplayStyle.Flex;
                sheetPopup.choices = new List<string>(sheetNames);
                sheetPopup.SetValueWithoutNotify(sheetNames[sheetIndex]);
                sheetPopup.userData = new SheetPopupState(sheetNodes);
                sheetPopup.tooltip = "选择电子表格内的 Sheet";
                sheetPopup.SetEnabled(true);
            } else {
                string unavailableChoice = GetUnavailableSheetChoice(binding);
                GetSheetRow(sheetPopup).style.display = DisplayStyle.Flex;
                sheetPopup.choices = new List<string> { unavailableChoice };
                sheetPopup.SetValueWithoutNotify(unavailableChoice);
                sheetPopup.userData = null;
                sheetPopup.tooltip = GetUnavailableSheetTooltip(binding);
                sheetPopup.SetEnabled(false);
            }
        }

        public bool TryOpenSource(MgDataKitAssetEntry entry, out string errorMessage) {
            if (!TryValidate(entry, out errorMessage))
                return false;

            MgDataFeishuBinding binding = FeishuBindingUtility.Read(entry);
            if (string.IsNullOrWhiteSpace(binding?.source) && !string.IsNullOrWhiteSpace(entry.SourceData)) {
                try {
                    binding = JsonUtility.FromJson<MgDataFeishuBinding>(entry.SourceData);
                } catch (Exception) {
                    binding = FeishuBindingUtility.Read(entry);
                }
            }
            string source = binding?.source?.Trim();
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri sourceUri) &&
                (sourceUri.Scheme == Uri.UriSchemeHttp || sourceUri.Scheme == Uri.UriSchemeHttps)) {
                Application.OpenURL(source);
                return true;
            }

            LarkProjectConfig settings = LarkProjectConfigStore.GetOrNull();
            string host = (settings?.WikiHost ?? LarkProjectConfig.DefaultWikiHost)?.Trim();
            if (string.IsNullOrWhiteSpace(host))
                return SetError(out errorMessage, "无法构造飞书来源 URL，请先配置 Wiki Host。");

            int wikiIndex = host.IndexOf("/wiki", StringComparison.OrdinalIgnoreCase);
            string root = (wikiIndex >= 0 ? host.Substring(0, wikiIndex) : host).TrimEnd('/');
            string url = source.StartsWith("shtcn", StringComparison.OrdinalIgnoreCase)
                ? root + "/sheets/" + source
                : root + "/" + source.TrimStart('/');
            Application.OpenURL(url);
            return true;
        }

        public bool TryOpenBatchImport(
            Type assetType,
            string defaultOutputFolder,
            out string errorMessage) {
            errorMessage = null;
            if (assetType == null)
                return SetError(out errorMessage, "未选择有效的 MgData 类型。");

            MgDataKitBatchImportWindow.Open(assetType, defaultOutputFolder);
            return true;
        }

        private static bool SetError(out string errorMessage, string message) {
            errorMessage = message;
            return false;
        }

        private static bool TryValidateSource(string source, out string errorMessage) {
            errorMessage = null;
            string normalized = source?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
                return SetError(out errorMessage, "飞书来源不能为空。");

            if (normalized.StartsWith("wiki/", StringComparison.OrdinalIgnoreCase)) {
                string nodeToken = normalized.Substring("wiki/".Length);
                if (IsToken(nodeToken))
                    return true;
                return SetError(out errorMessage, "飞书 Wiki 来源格式应为 wiki/<node-token>。");
            }

            if (normalized.StartsWith("shtcn", StringComparison.OrdinalIgnoreCase) && IsToken(normalized))
                return true;

            if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
                IsFeishuHost(uri.Host) &&
                (uri.AbsolutePath.Contains("/wiki/", StringComparison.OrdinalIgnoreCase) ||
                 uri.AbsolutePath.Contains("/sheets/", StringComparison.OrdinalIgnoreCase)))
                return true;

            return SetError(
                out errorMessage,
                "飞书来源必须是 wiki/<node-token>、飞书 Wiki/Sheet URL，或 shtcn 开头的 spreadsheet token。");
        }

        private static bool IsToken(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            for (var i = 0; i < value.Length; i++) {
                char character = value[i];
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-')
                    return false;
            }

            return true;
        }

        private static bool IsFeishuHost(string host) {
            return string.Equals(host, "feishu.cn", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".feishu.cn", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "larksuite.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".larksuite.com", StringComparison.OrdinalIgnoreCase);
        }

        private static VisualElement GetSheetRow(DropdownField sheetPopup) {
            return sheetPopup?.parent ?? sheetPopup;
        }

        private static MgDataFeishuBinding ReadBinding(MgDataKitAssetEntry entry) {
            if (entry == null)
                return new MgDataFeishuBinding();

            MgDataFeishuBinding binding = FeishuBindingUtility.Read(entry);
            if (string.IsNullOrWhiteSpace(binding?.source) && !string.IsNullOrWhiteSpace(entry.SourceData)) {
                try {
                    binding = JsonUtility.FromJson<MgDataFeishuBinding>(entry.SourceData);
                } catch (Exception) {
                    binding = FeishuBindingUtility.Read(entry);
                }
            }

            return binding ?? new MgDataFeishuBinding();
        }

        private static void ApplyBinding(
            MgDataSourceAdapterContext context,
            string source,
            string sheetId,
            string sheetName) {
            if (context?.Entry?.Asset == null || context.Editor?.Catalog == null)
                return;

            Undo.RecordObject(context.Editor.Catalog, "绑定飞书来源");
            FeishuBindingUtility.Write(context.Entry, new MgDataFeishuBinding {
                source = source?.Trim() ?? string.Empty,
                sheetId = sheetId?.Trim() ?? string.Empty,
                sheetName = sheetName?.Trim() ?? string.Empty
            });
            MgDataKitAssetCatalogProvider.Save(context.Editor.Catalog);
            context.Commands?.RequestRefresh(EditorRefreshReason.CatalogChanged);
        }

        private static void ApplySourceBinding(
            MgDataSourceAdapterContext context,
            MgDataFeishuBinding existing,
            string source) {
            string normalizedSource = source?.Trim() ?? string.Empty;
            bool sameSource = string.Equals(
                existing?.source?.Trim(),
                normalizedSource,
                StringComparison.OrdinalIgnoreCase);
            string sheetId = sameSource ? existing?.sheetId : string.Empty;
            string sheetName = sameSource ? existing?.sheetName : string.Empty;
            if (!sameSource && TryFindDefaultSheet(normalizedSource, out MgDataKitLarkTableNode defaultSheet)) {
                sheetId = defaultSheet.SheetId;
                sheetName = defaultSheet.SheetName;
            }
            ApplyBinding(
                context,
                normalizedSource,
                sheetId,
                sheetName);
        }

        private static bool TryBuildSourceOptions(
            string currentSource,
            out IReadOnlyList<MgDataKitLarkTableNode> sheetNodes,
            out string[] optionNames,
            out int currentIndex,
            out bool hasUnlistedCurrentSource) {
            var nodes = new List<MgDataKitLarkTableNode>();
            var names = new List<string> { "(未绑定)" };
            currentIndex = 0;
            hasUnlistedCurrentSource = false;
            optionNames = null;
            MgDataKitLarkTableCatalog catalog = MgDataKitLarkTableCatalogProvider.GetOrNull();
            if (catalog == null || catalog.SheetCount == 0) {
                sheetNodes = nodes;
                return false;
            }

            var hierarchyTitles = new List<string>();
            for (var i = 0; i < catalog.Nodes.Count; i++) {
                MgDataKitLarkTableNode node = catalog.Nodes[i];
                if (node == null)
                    continue;

                int depth = Mathf.Max(0, node.Depth);
                while (hierarchyTitles.Count > depth)
                    hierarchyTitles.RemoveAt(hierarchyTitles.Count - 1);
                while (hierarchyTitles.Count < depth)
                    hierarchyTitles.Add("(未知层级)");
                hierarchyTitles.Add(string.IsNullOrWhiteSpace(node.Title) ? node.NodeToken : node.Title);
                if (!node.IsSheet)
                    continue;

                nodes.Add(node);
                names.Add(string.Join(" / ", hierarchyTitles));
            }

            for (var i = 0; i < nodes.Count; i++) {
                if (string.Equals(nodes[i].Source, currentSource, StringComparison.Ordinal)) {
                    currentIndex = i + 1;
                    break;
                }
            }

            hasUnlistedCurrentSource = currentIndex == 0 && !string.IsNullOrWhiteSpace(currentSource);
            if (hasUnlistedCurrentSource) {
                names.Insert(1, $"(目录外) {currentSource}");
                currentIndex = 1;
            }

            sheetNodes = nodes;
            optionNames = names.ToArray();
            return true;
        }

        private static bool TryBuildSheetOptions(
            string currentSource,
            string currentSheetId,
            string currentSheetName,
            out IReadOnlyList<MgDataKitLarkTableNode> sheetNodes,
            out string[] optionNames,
            out int currentIndex) {
            var nodes = new List<MgDataKitLarkTableNode>();
            var names = new List<string>();
            currentIndex = 0;
            optionNames = null;
            MgDataKitLarkTableCatalog catalog = MgDataKitLarkTableCatalogProvider.GetOrNull();
            if (catalog == null || catalog.Nodes.Count == 0 || string.IsNullOrWhiteSpace(currentSource)) {
                sheetNodes = nodes;
                return false;
            }

            for (var i = 0; i < catalog.Nodes.Count; i++) {
                MgDataKitLarkTableNode node = catalog.Nodes[i];
                if (node == null || !IsWorkbookSheetFromSource(node, currentSource))
                    continue;
                nodes.Add(node);
                names.Add(string.IsNullOrWhiteSpace(node.SheetName) ? node.Title : node.SheetName);
            }

            if (nodes.Count == 0) {
                sheetNodes = nodes;
                return false;
            }

            for (var i = 0; i < nodes.Count; i++) {
                if (!string.IsNullOrWhiteSpace(currentSheetId) &&
                    string.Equals(nodes[i].SheetId, currentSheetId, StringComparison.Ordinal)) {
                    currentIndex = i;
                    break;
                }
                if (string.IsNullOrWhiteSpace(currentSheetId) &&
                    string.Equals(nodes[i].SheetName, currentSheetName, StringComparison.Ordinal))
                    currentIndex = i;
            }

            sheetNodes = nodes;
            optionNames = names.ToArray();
            return true;
        }

        private static string GetUnavailableSheetChoice(MgDataFeishuBinding binding) {
            if (string.IsNullOrWhiteSpace(binding?.source))
                return "(请先选择来源)";

            string currentSheet = !string.IsNullOrWhiteSpace(binding.sheetName)
                ? binding.sheetName
                : binding.sheetId;
            return string.IsNullOrWhiteSpace(currentSheet)
                ? "(请刷新 Wiki 树)"
                : $"{currentSheet}（目录待刷新）";
        }

        private static string GetUnavailableSheetTooltip(MgDataFeishuBinding binding) {
            if (string.IsNullOrWhiteSpace(binding?.source))
                return "选择飞书来源后才能选择 Sheet";

            MgDataKitLarkTableCatalog catalog = MgDataKitLarkTableCatalogProvider.GetOrNull();
            if (catalog == null)
                return "未找到飞书表目录。请打开 Wiki 树并刷新完整目录。";
            if (catalog.WorkbookSheetCount == 0)
                return "飞书表目录尚未包含工作簿 Sheet。请打开 Wiki 树并刷新完整目录。";

            return "当前来源在飞书表目录中没有可选 Sheet。请刷新 Wiki 树后重试。";
        }

        private static bool TryFindDefaultSheet(string source, out MgDataKitLarkTableNode defaultSheet) {
            defaultSheet = null;
            MgDataKitLarkTableCatalog catalog = MgDataKitLarkTableCatalogProvider.GetOrNull();
            if (catalog == null || string.IsNullOrWhiteSpace(source))
                return false;

            for (var i = 0; i < catalog.Nodes.Count; i++) {
                if (IsWorkbookSheetFromSource(catalog.Nodes[i], source)) {
                    defaultSheet = catalog.Nodes[i];
                    return true;
                }
            }

            return false;
        }

        private static bool IsWorkbookSheetFromSource(MgDataKitLarkTableNode node, string source) {
            if (node == null || !node.IsWorkbookSheet || string.IsNullOrWhiteSpace(source))
                return false;
            if (string.Equals(node.Source, source, StringComparison.Ordinal))
                return true;

            string normalized = source.Trim();
            int wikiIndex = normalized.IndexOf("wiki/", StringComparison.OrdinalIgnoreCase);
            if (wikiIndex < 0)
                return false;
            string token = normalized.Substring(wikiIndex + 5);
            int queryIndex = token.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
                token = token.Substring(0, queryIndex);
            return string.Equals(node.ParentNodeToken, token.TrimEnd('/'), StringComparison.Ordinal);
        }

        private static string ResolveSourceSelection(
            string currentSource,
            int selectedIndex,
            SourcePopupState state) {
            if (state == null || selectedIndex <= 0)
                return string.Empty;
            if (state.HasUnlistedCurrentSource) {
                if (selectedIndex == 1)
                    return currentSource;
                int nodeIndex = selectedIndex - 2;
                return nodeIndex >= 0 && nodeIndex < state.Nodes.Count
                    ? state.Nodes[nodeIndex].Source
                    : currentSource;
            }

            int index = selectedIndex - 1;
            return index >= 0 && index < state.Nodes.Count
                ? state.Nodes[index].Source
                : currentSource;
        }

        private sealed class SourcePopupState {
            public readonly IReadOnlyList<MgDataKitLarkTableNode> Nodes;
            public readonly string CurrentSource;
            public readonly bool HasUnlistedCurrentSource;

            public SourcePopupState(
                IReadOnlyList<MgDataKitLarkTableNode> nodes,
                string currentSource,
                bool hasUnlistedCurrentSource) {
                Nodes = nodes;
                CurrentSource = currentSource;
                HasUnlistedCurrentSource = hasUnlistedCurrentSource;
            }
        }

        private sealed class SheetPopupState {
            public readonly IReadOnlyList<MgDataKitLarkTableNode> Nodes;

            public SheetPopupState(IReadOnlyList<MgDataKitLarkTableNode> nodes) {
                Nodes = nodes;
            }
        }
    }
}

#endif
