#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MgDataKit;
using UnityEngine;

namespace MgDataKit.Editor {
    public sealed class FeishuDataSourceImporter : IMgDataSourceImporter {
        public bool CanImport(string sourceId) {
            return string.Equals(sourceId, "feishu", StringComparison.OrdinalIgnoreCase);
        }

        public MgDataSourceReadResult Read(MgDataBase asset, MgDataKitAssetEntry entry) {
            if (asset == null || entry == null)
                return MgDataSourceReadResult.Failed("Asset 或 Catalog Entry 为空。");

            MgDataFeishuBinding binding = FeishuBindingUtility.Read(entry);
            if (string.IsNullOrWhiteSpace(binding?.source) &&
                !string.IsNullOrWhiteSpace(entry.SourceData)) {
                try {
                    binding = JsonUtility.FromJson<MgDataFeishuBinding>(entry.SourceData);
                } catch (Exception) {
                    binding = FeishuBindingUtility.Read(entry);
                }
            }
            if (binding == null || string.IsNullOrWhiteSpace(binding.source))
                return MgDataSourceReadResult.Failed($"飞书来源绑定为空，无法导入：{asset.name}");
            if (!MgDataGridImporter.TryGetListField(asset.GetType(), out FieldInfo listField))
                return MgDataSourceReadResult.Failed("MgData 类型中未找到唯一的 List<T> 字段。");

            if (!MgDataFeishuSyncService.TryPreflightUserAuth(out string authError))
                return MgDataSourceReadResult.Failed(authError);

            string source = ComposeSourceUrl(binding.source);
            string spreadsheetReference = ResolveSpreadsheetReference(source, out string resolveError);
            if (string.IsNullOrWhiteSpace(spreadsheetReference))
                return MgDataSourceReadResult.Failed(resolveError ?? "无法解析飞书电子表格来源。");

            if (!TryRunWorkbookInfo(spreadsheetReference, out string workbookJson, out string workbookError))
                return MgDataSourceReadResult.Failed(workbookError);
            if (!MgDataLarkCliOutputParser.TryParseWorkbookSheets(
                    workbookJson,
                    out List<MgDataLarkCliOutputParser.WorkbookSheetInfo> sheets) || sheets.Count == 0)
                return MgDataSourceReadResult.Failed("飞书工作簿中没有可用的 Sheet。");

            if (!TrySelectSheet(sheets, binding, listField.Name, out var selected, out string selectionError))
                return MgDataSourceReadResult.Failed(selectionError);

            if (!TryRunCsvGet(
                    spreadsheetReference,
                    selected,
                    out string csvJson,
                    out string csvError))
                return MgDataSourceReadResult.Failed(csvError);
            if (MgDataLarkCliOutputParser.HasIncompletePage(
                    csvJson,
                    out bool hasMore,
                    out bool truncated))
                return MgDataSourceReadResult.Failed(
                    "飞书 CSV 返回不完整：has_more/truncated=true。为避免用不完整数据覆盖本地 Asset，已取消导入。\n" +
                    $"has_more={hasMore}, truncated={truncated}\n" +
                    $"Sheet={selected.Name ?? selected.Id}");
            if (!MgDataLarkCliOutputParser.TryParseCellValuesGrid(
                    csvJson,
                    out string[][] grid,
                    out string parseError))
                return MgDataSourceReadResult.Failed(parseError);

            return new MgDataSourceReadResult {
                Success = true,
                Grid = grid,
                SourceLabel = source,
                SheetName = selected.Name,
                SheetId = selected.Id
            };
        }

        private static bool TrySelectSheet(
            IReadOnlyList<MgDataLarkCliOutputParser.WorkbookSheetInfo> sheets,
            MgDataFeishuBinding binding,
            string listFieldName,
            out MgDataLarkCliOutputParser.WorkbookSheetInfo selected,
            out string errorMessage) {
            selected = default;
            errorMessage = null;
            if (!string.IsNullOrWhiteSpace(binding.sheetId)) {
                for (var i = 0; i < sheets.Count; i++) {
                    if (!string.Equals(sheets[i].Id, binding.sheetId.Trim(), StringComparison.Ordinal))
                        continue;

                    if (!sheets[i].IsCsvReadable) {
                        errorMessage =
                            $"绑定的 Sheet ID 不支持 CSV 读取：{binding.sheetId} " +
                            $"(resource_type={sheets[i].ResourceType})。";
                        return false;
                    }

                    selected = sheets[i];
                    return true;
                }

                errorMessage = $"飞书工作簿中未找到绑定的 Sheet ID：{binding.sheetId}。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(binding.sheetName)) {
                for (var i = 0; i < sheets.Count; i++) {
                    if (!string.Equals(sheets[i].Name, binding.sheetName.Trim(), StringComparison.Ordinal))
                        continue;

                    if (!sheets[i].IsCsvReadable) {
                        errorMessage =
                            $"绑定的 Sheet 名称不支持 CSV 读取：{binding.sheetName} " +
                            $"(resource_type={sheets[i].ResourceType})。";
                        return false;
                    }

                    selected = sheets[i];
                    return true;
                }

                errorMessage = $"飞书工作簿中未找到绑定的 Sheet 名称：{binding.sheetName}。";
                return false;
            }

            string expectedName = SanitizeSheetName(listFieldName);
            for (var i = 0; i < sheets.Count; i++) {
                if (sheets[i].IsCsvReadable &&
                    IsListFieldSheetName(sheets[i].Name, listFieldName, expectedName)) {
                    selected = sheets[i];
                    return true;
                }
            }

            for (var i = 0; i < sheets.Count; i++) {
                if (!sheets[i].IsCsvReadable)
                    continue;

                selected = sheets[i];
                return true;
            }

            errorMessage = "飞书工作簿中没有可通过 CSV 读取的 Sheet。";
            return false;
        }

        private static bool IsListFieldSheetName(string sheetName, string listFieldName, string expectedName) {
            if (string.IsNullOrWhiteSpace(sheetName))
                return false;

            var normalizedSheetName = sheetName.Trim();
            if (string.Equals(normalizedSheetName, listFieldName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedSheetName, expectedName, StringComparison.OrdinalIgnoreCase))
                return true;

            string trimmedFieldName = listFieldName?.TrimStart('_');
            string trimmedExpectedName = expectedName?.TrimStart('_');
            return string.Equals(normalizedSheetName, trimmedFieldName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedSheetName, trimmedExpectedName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSpreadsheetReference(string source, out string errorMessage) {
            errorMessage = null;
            if (!IsWikiReference(source))
                return source;

            string args = "wiki +node-get " +
                          $"--node-token {LarkCliProcessRunner.EscapeCliArgument(source)} " +
                          "--as user --json";
            if (!LarkCliProcessRunner.TryRun(args, out LarkCliProcessRunner.RunResult result) || result == null)
                return SetError(out errorMessage, "无法启动 Lark CLI。");
            if (!result.Success)
                return SetError(out errorMessage, LarkCliAuthHelper.FormatFailureMessage(result, "读取 Wiki 节点"));
            if (!MgDataLarkCliOutputParser.TryResolveOutputText(result, out string jsonText, out errorMessage))
                return null;
            if (!MgDataLarkCliOutputParser.TryParseWikiSpreadsheetToken(jsonText, out string token, out errorMessage))
                return null;
            return token;
        }

        private static bool TryRunWorkbookInfo(
            string spreadsheetReference,
            out string jsonText,
            out string errorMessage) {
            jsonText = null;
            errorMessage = null;
            var builder = new StringBuilder("sheets +workbook-info --as user --json ");
            AppendSpreadsheetReference(builder, spreadsheetReference);
            if (!LarkCliProcessRunner.TryRun(builder.ToString(), out LarkCliProcessRunner.RunResult result) || result == null) {
                errorMessage = "无法启动 Lark CLI。";
                return false;
            }
            if (!result.Success) {
                errorMessage = LarkCliAuthHelper.FormatFailureMessage(result, "读取飞书工作簿信息");
                return false;
            }
            return MgDataLarkCliOutputParser.TryResolveOutputText(result, out jsonText, out errorMessage) &&
                   MgDataLarkCliOutputParser.TryParseEnvelopeOk(jsonText, out errorMessage);
        }

        private static bool TryRunCsvGet(
            string spreadsheetReference,
            MgDataLarkCliOutputParser.WorkbookSheetInfo sheet,
            out string jsonText,
            out string errorMessage) {
            jsonText = null;
            errorMessage = null;
            var builder = new StringBuilder("sheets +csv-get --as user --json --format json ");
            builder.Append("--range ").Append(BuildRange(sheet)).Append(' ');
            if (!string.IsNullOrWhiteSpace(sheet.Id))
                builder.Append("--sheet-id ").Append(LarkCliProcessRunner.EscapeCliArgument(sheet.Id)).Append(' ');
            else
                builder.Append("--sheet-name ").Append(LarkCliProcessRunner.EscapeCliArgument(sheet.Name)).Append(' ');
            AppendSpreadsheetReference(builder, spreadsheetReference);
            if (!LarkCliProcessRunner.TryRun(builder.ToString(), out LarkCliProcessRunner.RunResult result) || result == null) {
                errorMessage = "无法启动 Lark CLI。";
                return false;
            }
            if (!result.Success) {
                errorMessage = LarkCliAuthHelper.FormatFailureMessage(result, "读取 Sheet 数据");
                return false;
            }
            if (!MgDataLarkCliOutputParser.TryResolveOutputText(result, out jsonText, out errorMessage))
                return false;
            return MgDataLarkCliOutputParser.TryParseEnvelopeOk(jsonText, out errorMessage);
        }

        private static void AppendSpreadsheetReference(StringBuilder builder, string reference) {
            if (reference.Contains("://", StringComparison.Ordinal))
                builder.Append("--url ").Append(LarkCliProcessRunner.EscapeCliArgument(reference)).Append(' ');
            else
                builder.Append("--spreadsheet-token ")
                    .Append(LarkCliProcessRunner.EscapeCliArgument(reference))
                    .Append(' ');
        }

        private static string BuildRange(MgDataLarkCliOutputParser.WorkbookSheetInfo sheet) {
            var rowCount = sheet.RowCount > 0 ? sheet.RowCount : 10000;
            var columnCount = sheet.ColumnCount > 0 ? sheet.ColumnCount : 18278;
            return $"A1:{ColumnName(columnCount)}{rowCount}";
        }

        private static string ColumnName(int columnNumber) {
            var builder = new StringBuilder();
            var value = Math.Max(1, columnNumber);
            while (value > 0) {
                value--;
                builder.Insert(0, (char)('A' + value % 26));
                value /= 26;
            }

            return builder.ToString();
        }

        private static string ComposeSourceUrl(string source) {
            var normalized = (source ?? string.Empty).Trim();
            if (normalized.Contains("://", StringComparison.Ordinal))
                return normalized;
            if (!normalized.StartsWith("wiki/", StringComparison.OrdinalIgnoreCase))
                return normalized;

            LarkProjectConfig settings = LarkProjectConfigStore.GetOrNull();
            string host = (settings?.WikiHost ?? LarkProjectConfig.DefaultWikiHost)?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(host))
                return normalized;
            int wikiIndex = host.IndexOf("/wiki", StringComparison.OrdinalIgnoreCase);
            string root = wikiIndex >= 0 ? host.Substring(0, wikiIndex) : host;
            return root.TrimEnd('/') + "/" + normalized.TrimStart('/');
        }

        private static bool IsWikiReference(string source) {
            return source.StartsWith("wiki/", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("/wiki/", StringComparison.OrdinalIgnoreCase);
        }

        private static string SetError(out string errorMessage, string message) {
            errorMessage = message;
            return null;
        }

        private static string SanitizeSheetName(string name) {
            if (string.IsNullOrEmpty(name))
                return "Sheet";

            var builder = new StringBuilder();
            foreach (var c in name) {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
                    builder.Append(c);
            }

            return builder.Length > 0 ? builder.ToString() : "Sheet";
        }
    }
}
#endif
