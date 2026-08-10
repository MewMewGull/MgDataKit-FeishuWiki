using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
public sealed class MgDataKitBatchImportWindow : EditorWindow {
    private const string DefaultMatchPattern = "^(.*)$";
    private const string DefaultReplacementPattern = "$1";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100d);
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase) {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly List<Type> _assetTypes = new();
    private readonly List<ParentDocumentOption> _parentOptions = new();
    private readonly List<PreviewEntry> _previewEntries = new();
    private string[] _assetTypeNames = Array.Empty<string>();
    private string[] _parentOptionNames = Array.Empty<string>();
    private int _selectedTypeIndex = -1;
    private int _selectedParentIndex = -1;
    private DefaultAsset _outputFolder;
    private string _matchPattern = DefaultMatchPattern;
    private string _replacementPattern = DefaultReplacementPattern;
    private bool _importAfterCreate = true;
    private Vector2 _previewScrollPosition;
    private string _loadError;
    private string _previewError;

    private Type SelectedType =>
        _selectedTypeIndex >= 0 && _selectedTypeIndex < _assetTypes.Count
            ? _assetTypes[_selectedTypeIndex]
            : null;

    private ParentDocumentOption SelectedParent =>
        _selectedParentIndex >= 0 && _selectedParentIndex < _parentOptions.Count
            ? _parentOptions[_selectedParentIndex]
            : null;

    public static void Open(Type initialType, string defaultOutputFolder) {
        MgDataKitBatchImportWindow window = GetWindow<MgDataKitBatchImportWindow>(false, "MgData 批量导入");
        window.minSize = new Vector2(760f, 520f);
        window.Initialize(initialType, defaultOutputFolder);
        window.Show();
        window.Focus();
    }

    private void OnEnable() {
        EditorApplication.projectChanged += HandleProjectChanged;
        ReloadOptions(SelectedType, null, false);
        if (_outputFolder == null)
            _outputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data");
        RebuildPreview();
    }

    private void OnDisable() {
        EditorApplication.projectChanged -= HandleProjectChanged;
    }

    private void Initialize(Type initialType, string defaultOutputFolder) {
        _outputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(defaultOutputFolder);
        if (_outputFolder == null)
            _outputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data");

        ReloadOptions(initialType, null, true);
        RebuildPreview();
        Repaint();
    }

    private void HandleProjectChanged() {
        Type selectedType = SelectedType;
        string parentToken = SelectedParent?.NodeToken;
        ReloadOptions(selectedType, parentToken, false);
        RebuildPreview();
        Repaint();
    }

    private void OnGUI() {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("MgData 批量导入", EditorStyles.boldLabel);
        DrawConfiguration();
        GUILayout.Space(8f);
        DrawPreview();
        DrawActions();
    }

    private void DrawConfiguration() {
        if (!string.IsNullOrWhiteSpace(_loadError))
            EditorGUILayout.HelpBox(_loadError, MessageType.Error);

        EditorGUI.BeginChangeCheck();
        int selectedTypeIndex = EditorGUILayout.Popup("归属类", _selectedTypeIndex, _assetTypeNames);
        if (EditorGUI.EndChangeCheck()) {
            _selectedTypeIndex = selectedTypeIndex;
            SelectLikelyParent();
            RebuildPreview();
        }

        EditorGUI.BeginChangeCheck();
        int selectedParentIndex = EditorGUILayout.Popup("父文档", _selectedParentIndex, _parentOptionNames);
        if (EditorGUI.EndChangeCheck()) {
            _selectedParentIndex = selectedParentIndex;
            RebuildPreview();
        }

        EditorGUI.BeginChangeCheck();
        DefaultAsset outputFolder = EditorGUILayout.ObjectField(
            "Asset 输出目录",
            _outputFolder,
            typeof(DefaultAsset),
            false) as DefaultAsset;
        if (EditorGUI.EndChangeCheck()) {
            _outputFolder = outputFolder;
            RebuildPreview();
        }

        EditorGUI.BeginChangeCheck();
        _matchPattern = EditorGUILayout.TextField(
            new GUIContent("匹配正则", "使用 .NET 正则表达式匹配子表名称。"),
            _matchPattern ?? string.Empty);
        _replacementPattern = EditorGUILayout.TextField(
            new GUIContent("命名格式", "支持 $0、$1 和 ${name} 捕获组替换。"),
            _replacementPattern ?? string.Empty);
        if (EditorGUI.EndChangeCheck())
            RebuildPreview();

        _importAfterCreate = EditorGUILayout.Toggle("创建后立即从飞书导入", _importAfterCreate);
    }

    private void DrawPreview() {
        int createCount = _previewEntries.Count(entry => entry.State == PreviewState.Create);
        int boundCount = _previewEntries.Count(entry => entry.State == PreviewState.AlreadyBound);
        int errorCount = _previewEntries.Count(entry => entry.State == PreviewState.Error);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
            EditorGUILayout.LabelField(
                $"结果预览  子表 {_previewEntries.Count}  新建 {createCount}  已绑定 {boundCount}  问题 {errorCount}",
                EditorStyles.miniBoldLabel);
        }

        if (!string.IsNullOrWhiteSpace(_previewError))
            EditorGUILayout.HelpBox(_previewError, MessageType.Error);
        else if (errorCount > 0)
            EditorGUILayout.HelpBox("预览中存在无法执行的项目，请调整正则、命名或输出目录。", MessageType.Error);

        float contentWidth = Mathf.Max(640f, position.width - 34f);
        float sourceWidth = contentWidth * 0.34f;
        float assetWidth = contentWidth * 0.36f;
        float statusWidth = contentWidth - sourceWidth - assetWidth;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
            EditorGUILayout.LabelField("子表", EditorStyles.miniBoldLabel, GUILayout.Width(sourceWidth));
            EditorGUILayout.LabelField("格式化后的 Asset 名称", EditorStyles.miniBoldLabel, GUILayout.Width(assetWidth));
            EditorGUILayout.LabelField("状态", EditorStyles.miniBoldLabel, GUILayout.Width(statusWidth));
        }

        using (EditorGUILayout.ScrollViewScope scroll = new(
                   _previewScrollPosition,
                   GUILayout.ExpandHeight(true))) {
            _previewScrollPosition = scroll.scrollPosition;
            GUIStyle errorStatusStyle = new(EditorStyles.label);
            errorStatusStyle.normal.textColor = new Color(0.9f, 0.32f, 0.28f);
            for (var i = 0; i < _previewEntries.Count; i++) {
                PreviewEntry entry = _previewEntries[i];
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(
                        new GUIContent(entry.SourceTitle, entry.Source),
                        GUILayout.Width(sourceWidth));
                    EditorGUILayout.LabelField(
                        new GUIContent(entry.AssetName, entry.AssetPath),
                        GUILayout.Width(assetWidth));

                    EditorGUILayout.LabelField(
                        new GUIContent(entry.Status, entry.Status),
                        entry.State == PreviewState.Error ? errorStatusStyle : EditorStyles.label,
                        GUILayout.Width(statusWidth));
                }
            }
        }
    }

    private void DrawActions() {
        int createCount = _previewEntries.Count(entry => entry.State == PreviewState.Create);
        int targetCount = _previewEntries.Count(entry => entry.State != PreviewState.Error);
        bool hasErrors = !string.IsNullOrWhiteSpace(_previewError) ||
                         _previewEntries.Any(entry => entry.State == PreviewState.Error);
        bool hasWork = createCount > 0 || (_importAfterCreate && targetCount > 0);

        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("重新读取目录快照", GUILayout.Height(28f))) {
                Type selectedType = SelectedType;
                string parentToken = SelectedParent?.NodeToken;
                ReloadOptions(selectedType, parentToken, false);
                RebuildPreview();
            }

            using (new EditorGUI.DisabledScope(hasErrors || !hasWork)) {
                if (GUILayout.Button("执行批量导入", GUILayout.Height(28f)))
                    ExecuteBatchImport();
            }
        }
    }

    private void ReloadOptions(Type preferredType, string preferredParentToken, bool selectLikelyParent) {
        _loadError = null;
        _assetTypes.Clear();
        _parentOptions.Clear();

        if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                out MgDataKitAssetCatalog assetCatalog,
                out string assetCatalogError)) {
            _loadError = assetCatalogError;
        } else {
            for (var i = 0; i < assetCatalog.Entries.Count; i++) {
                Type assetType = assetCatalog.Entries[i]?.AssetType;
                if (assetType != null && !assetType.IsAbstract)
                    _assetTypes.Add(assetType);
            }

            _assetTypes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        }

        _assetTypeNames = _assetTypes.Select(type => type.Name).ToArray();
        _selectedTypeIndex = preferredType != null ? _assetTypes.IndexOf(preferredType) : -1;
        if (_selectedTypeIndex < 0 && _assetTypes.Count > 0)
            _selectedTypeIndex = 0;

        if (!MgDataKitLarkTableCatalogProvider.TryGet(
                out MgDataKitLarkTableCatalog larkCatalog,
                out string larkCatalogError)) {
            _loadError = string.IsNullOrWhiteSpace(_loadError)
                ? larkCatalogError
                : _loadError + "\n" + larkCatalogError;
        } else {
            BuildParentOptions(larkCatalog);
        }

        _parentOptionNames = _parentOptions.Select(option => option.DisplayName).ToArray();
        _selectedParentIndex = FindParentIndex(preferredParentToken);
        if (_selectedParentIndex < 0 && _parentOptions.Count > 0)
            _selectedParentIndex = 0;
        if (selectLikelyParent || string.IsNullOrWhiteSpace(preferredParentToken))
            SelectLikelyParent();
    }

    private void BuildParentOptions(MgDataKitLarkTableCatalog catalog) {
        var sheetsByParent = new Dictionary<string, List<MgDataKitLarkTableNode>>(StringComparer.Ordinal);
        for (var i = 0; i < catalog.Nodes.Count; i++) {
            MgDataKitLarkTableNode node = catalog.Nodes[i];
            if (node == null || !node.IsSheet)
                continue;

            string parentToken = node.ParentNodeToken ?? string.Empty;
            if (!sheetsByParent.TryGetValue(parentToken, out List<MgDataKitLarkTableNode> childSheets)) {
                childSheets = new List<MgDataKitLarkTableNode>();
                sheetsByParent.Add(parentToken, childSheets);
            }
            childSheets.Add(node);
        }

        string rootToken = catalog.RootNodeToken ?? string.Empty;
        if (sheetsByParent.TryGetValue(rootToken, out List<MgDataKitLarkTableNode> rootSheets)) {
            _parentOptions.Add(new ParentDocumentOption(
                rootToken,
                $"(目录根节点) ({rootSheets.Count} 张表)",
                rootSheets));
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
                hierarchyTitles.Add("(未知节点)");

            string title = string.IsNullOrWhiteSpace(node.Title) ? node.NodeToken : node.Title;
            var pathParts = new List<string>(hierarchyTitles) { title };
            string hierarchyPath = string.Join(" / ", pathParts);

            if (!node.IsSheet &&
                sheetsByParent.TryGetValue(node.NodeToken ?? string.Empty, out List<MgDataKitLarkTableNode> sheets)) {
                _parentOptions.Add(new ParentDocumentOption(
                    node.NodeToken,
                    $"{hierarchyPath} ({sheets.Count} 张表)",
                    sheets));
            }

            hierarchyTitles.Add(title);
        }
    }

    private int FindParentIndex(string parentToken) {
        if (string.IsNullOrWhiteSpace(parentToken))
            return -1;

        for (var i = 0; i < _parentOptions.Count; i++) {
            if (string.Equals(_parentOptions[i].NodeToken, parentToken, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void SelectLikelyParent() {
        Type selectedType = SelectedType;
        if (selectedType == null || _parentOptions.Count == 0)
            return;

        var boundSources = new HashSet<string>(StringComparer.Ordinal);
        List<MgDataKitAssetEntry> entries = MgDataKitAssetCatalogProvider.GetEntries(selectedType);
        for (var i = 0; i < entries.Count; i++) {
            string source = NormalizeFeishuSource(FeishuBindingUtility.Read(entries[i]).source);
            if (!string.IsNullOrWhiteSpace(source))
                boundSources.Add(source);
        }

        int bestIndex = 0;
        int bestScore = -1;
        for (var parentIndex = 0; parentIndex < _parentOptions.Count; parentIndex++) {
            ParentDocumentOption option = _parentOptions[parentIndex];
            int boundCount = 0;
            int prefixCount = 0;
            for (var sheetIndex = 0; sheetIndex < option.ChildSheets.Count; sheetIndex++) {
                MgDataKitLarkTableNode sheet = option.ChildSheets[sheetIndex];
                if (boundSources.Contains(NormalizeFeishuSource(sheet.Source)))
                    boundCount++;
                if (!string.IsNullOrWhiteSpace(sheet.Title) &&
                    sheet.Title.StartsWith(selectedType.Name, StringComparison.OrdinalIgnoreCase))
                    prefixCount++;
            }

            int score = boundCount * 1000 + prefixCount;
            if (score > bestScore) {
                bestScore = score;
                bestIndex = parentIndex;
            }
        }

        _selectedParentIndex = bestIndex;
    }

    private void RebuildPreview() {
        _previewEntries.Clear();
        _previewError = null;

        if (!string.IsNullOrWhiteSpace(_loadError))
            return;

        Type selectedType = SelectedType;
        if (selectedType == null) {
            _previewError = "请选择 MgDataBase 子类。";
            return;
        }

        if (!string.Equals(MgDataKitAssetCatalogProvider.GetSourceId(selectedType), "feishu", StringComparison.OrdinalIgnoreCase)) {
            _previewError = $"{selectedType.Name} 当前不是飞书数据源，请先在标签与类型配置中切换来源。";
            return;
        }

        ParentDocumentOption selectedParent = SelectedParent;
        if (selectedParent == null) {
            _previewError = "当前飞书目录快照中没有包含直属电子表格的父文档。";
            return;
        }

        string outputFolderPath = GetOutputFolderPath();
        if (string.IsNullOrWhiteSpace(outputFolderPath)) {
            _previewError = "请选择 Assets 目录内的有效输出文件夹。";
            return;
        }

        Regex namingRegex;
        try {
            namingRegex = new Regex(_matchPattern ?? string.Empty, RegexOptions.None, RegexTimeout);
        } catch (ArgumentException ex) {
            _previewError = $"正则表达式无效：{ex.Message}";
            return;
        }

        Dictionary<string, List<MgDataBase>> existingAssetsBySource = BuildExistingAssetsBySource(selectedType);
        for (var i = 0; i < selectedParent.ChildSheets.Count; i++) {
            MgDataKitLarkTableNode sheet = selectedParent.ChildSheets[i];
            _previewEntries.Add(BuildPreviewEntry(
                sheet,
                outputFolderPath,
                namingRegex,
                existingAssetsBySource));
        }

        MarkDuplicateAssetNames();
    }

    private PreviewEntry BuildPreviewEntry(
        MgDataKitLarkTableNode sheet,
        string outputFolderPath,
        Regex namingRegex,
        IReadOnlyDictionary<string, List<MgDataBase>> existingAssetsBySource) {
        string sourceTitle = string.IsNullOrWhiteSpace(sheet.Title) ? sheet.NodeToken : sheet.Title;
        string source = sheet.Source;
        string assetName;
        try {
            Match match = namingRegex.Match(sourceTitle);
            if (!match.Success)
                return PreviewEntry.Error(sheet, sourceTitle, string.Empty, string.Empty, "正则未匹配");
            assetName = match.Result(_replacementPattern ?? string.Empty);
        } catch (RegexMatchTimeoutException) {
            return PreviewEntry.Error(sheet, sourceTitle, string.Empty, string.Empty, "正则匹配超时");
        } catch (ArgumentException ex) {
            return PreviewEntry.Error(sheet, sourceTitle, string.Empty, string.Empty, $"命名格式无效: {ex.Message}");
        }

        string assetPath = $"{outputFolderPath}/{assetName}.asset";
        string normalizedSource = NormalizeFeishuSource(source);
        if (existingAssetsBySource.TryGetValue(
                normalizedSource,
                out List<MgDataBase> existingAssets)) {
            if (existingAssets.Count > 1) {
                return PreviewEntry.Error(
                    sheet,
                    sourceTitle,
                    assetName,
                    assetPath,
                    $"来源已被 {existingAssets.Count} 个 Asset 重复绑定");
            }

            MgDataBase existingAsset = existingAssets[0];
            return PreviewEntry.AlreadyBound(sheet, sourceTitle, assetName, assetPath, existingAsset);
        }

        if (!TryValidateAssetName(assetName, out string nameError))
            return PreviewEntry.Error(sheet, sourceTitle, assetName, assetPath, nameError);

        UnityEngine.Object pathAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (pathAsset != null)
            return PreviewEntry.Error(sheet, sourceTitle, assetName, assetPath, $"路径已存在: {pathAsset.name}");
        string absoluteAssetPath = GetAbsoluteProjectPath(assetPath);
        if (File.Exists(absoluteAssetPath) || Directory.Exists(absoluteAssetPath))
            return PreviewEntry.Error(sheet, sourceTitle, assetName, assetPath, "路径已存在但 Unity 无法加载");

        return PreviewEntry.Create(sheet, sourceTitle, assetName, assetPath);
    }

    private static Dictionary<string, List<MgDataBase>> BuildExistingAssetsBySource(Type selectedType) {
        var result = new Dictionary<string, List<MgDataBase>>(StringComparer.Ordinal);
        List<MgDataKitAssetEntry> entries = MgDataKitAssetCatalogProvider.GetEntries(selectedType);
        for (var i = 0; i < entries.Count; i++) {
            MgDataBase asset = entries[i]?.Asset;
            string source = NormalizeFeishuSource(FeishuBindingUtility.Read(entries[i]).source);
            if (asset == null || string.IsNullOrWhiteSpace(source))
                continue;

            if (!result.TryGetValue(source, out List<MgDataBase> assets)) {
                assets = new List<MgDataBase>();
                result.Add(source, assets);
            }
            assets.Add(asset);
        }

        return result;
    }

    private void MarkDuplicateAssetNames() {
        var entriesByPath = new Dictionary<string, List<PreviewEntry>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _previewEntries.Count; i++) {
            PreviewEntry entry = _previewEntries[i];
            if (entry.State != PreviewState.Create)
                continue;

            if (!entriesByPath.TryGetValue(entry.AssetPath, out List<PreviewEntry> entries)) {
                entries = new List<PreviewEntry>();
                entriesByPath.Add(entry.AssetPath, entries);
            }
            entries.Add(entry);
        }

        foreach (List<PreviewEntry> entries in entriesByPath.Values) {
            if (entries.Count < 2)
                continue;

            for (var i = 0; i < entries.Count; i++)
                entries[i].SetError("格式化后名称重复");
        }
    }

    private string GetOutputFolderPath() {
        if (_outputFolder == null)
            return null;

        string path = AssetDatabase.GetAssetPath(_outputFolder)?.Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(path) &&
               (string.Equals(path, "Assets", StringComparison.Ordinal) ||
                path.StartsWith("Assets/", StringComparison.Ordinal)) &&
               AssetDatabase.IsValidFolder(path)
            ? path.TrimEnd('/')
            : null;
    }

    private static string GetAbsoluteProjectPath(string projectRelativePath) {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
    }

    private static bool TryValidateAssetName(string assetName, out string error) {
        error = null;
        if (string.IsNullOrWhiteSpace(assetName)) {
            error = "Asset 名称为空";
            return false;
        }

        if (!string.Equals(assetName, assetName.Trim(), StringComparison.Ordinal)) {
            error = "名称首尾不能有空白";
            return false;
        }

        if (assetName.EndsWith(".", StringComparison.Ordinal)) {
            error = "名称不能以句点结尾";
            return false;
        }

        if (assetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            assetName.IndexOf('/') >= 0 ||
            assetName.IndexOf('\\') >= 0) {
            error = "名称包含非法文件名字符";
            return false;
        }

        string reservedStem = assetName.Split('.')[0];
        if (ReservedFileNames.Contains(reservedStem)) {
            error = "名称是系统保留文件名";
            return false;
        }

        return true;
    }

    private static string NormalizeFeishuSource(string source) {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        string normalized = source.Trim();
        int suffixIndex = normalized.IndexOfAny(new[] { '?', '#' });
        if (suffixIndex >= 0)
            normalized = normalized.Substring(0, suffixIndex);
        normalized = normalized.TrimEnd('/');

        int wikiIndex = normalized.IndexOf("/wiki/", StringComparison.OrdinalIgnoreCase);
        if (wikiIndex >= 0)
            return "wiki/" + normalized.Substring(wikiIndex + "/wiki/".Length);
        if (normalized.StartsWith("wiki/", StringComparison.OrdinalIgnoreCase))
            return "wiki/" + normalized.Substring("wiki/".Length);
        return normalized;
    }

    private void ExecuteBatchImport() {
        RebuildPreview();
        int createCount = _previewEntries.Count(entry => entry.State == PreviewState.Create);
        int targetCount = _previewEntries.Count(entry => entry.State != PreviewState.Error);
        if (!string.IsNullOrWhiteSpace(_previewError) ||
            _previewEntries.Any(entry => entry.State == PreviewState.Error) ||
            (createCount == 0 && (!_importAfterCreate || targetCount == 0)))
            return;

        string operationSummary = _importAfterCreate
            ? $"将创建并绑定 {createCount} 个 Asset，并从飞书导入 {targetCount} 个 Asset。"
            : $"将创建并绑定 {createCount} 个 Asset。";
        if (!EditorUtility.DisplayDialog("确认批量导入", operationSummary, "执行", "取消"))
            return;

        if (_importAfterCreate && !MgDataFeishuSyncService.TryPreflightUserAuth(out string authError)) {
            Debug.LogError($"[MgDataKit] 批量导入预检失败。\n{authError}");
            LarkCliMessageWindow.Show("MgDataKit 批量导入", authError);
            return;
        }

        if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                out MgDataKitAssetCatalog catalog,
                out string catalogError)) {
            EditorUtility.DisplayDialog("批量导入失败", catalogError, "确定");
            return;
        }

        Type selectedType = SelectedType;
        var targetAssets = new List<MgDataBase>();
        var createdAssets = new List<MgDataBase>();
        var createdAssetPaths = new List<string>();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("MgData 批量导入");

        try {
            if (createCount > 0)
                Undo.RecordObject(catalog, "MgData 批量注册 Asset");

            for (var i = 0; i < _previewEntries.Count; i++) {
                PreviewEntry preview = _previewEntries[i];
                if (preview.State == PreviewState.AlreadyBound) {
                    targetAssets.Add(preview.ExistingAsset);
                    continue;
                }
                if (preview.State != PreviewState.Create)
                    continue;

                MgDataBase asset = CreateInstance(selectedType) as MgDataBase;
                if (asset == null)
                    throw new InvalidOperationException($"无法创建 {selectedType.Name} 实例。");

                asset.name = preview.AssetName;
                AssetDatabase.CreateAsset(asset, preview.AssetPath);
                createdAssetPaths.Add(preview.AssetPath);
                Undo.RegisterCreatedObjectUndo(asset, $"创建 {preview.AssetName}");

                MgDataKitAssetEntry catalogEntry = catalog.AddEntry(asset);
                if (catalogEntry == null)
                    throw new InvalidOperationException($"无法将 {preview.AssetName} 注册到 Asset Catalog。");

                FeishuBindingUtility.Write(
                    catalogEntry,
                    new MgDataFeishuBinding { source = preview.Source });

                EditorUtility.SetDirty(asset);
                createdAssets.Add(asset);
                targetAssets.Add(asset);
            }

            MgDataKitAssetCatalogProvider.Save(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Undo.CollapseUndoOperations(undoGroup);
        } catch (Exception ex) {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(undoGroup);
            for (var i = 0; i < createdAssetPaths.Count; i++) {
                string createdPath = createdAssetPaths[i];
                if (AssetDatabase.LoadMainAssetAtPath(createdPath) != null)
                    AssetDatabase.DeleteAsset(createdPath);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.LogError($"[MgDataKit] 批量创建 Asset 失败，已回滚本次创建。\n{ex}");
            EditorUtility.DisplayDialog("批量导入失败", ex.Message, "确定");
            RebuildPreview();
            return;
        }

        bool importSucceeded = true;
        string importError = null;
        if (_importAfterCreate)
            importSucceeded = MgDataFeishuSyncService.TrySyncFeishuAssets(targetAssets, out importError);

        MgDataKitEditor.RepaintOpenWindows();
        RebuildPreview();
        if (createdAssets.Count > 0) {
            Selection.activeObject = createdAssets[createdAssets.Count - 1];
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        if (!importSucceeded) {
            Debug.LogError($"[MgDataKit] 批量 Asset 创建完成，但飞书导入存在失败项。\n{importError}");
            LarkCliMessageWindow.Show(
                "MgDataKit 批量导入",
                $"已创建并绑定 {createdAssets.Count} 个 Asset，但飞书导入存在失败项：\n\n{importError}");
            return;
        }

        string completedMessage = _importAfterCreate
            ? $"已创建并绑定 {createdAssets.Count} 个 Asset，飞书导入 {targetAssets.Count} 个 Asset。"
            : $"已创建并绑定 {createdAssets.Count} 个 Asset。";
        Debug.Log($"[MgDataKit] {completedMessage}");
        EditorUtility.DisplayDialog("批量导入完成", completedMessage, "确定");
    }

    private sealed class ParentDocumentOption {
        public readonly string NodeToken;
        public readonly string DisplayName;
        public readonly IReadOnlyList<MgDataKitLarkTableNode> ChildSheets;

        public ParentDocumentOption(
            string nodeToken,
            string displayName,
            IReadOnlyList<MgDataKitLarkTableNode> childSheets) {
            NodeToken = nodeToken;
            DisplayName = displayName;
            ChildSheets = childSheets;
        }
    }

    private enum PreviewState {
        Create,
        AlreadyBound,
        Error
    }

    private sealed class PreviewEntry {
        public readonly MgDataKitLarkTableNode Sheet;
        public readonly string SourceTitle;
        public readonly string AssetName;
        public readonly string AssetPath;
        public readonly MgDataBase ExistingAsset;
        public string Source => Sheet.Source;
        public PreviewState State { get; private set; }
        public string Status { get; private set; }

        private PreviewEntry(
            MgDataKitLarkTableNode sheet,
            string sourceTitle,
            string assetName,
            string assetPath,
            MgDataBase existingAsset,
            PreviewState state,
            string status) {
            Sheet = sheet;
            SourceTitle = sourceTitle;
            AssetName = assetName;
            AssetPath = assetPath;
            ExistingAsset = existingAsset;
            State = state;
            Status = status;
        }

        public static PreviewEntry Create(
            MgDataKitLarkTableNode sheet,
            string sourceTitle,
            string assetName,
            string assetPath) {
            return new PreviewEntry(
                sheet,
                sourceTitle,
                assetName,
                assetPath,
                null,
                PreviewState.Create,
                "将创建");
        }

        public static PreviewEntry AlreadyBound(
            MgDataKitLarkTableNode sheet,
            string sourceTitle,
            string assetName,
            string assetPath,
            MgDataBase existingAsset) {
            return new PreviewEntry(
                sheet,
                sourceTitle,
                assetName,
                assetPath,
                existingAsset,
                PreviewState.AlreadyBound,
                $"已绑定: {existingAsset.name}");
        }

        public static PreviewEntry Error(
            MgDataKitLarkTableNode sheet,
            string sourceTitle,
            string assetName,
            string assetPath,
            string status) {
            return new PreviewEntry(
                sheet,
                sourceTitle,
                assetName,
                assetPath,
                null,
                PreviewState.Error,
                status);
        }

        public void SetError(string status) {
            State = PreviewState.Error;
            Status = status;
        }
    }
}
}
