#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MgDataKit.Editor {
    /// <summary>
    /// 从 lark-cli JSON envelope 中提取业务数据。
    /// </summary>
    internal static class MgDataLarkCliOutputParser {
        public static string ExtractJsonObject(string output) {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            for (var start = 0; start < output.Length; start++) {
                if (output[start] != '{')
                    continue;

                var depth = 0;
                var inString = false;
                var escaped = false;
                for (var index = start; index < output.Length; index++) {
                    var current = output[index];
                    if (inString) {
                        if (escaped) {
                            escaped = false;
                        } else if (current == '\\') {
                            escaped = true;
                        } else if (current == '"') {
                            inString = false;
                        }

                        continue;
                    }

                    if (current == '"') {
                        inString = true;
                        continue;
                    }
                    if (current == '{') {
                        depth++;
                        continue;
                    }
                    if (current != '}')
                        continue;

                    depth--;
                    if (depth == 0)
                        return output.Substring(start, index - start + 1);
                }
            }

            return null;
        }

        public static bool TryResolveOutputText(LarkCliProcessRunner.RunResult result, out string jsonText, out string errorMessage) {
            jsonText = null;
            errorMessage = null;
            if (result == null) {
                errorMessage = "CLI 未返回结果。";
                return false;
            }

            if (TryReadOffloadedJsonPath(result.StandardOutput, out var offloadedJson) ||
                TryReadOffloadedJsonPath(result.StandardError, out offloadedJson)) {
                jsonText = ExtractJsonObject(offloadedJson);
                if (string.IsNullOrEmpty(jsonText)) {
                    errorMessage = "CLI 输出文件不包含有效 JSON 对象。";
                    return false;
                }
                return true;
            }

            string standardOutput = result.StandardOutput ?? string.Empty;
            jsonText = ExtractJsonObject(standardOutput);
            if (string.IsNullOrEmpty(jsonText)) {
                string diagnostics = result.StandardError ?? string.Empty;
                errorMessage = string.IsNullOrWhiteSpace(standardOutput) && string.IsNullOrWhiteSpace(diagnostics)
                    ? "CLI 未返回任何内容。"
                    : string.IsNullOrWhiteSpace(diagnostics)
                        ? standardOutput.Trim()
                        : diagnostics.Trim();
                return false;
            }

            return true;
        }

        public static bool TryParseEnvelopeOk(string jsonText, out string errorMessage) {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(jsonText)) {
                errorMessage = "JSON 为空。";
                return false;
            }

            if (Regex.IsMatch(jsonText, "\"ok\"\\s*:\\s*true", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(jsonText, "\"ok\"\\s*:\\s*false", RegexOptions.IgnoreCase)) {
                errorMessage = ExtractEnvelopeErrorMessage(jsonText) ?? "飞书 CLI 返回失败。";
                return false;
            }

            // 某些 CLI 版本只返回 error 对象而没有 ok 字段；不能把它静默当作成功。
            if (Regex.IsMatch(jsonText, "\"error\"\\s*:\\s*(?:\\{|\"|[-+]?\\d)", RegexOptions.IgnoreCase)) {
                errorMessage = ExtractEnvelopeErrorMessage(jsonText) ?? "飞书 CLI 返回失败。";
                return false;
            }

            // auth status 等命令不含 ok/error 字段；勿把 identities.*.message 误判为失败。
            return true;
        }

        static string ExtractEnvelopeErrorMessage(string jsonText) {
            var errorBlockMatch = Regex.Match(
                jsonText,
                "\"error\"\\s*:\\s*\\{[\\s\\S]*?\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.Singleline);
            if (errorBlockMatch.Success)
                return Regex.Unescape(errorBlockMatch.Groups[1].Value);

            return ExtractJsonStringValue(jsonText, "message");
        }

        public static bool TryParseWikiSpreadsheetToken(string jsonText, out string spreadsheetToken, out string errorMessage) {
            spreadsheetToken = null;
            errorMessage = null;

            if (!TryParseEnvelopeOk(jsonText, out errorMessage))
                return false;

            var objType = ExtractNestedString(jsonText, "obj_type");
            if (!string.IsNullOrEmpty(objType) &&
                !string.Equals(objType, "sheet", StringComparison.OrdinalIgnoreCase)) {
                errorMessage = $"Wiki 节点类型不是 sheet（obj_type={objType}）。";
                return false;
            }

            spreadsheetToken = ExtractNestedString(jsonText, "obj_token")
                               ?? ExtractNestedString(jsonText, "spreadsheet_token")
                               ?? ExtractNestedString(jsonText, "token");
            if (string.IsNullOrWhiteSpace(spreadsheetToken)) {
                errorMessage = "Wiki 解析结果中未找到 spreadsheet token。";
                return false;
            }

            return true;
        }

        public static bool TryParseUserAuthReady(string jsonText, out string errorMessage) {
            errorMessage = null;
            if (!TryParseEnvelopeOk(jsonText, out errorMessage))
                return false;

            if (IsIdentityReady(jsonText, "user"))
                return true;

            if (IsIdentityReady(jsonText, "bot")) {
                errorMessage =
                    "当前仅有应用机器人身份可用，飞书同步需要个人账号权限（--as user）。\n" +
                    "请执行 MgDataKit → 飞书 Lark CLI → 登录飞书账号。";
                return false;
            }

            errorMessage = "个人账号尚未登录或未就绪。请执行 MgDataKit → 飞书 Lark CLI → 登录飞书账号。";
            return false;
        }

        public static bool TryGetUserAuthStatus(string jsonText, out string status) {
            status = null;
            if (string.IsNullOrWhiteSpace(jsonText))
                return false;

            var match = Regex.Match(
                jsonText,
                "\"user\"\\s*:\\s*\\{[\\s\\S]*?\"status\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            status = Regex.Unescape(match.Groups[1].Value);
            return !string.IsNullOrWhiteSpace(status);
        }

        static bool IsIdentityReady(string jsonText, string identityName) {
            if (string.IsNullOrWhiteSpace(jsonText) || string.IsNullOrWhiteSpace(identityName))
                return false;

            var blockMatch = Regex.Match(
                jsonText,
                $"\"{Regex.Escape(identityName)}\"\\s*:\\s*\\{{([\\s\\S]*?)\\}}\\s*(?:,|\\}})",
                RegexOptions.Singleline);
            if (!blockMatch.Success)
                return false;

            var block = blockMatch.Groups[1].Value;
            return Regex.IsMatch(block, "\"status\"\\s*:\\s*\"ready\"", RegexOptions.IgnoreCase) &&
                   Regex.IsMatch(block, "\"available\"\\s*:\\s*true", RegexOptions.IgnoreCase);
        }

        public static bool TryParseCellValuesGrid(string jsonText, out string[][] values, out string errorMessage) {
            values = null;
            errorMessage = null;

            if (!TryParseEnvelopeOk(jsonText, out errorMessage))
                return false;

            if (TryParseValuesArray(jsonText, out values))
                return values.Length > 0;

            if (TryParseAnnotatedCsv(jsonText, out values))
                return values.Length > 0;

            if (TryParseRangesCells(jsonText, out values))
                return values.Length > 0;

            errorMessage = "无法从 CLI 输出中解析单元格网格。";
            return false;
        }

        public static bool HasIncompletePage(
            string jsonText,
            out bool hasMore,
            out bool truncated) {
            hasMore = ReadJsonBoolean(jsonText, "has_more") || ReadJsonBoolean(jsonText, "hasMore");
            truncated = ReadJsonBoolean(jsonText, "truncated");
            return hasMore || truncated;
        }

        public static bool TryParseWikiNodes(
            string jsonText,
            out List<MgDataLarkWikiNodeInfo> nodes,
            out string errorMessage) {
            nodes = new List<MgDataLarkWikiNodeInfo>();
            errorMessage = null;
            if (!TryParseEnvelopeOk(jsonText, out errorMessage))
                return false;

            WikiNodeListEnvelope envelope;
            try {
                envelope = JsonUtility.FromJson<WikiNodeListEnvelope>(jsonText);
            } catch (Exception ex) {
                errorMessage = $"无法解析 Wiki 节点列表：{ex.Message}";
                return false;
            }

            if (envelope?.data?.nodes == null) {
                errorMessage = "Wiki 节点列表缺少 data.nodes。";
                return false;
            }

            for (var i = 0; i < envelope.data.nodes.Length; i++) {
                WikiNodeData node = envelope.data.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.node_token))
                    continue;

                nodes.Add(new MgDataLarkWikiNodeInfo(
                    node.title,
                    node.node_token,
                    node.parent_node_token,
                    node.obj_token,
                    node.obj_type,
                    node.node_type,
                    node.has_child,
                    0));
            }

            return true;
        }

        [Serializable]
        private sealed class WikiNodeListEnvelope {
            public WikiNodeListData data;
        }

        [Serializable]
        private sealed class WikiNodeListData {
            public WikiNodeData[] nodes;
        }

        [Serializable]
        private sealed class WikiNodeData {
            public string node_token;
            public string obj_token;
            public string obj_type;
            public string parent_node_token;
            public string node_type;
            public string title;
            public bool has_child;
        }

        public static bool TryParseWorkbookImportToken(string jsonText, out string spreadsheetToken, out string errorMessage) {
            spreadsheetToken = null;
            errorMessage = null;
            if (!TryParseEnvelopeOk(jsonText, out errorMessage))
                return false;

            spreadsheetToken = ExtractNestedString(jsonText, "token");
            if (string.IsNullOrWhiteSpace(spreadsheetToken)) {
                errorMessage = "导入结果中未找到 spreadsheet token。";
                return false;
            }

            return true;
        }

        public static bool TryParseWikiMoveNodeToken(string jsonText, out string nodeToken, out string errorMessage) {
            nodeToken = null;
            errorMessage = null;
            if (!TryParseEnvelopeOk(jsonText, out errorMessage))
                return false;

            nodeToken = ExtractNestedString(jsonText, "wiki_token")
                        ?? ExtractNestedString(jsonText, "node_token");
            if (string.IsNullOrWhiteSpace(nodeToken)) {
                errorMessage = "移入知识库结果中未找到 wiki node token。";
                return false;
            }

            return true;
        }

        public readonly struct WorkbookSheetInfo {
            public readonly string Name;
            public readonly string Id;
            public readonly int RowCount;
            public readonly int ColumnCount;
            public readonly string ResourceType;

            public bool IsCsvReadable => string.IsNullOrWhiteSpace(ResourceType) ||
                                         string.Equals(ResourceType, "sheet", StringComparison.OrdinalIgnoreCase);

            public WorkbookSheetInfo(
                string name,
                string id,
                int rowCount = 0,
                int columnCount = 0,
                string resourceType = null) {
                Name = name;
                Id = id;
                RowCount = rowCount;
                ColumnCount = columnCount;
                ResourceType = resourceType;
            }
        }

        public static bool TryParseWorkbookSheets(string jsonText, out List<WorkbookSheetInfo> sheets) {
            sheets = new List<WorkbookSheetInfo>();
            if (string.IsNullOrWhiteSpace(jsonText))
                return false;

            var dataSection = ExtractJsonObjectByKey(jsonText, "data") ?? jsonText;
            var sheetsStart = FindKeyArrayStart(dataSection, "sheets");
            if (sheetsStart < 0 ||
                !TryReadJsonArray(dataSection, sheetsStart, out _, out var sheetObjects))
                return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < sheetObjects.Count; i++) {
                var sheetJson = sheetObjects[i]?.Trim();
                if (string.IsNullOrEmpty(sheetJson) || sheetJson[0] != '{')
                    return false;

                var id = ExtractJsonStringValue(sheetJson, "sheet_id")
                         ?? ExtractJsonStringValue(sheetJson, "sheetId");
                var name = ExtractJsonStringValue(sheetJson, "title")
                           ?? ExtractJsonStringValue(sheetJson, "name")
                           ?? ExtractJsonStringValue(sheetJson, "sheet_name");
                var rowCount = ExtractJsonIntValue(sheetJson, "row_count", "rowCount");
                var columnCount = ExtractJsonIntValue(sheetJson, "column_count", "columnCount");
                var resourceType = ExtractJsonStringValue(sheetJson, "resource_type")
                                   ?? ExtractJsonStringValue(sheetJson, "resourceType");
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
                    continue;

                var key = $"{name}|{id}";
                if (!seen.Add(key))
                    continue;

                sheets.Add(new WorkbookSheetInfo(name, id, rowCount, columnCount, resourceType));
            }

            return sheets.Count > 0;
        }

        static bool TryReadOffloadedJsonPath(string combinedOutput, out string jsonText) {
            jsonText = null;
            if (string.IsNullOrWhiteSpace(combinedOutput))
                return false;

            var match = Regex.Match(
                combinedOutput,
                "(?:written to|offload(?:ed)? to|output file)\\s*[:：]?\\s*(?:\"(?<quoted>[^\"]+\\.json)\"|'(?<single>[^']+\\.json)'|(?<bare>(?:[A-Za-z]:[\\\\/]|/|\\.{0,2}[\\\\/])?[^\\r\\n\"']+?\\.json))",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            var path = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                : match.Groups["bare"].Value.Trim().TrimEnd('.', ',', ';', ')', ']');
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(LarkCliPathResolver.GetProjectRootPath(), path));
            if (!File.Exists(path))
                return false;

            jsonText = File.ReadAllText(path);
            return !string.IsNullOrWhiteSpace(jsonText);
        }

        static bool TryParseValuesArray(string jsonText, out string[][] values) {
            values = null;
            var dataSection = ExtractJsonObjectByKey(jsonText, "data") ?? jsonText;
            var valuesToken = FindKeyArrayStart(dataSection, "values");
            if (valuesToken < 0)
                return false;

            if (!TryParseJsonStringMatrix(dataSection, valuesToken, out var matrix) || matrix.Count == 0)
                return false;

            values = matrix.ToArray();
            return true;
        }

        static bool TryParseAnnotatedCsv(string jsonText, out string[][] values) {
            values = null;
            var csv = ExtractJsonStringValue(jsonText, "annotated_csv");
            if (string.IsNullOrEmpty(csv))
                return false;

            var rows = new List<string[]>();
            foreach (string rawLine in SplitCsvLogicalLines(csv)) {
                var line = rawLine.TrimStart('\uFEFF');
                line = Regex.Replace(line, @"^\[row=\d+\]\s*", string.Empty);
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                rows.Add(ParseCsvLine(line));
            }

            if (rows.Count == 0)
                return false;

            values = rows.ToArray();
            return true;
        }

        static bool TryParseRangesCells(string jsonText, out string[][] values) {
            values = null;
            var dataSection = ExtractJsonObjectByKey(jsonText, "data") ?? jsonText;
            var rangesStart = dataSection.IndexOf("\"ranges\"", StringComparison.Ordinal);
            if (rangesStart < 0)
                return false;

            var cellsStart = dataSection.IndexOf("\"cells\"", rangesStart, StringComparison.Ordinal);
            if (cellsStart < 0)
                return false;

            var matrixStart = dataSection.IndexOf('[', cellsStart);
            if (matrixStart < 0)
                return false;

            if (!TryParseJsonCellMatrix(dataSection, matrixStart, out var matrix) || matrix.Count == 0)
                return false;

            values = matrix.ToArray();
            return true;
        }

        static bool TryParseJsonStringMatrix(string json, int startIndex, out List<string[]> matrix) {
            matrix = new List<string[]>();
            if (!TryReadJsonArray(json, startIndex, out var endIndex, out var elements))
                return false;

            for (var i = 0; i < elements.Count; i++) {
                if (!TryParseJsonStringArray(elements[ i ], out var row))
                    return false;
                matrix.Add(row.ToArray());
            }

            return matrix.Count > 0;
        }

        static bool TryParseJsonCellMatrix(string json, int startIndex, out List<string[]> matrix) {
            matrix = new List<string[]>();
            if (!TryReadJsonArray(json, startIndex, out _, out var rowElements))
                return false;

            for (var r = 0; r < rowElements.Count; r++) {
                if (!TryParseJsonStringArray(rowElements[ r ], out var rowTokens)) {
                    matrix.Add(Array.Empty<string>());
                    continue;
                }

                var row = new string[rowTokens.Count];
                for (var c = 0; c < rowTokens.Count; c++)
                    row[ c ] = ParseCellScalar(rowTokens[ c ]);
                matrix.Add(row);
            }

            return matrix.Count > 0;
        }

        static bool TryParseJsonStringArray(string arrayJson, out List<string> values) {
            values = new List<string>();
            arrayJson = arrayJson?.Trim();
            if (string.IsNullOrEmpty(arrayJson))
                return false;

            if (arrayJson[0] != '[') {
                values.Add(ParseCellScalar(arrayJson));
                return true;
            }

            if (!TryReadJsonArray(arrayJson, 0, out _, out var elements))
                return false;

            for (var i = 0; i < elements.Count; i++)
                values.Add(ParseCellScalar(elements[ i ]));
            return true;
        }

        static string ParseCellScalar(string token) {
            token = token?.Trim();
            if (string.IsNullOrEmpty(token) || string.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
                return Regex.Unescape(token.Substring(1, token.Length - 2));

            if (token[0] == '{') {
                var text = ExtractJsonStringValue(token, "text")
                           ?? ExtractJsonStringValue(token, "value");
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            return token;
        }

        static bool TryReadJsonArray(string json, int startIndex, out int endIndex, out List<string> elements) {
            endIndex = startIndex;
            elements = new List<string>();
            if (startIndex < 0 || startIndex >= json.Length || json[startIndex] != '[')
                return false;

            var i = startIndex + 1;
            while (i < json.Length) {
                i = SkipWhitespace(json, i);
                if (i >= json.Length)
                    return false;

                if (json[i] == ']') {
                    endIndex = i;
                    return true;
                }

                if (!TryReadJsonValue(json, i, out var valueEnd, out var valueToken))
                    return false;

                elements.Add(valueToken);
                i = SkipWhitespace(json, valueEnd + 1);
                if (i >= json.Length)
                    return false;

                if (json[i] == ',') {
                    i++;
                    continue;
                }

                if (json[i] == ']') {
                    endIndex = i;
                    return true;
                }

                return false;
            }

            return false;
        }

        static bool TryReadJsonValue(string json, int startIndex, out int endIndex, out string token) {
            endIndex = startIndex;
            token = null;
            if (startIndex >= json.Length)
                return false;

            var c = json[startIndex];
            if (c == '"') {
                var sb = new StringBuilder();
                sb.Append(c);
                var i = startIndex + 1;
                var escaped = false;
                while (i < json.Length) {
                    sb.Append(json[i]);
                    if (escaped) {
                        escaped = false;
                    } else if (json[i] == '\\') {
                        escaped = true;
                    } else if (json[i] == '"') {
                        endIndex = i;
                        token = sb.ToString();
                        return true;
                    }

                    i++;
                }

                return false;
            }

            if (c == '{' || c == '[') {
                var depth = 0;
                var inString = false;
                var escaped = false;
                for (var i = startIndex; i < json.Length; i++) {
                    var current = json[i];
                    if (inString) {
                        if (escaped) {
                            escaped = false;
                        } else if (current == '\\') {
                            escaped = true;
                        } else if (current == '"') {
                            inString = false;
                        }

                        continue;
                    }

                    if (current == '"') {
                        inString = true;
                        continue;
                    }
                    if (current == '{' || current == '[')
                        depth++;
                    else if (current == '}' || current == ']')
                        depth--;

                    if (depth == 0) {
                        endIndex = i;
                        token = json.Substring(startIndex, endIndex - startIndex + 1);
                        return true;
                    }
                }

                return false;
            }

            var j = startIndex;
            while (j < json.Length && ",]}".IndexOf(json[j]) < 0)
                j++;

            endIndex = j - 1;
            token = json.Substring(startIndex, j - startIndex).Trim();
            return token.Length > 0;
        }

        static int FindKeyArrayStart(string json, string key) {
            var pattern = $"\"{key}\"\\s*:\\s*\\[";
            var match = Regex.Match(json, pattern);
            if (!match.Success)
                return -1;

            return match.Index + match.Value.Length - 1;
        }

        static string ExtractJsonObjectByKey(string json, string key) {
            var pattern = $"\"{key}\"\\s*:\\s*\\{{";
            var match = Regex.Match(json, pattern);
            if (!match.Success)
                return null;

            var start = match.Index + match.Value.Length - 1;
            if (!TryReadJsonValue(json, start, out _, out var token))
                return null;

            return token;
        }

        static string ExtractJsonStringValue(string json, string key) {
            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!match.Success)
                return null;
            return Regex.Unescape(match.Groups[1].Value);
        }

        static int ExtractJsonIntValue(string json, params string[] keys) {
            if (string.IsNullOrWhiteSpace(json) || keys == null)
                return 0;

            for (var i = 0; i < keys.Length; i++) {
                if (string.IsNullOrWhiteSpace(keys[i]))
                    continue;

                var match = Regex.Match(
                    json,
                    $"\"{Regex.Escape(keys[i])}\"\\s*:\\s*(?:\"(?<quoted>\\d+)\"|(?<number>\\d+))",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                string value = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["number"].Value;
                if (int.TryParse(value, out var parsedValue))
                    return parsedValue;
            }

            return 0;
        }

        static bool ReadJsonBoolean(string json, string key) {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*(true|false)",
                RegexOptions.IgnoreCase);
            return match.Success && string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        static string ExtractNestedString(string json, string key) {
            return ExtractJsonStringValue(json, key);
        }

        static int SkipWhitespace(string text, int index) {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            return index;
        }

        static IEnumerable<string> SplitCsvLogicalLines(string csv) {
            if (string.IsNullOrEmpty(csv))
                yield break;

            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < csv.Length; i++) {
                var c = csv[i];
                if (c == '"') {
                    if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"') {
                        sb.Append("\"\"");
                        i++;
                    } else {
                        inQuotes = !inQuotes;
                        sb.Append(c);
                    }

                    continue;
                }

                if ((c == '\n' || c == '\r') && !inQuotes) {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                        i++;
                    var line = sb.ToString();
                    if (!string.IsNullOrEmpty(line))
                        yield return line;
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }

        static string[] ParseCsvLine(string line) {
            var values = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++) {
                var c = line[i];
                if (c == '"') {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') {
                        sb.Append('"');
                        i++;
                    } else {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes) {
                    values.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            values.Add(sb.ToString());
            return values.ToArray();
        }
    }
}
#endif
