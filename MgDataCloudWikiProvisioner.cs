#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MgDataKit.Editor {
    internal readonly struct MgDataLarkWikiNodeInfo {
        public readonly string Title;
        public readonly string NodeToken;
        public readonly string ParentNodeToken;
        public readonly string ObjectToken;
        public readonly string ObjectType;
        public readonly string NodeType;
        public readonly bool HasChildren;
        public readonly int Depth;
        public readonly string Source;
        public readonly string SheetId;
        public readonly string SheetName;
        public readonly bool IsWorkbookSheet;

        public MgDataLarkWikiNodeInfo(
            string title,
            string nodeToken,
            string parentNodeToken,
            string objectToken,
            string objectType,
            string nodeType,
            bool hasChildren,
            int depth,
            string source = null,
            string sheetId = null,
            string sheetName = null,
            bool isWorkbookSheet = false) {
            Title = title;
            NodeToken = nodeToken;
            ParentNodeToken = parentNodeToken;
            ObjectToken = objectToken;
            ObjectType = objectType;
            NodeType = nodeType;
            HasChildren = hasChildren;
            Depth = depth;
            Source = source;
            SheetId = sheetId;
            SheetName = sheetName;
            IsWorkbookSheet = isWorkbookSheet;
        }

        public MgDataLarkWikiNodeInfo WithDepth(int depth) {
            return new MgDataLarkWikiNodeInfo(this, depth);
        }

        private MgDataLarkWikiNodeInfo(MgDataLarkWikiNodeInfo source, int depth) {
            Title = source.Title;
            NodeToken = source.NodeToken;
            ParentNodeToken = source.ParentNodeToken;
            ObjectToken = source.ObjectToken;
            ObjectType = source.ObjectType;
            NodeType = source.NodeType;
            HasChildren = source.HasChildren;
            Depth = depth;
            Source = source.Source;
            SheetId = source.SheetId;
            SheetName = source.SheetName;
            IsWorkbookSheet = source.IsWorkbookSheet;
        }
    }

    /// <summary>
    /// 飞书知识库：导入本地 xlsx 并挂载到 wiki 父节点下。
    /// </summary>
    internal static class MgDataCloudWikiProvisioner {
        public static IReadOnlyList<MgDataLarkWikiNodeInfo> ListNodeTree(
            string spaceId,
            string rootNodeToken,
            Action<int, string> reportProgress = null) {
            var tree = new List<MgDataLarkWikiNodeInfo>();
            var visitedNodeTokens = new HashSet<string>(StringComparer.Ordinal);
            AppendDescendants(
                spaceId,
                rootNodeToken,
                0,
                tree,
                visitedNodeTokens,
                reportProgress);
            return tree;
        }

        public static Dictionary<string, MgDataCloudAssetDataMigrator.WikiNodeInfo> ListChildSheets(
            string spaceId,
            string parentNodeToken,
            string wikiHost) {
            IReadOnlyList<MgDataLarkWikiNodeInfo> nodes = ListChildNodes(spaceId, parentNodeToken);
            var sheets = new Dictionary<string, MgDataCloudAssetDataMigrator.WikiNodeInfo>(StringComparer.Ordinal);
            for (var i = 0; i < nodes.Count; i++) {
                MgDataLarkWikiNodeInfo node = nodes[i];
                if (!string.Equals(node.ObjectType, "sheet", StringComparison.OrdinalIgnoreCase))
                    continue;

                var title = string.IsNullOrWhiteSpace(node.Title) ? node.NodeToken : node.Title;
                sheets[title] = new MgDataCloudAssetDataMigrator.WikiNodeInfo(
                    title,
                    node.NodeToken,
                    wikiHost);
            }

            return sheets;
        }

        private static IReadOnlyList<MgDataLarkWikiNodeInfo> ListChildNodes(
            string spaceId,
            string parentNodeToken) {
            var args =
                "wiki +node-list " +
                $"--space-id {LarkCliProcessRunner.EscapeCliArgument(spaceId)} ";
            if (!string.IsNullOrWhiteSpace(parentNodeToken)) {
                args +=
                    $"--parent-node-token {LarkCliProcessRunner.EscapeCliArgument(parentNodeToken)} ";
            }
            args += "--page-all --page-limit 0 --as user --json";

            if (!TryRunFromProjectRoot(args, out var cliResult))
                throw new InvalidOperationException("无法启动 Lark CLI。");

            if (!cliResult.Success)
                throw new InvalidOperationException(LarkCliAuthHelper.FormatFailureMessage(cliResult, "列出 wiki 子节点"));

            if (!MgDataLarkCliOutputParser.TryResolveOutputText(cliResult, out var jsonText, out var parseError))
                throw new InvalidOperationException(parseError);

            if (!MgDataLarkCliOutputParser.TryParseWikiNodes(
                    jsonText,
                    out var nodes,
                    out var nodeParseError))
                throw new InvalidOperationException(nodeParseError);

            return nodes;
        }

        private static void AppendDescendants(
            string spaceId,
            string parentNodeToken,
            int depth,
            List<MgDataLarkWikiNodeInfo> tree,
            HashSet<string> visitedNodeTokens,
            Action<int, string> reportProgress) {
            IReadOnlyList<MgDataLarkWikiNodeInfo> children;
            try {
                children = ListChildNodes(spaceId, parentNodeToken);
            } catch (Exception ex) {
                var parentLabel = string.IsNullOrWhiteSpace(parentNodeToken) ? "知识库根节点" : parentNodeToken;
                throw new InvalidOperationException($"读取 {parentLabel} 的子节点失败：{ex.Message}", ex);
            }

            for (var i = 0; i < children.Count; i++) {
                MgDataLarkWikiNodeInfo child = children[i];
                if (!visitedNodeTokens.Add(child.NodeToken))
                    continue;

                MgDataLarkWikiNodeInfo treeNode = child.WithDepth(depth);
                tree.Add(treeNode);
                reportProgress?.Invoke(tree.Count, treeNode.Title);

                if (string.Equals(treeNode.ObjectType, "sheet", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(treeNode.ObjectToken)) {
                    AppendWorkbookSheets(treeNode, tree, reportProgress);
                }

                if (treeNode.HasChildren) {
                    AppendDescendants(
                        spaceId,
                        treeNode.NodeToken,
                        depth + 1,
                        tree,
                        visitedNodeTokens,
                        reportProgress);
                }
            }
        }

        private static void AppendWorkbookSheets(
            MgDataLarkWikiNodeInfo workbookNode,
            List<MgDataLarkWikiNodeInfo> tree,
            Action<int, string> reportProgress) {
            if (!TryListWorkbookSheets(
                    workbookNode.ObjectToken,
                    out List<MgDataLarkCliOutputParser.WorkbookSheetInfo> sheets,
                    out string errorMessage)) {
                throw new InvalidOperationException(
                    $"读取工作簿“{workbookNode.Title}”的 Sheet 列表失败：{errorMessage}");
            }

            for (var i = 0; i < sheets.Count; i++) {
                MgDataLarkCliOutputParser.WorkbookSheetInfo sheet = sheets[i];
                if (!sheet.IsCsvReadable)
                    continue;

                string sheetName = string.IsNullOrWhiteSpace(sheet.Name) ? sheet.Id : sheet.Name;
                string syntheticToken = workbookNode.NodeToken + "::sheet::" + (sheet.Id ?? sheetName);
                var child = new MgDataLarkWikiNodeInfo(
                    sheetName,
                    syntheticToken,
                    workbookNode.NodeToken,
                    sheet.Id,
                    "sheet",
                    "workbook_sheet",
                    false,
                    workbookNode.Depth + 1,
                    workbookNode.Source ?? "wiki/" + workbookNode.NodeToken,
                    sheet.Id,
                    sheet.Name ?? sheet.Id,
                    true);
                tree.Add(child);
                reportProgress?.Invoke(tree.Count, $"{workbookNode.Title} / {sheetName}");
            }
        }

        private static bool TryListWorkbookSheets(
            string spreadsheetToken,
            out List<MgDataLarkCliOutputParser.WorkbookSheetInfo> sheets,
            out string errorMessage) {
            sheets = new List<MgDataLarkCliOutputParser.WorkbookSheetInfo>();
            errorMessage = null;
            string args =
                "sheets +workbook-info --as user --json --spreadsheet-token " +
                LarkCliProcessRunner.EscapeCliArgument(spreadsheetToken);
            if (!TryRunFromProjectRoot(args, out LarkCliProcessRunner.RunResult cliResult) || cliResult == null) {
                errorMessage = "无法启动 Lark CLI。";
                return false;
            }

            if (!cliResult.Success) {
                errorMessage = LarkCliAuthHelper.FormatFailureMessage(cliResult, "读取飞书工作簿信息");
                return false;
            }

            if (!MgDataLarkCliOutputParser.TryResolveOutputText(cliResult, out string jsonText, out errorMessage) ||
                !MgDataLarkCliOutputParser.TryParseEnvelopeOk(jsonText, out errorMessage) ||
                !MgDataLarkCliOutputParser.TryParseWorkbookSheets(jsonText, out sheets)) {
                errorMessage ??= "工作簿信息中没有可用 Sheet。";
                return false;
            }

            return true;
        }

        public static MgDataCloudAssetDataMigrator.WikiNodeInfo ImportExcelAndMoveToWiki(
            string absoluteExcelPath,
            string sheetTitle,
            string spaceId,
            string parentNodeToken,
            string wikiHost) {
            if (string.IsNullOrWhiteSpace(absoluteExcelPath) || !File.Exists(absoluteExcelPath))
                throw new FileNotFoundException("Excel 不存在。", absoluteExcelPath);

            var projectRoot = LarkCliPathResolver.GetProjectRootPath();
            var relativeExcelPath = "./" + Path.GetRelativePath(projectRoot, absoluteExcelPath).Replace('\\', '/');

            var importArgs =
                "sheets +workbook-import " +
                $"--file {LarkCliProcessRunner.EscapeCliArgument(relativeExcelPath)} " +
                $"--name {LarkCliProcessRunner.EscapeCliArgument(sheetTitle)} " +
                "--as user --json";

            if (!TryRunFromProjectRoot(importArgs, out var importResult))
                throw new InvalidOperationException("无法启动 Lark CLI。");

            if (!importResult.Success)
                throw new InvalidOperationException(LarkCliAuthHelper.FormatFailureMessage(importResult, "导入 Excel 到飞书"));

            if (!MgDataLarkCliOutputParser.TryResolveOutputText(importResult, out var importJson, out var importParseError))
                throw new InvalidOperationException(importParseError);

            if (!MgDataLarkCliOutputParser.TryParseWorkbookImportToken(importJson, out var spreadsheetToken, out var importError))
                throw new InvalidOperationException(importError);

            var moveArgs =
                "wiki +move " +
                "--obj-type sheet " +
                $"--obj-token {LarkCliProcessRunner.EscapeCliArgument(spreadsheetToken)} " +
                $"--target-space-id {LarkCliProcessRunner.EscapeCliArgument(spaceId)} " +
                $"--target-parent-token {LarkCliProcessRunner.EscapeCliArgument(parentNodeToken)} " +
                "--as user --json";

            if (!TryRunFromProjectRoot(moveArgs, out var moveResult))
                throw new InvalidOperationException("无法启动 Lark CLI。");

            if (!moveResult.Success)
                throw new InvalidOperationException(LarkCliAuthHelper.FormatFailureMessage(moveResult, "移入知识库"));

            if (!MgDataLarkCliOutputParser.TryResolveOutputText(moveResult, out var moveJson, out var moveParseError))
                throw new InvalidOperationException(moveParseError);

            if (!MgDataLarkCliOutputParser.TryParseWikiMoveNodeToken(moveJson, out var nodeToken, out var moveError))
                throw new InvalidOperationException(moveError);

            return new MgDataCloudAssetDataMigrator.WikiNodeInfo(sheetTitle, nodeToken, wikiHost);
        }

        static bool TryRunFromProjectRoot(string arguments, out LarkCliProcessRunner.RunResult result) {
            return LarkCliProcessRunner.TryRun(arguments, out result, LarkCliPathResolver.GetProjectRootPath());
        }
    }
}
#endif
