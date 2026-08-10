#if UNITY_EDITOR
using System.Text.RegularExpressions;
using MgDataKit.Editor;
using UnityEditor;
using UnityEngine;

public class LarkProjectConfigEditorWindow : EditorWindow {
    LarkProjectConfigData _config = new LarkProjectConfigData();
    bool _showSecret;
    string _statusMessage;

    [MenuItem(FeishuEditorMenu.LarkCli.ProjectConfig, false, 1)]
    public static void Open() {
        var window = GetWindow<LarkProjectConfigEditorWindow>(false, "项目飞书应用配置", true);
        window.minSize = new Vector2(520f, 560f);
        window.Show();
    }

    void OnEnable() {
        if (LarkProjectConfigStore.TryLoad(out var loaded, out _))
            _config = loaded;
        else
            _config = new LarkProjectConfigData();
    }

    void OnGUI() {
        EditorGUILayout.LabelField("项目飞书应用配置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "此资产保存在宿主项目的 Assets/MgDataKit/Project 目录，位于 MgDataKit Core 仓库之外。\n" +
            "请按项目的版本控制与凭据管理规范处理 App ID / Secret / Wiki；用户令牌仅保存在本机。",
            MessageType.Info);

        EditorGUILayout.Space(6f);
        _config.appId = EditorGUILayout.TextField("App ID", _config.appId ?? string.Empty);

        EditorGUILayout.BeginHorizontal();
        _config.appSecret = EditorGUILayout.PasswordField("App Secret", _config.appSecret ?? string.Empty);
        _showSecret = GUILayout.Toggle(_showSecret, "显示", GUILayout.Width(52f));
        EditorGUILayout.EndHorizontal();
        if (_showSecret && !string.IsNullOrEmpty(_config.appSecret))
            EditorGUILayout.TextField("App Secret（明文）", _config.appSecret);

        _config.brand = EditorGUILayout.TextField("Brand（feishu / lark）", _config.brand ?? LarkProjectConfigData.DefaultBrand);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("飞书 Wiki 配置", EditorStyles.boldLabel);
        _config.wikiHost = EditorGUILayout.TextField("Wiki Host", _config.wikiHost ?? LarkProjectConfigData.DefaultWikiHost);
        _config.wikiSpaceId = EditorGUILayout.TextField(
            "Wiki Space ID",
            _config.wikiSpaceId ?? LarkProjectConfigData.DefaultWikiSpaceId);
        _config.wikiParentNodeToken = EditorGUILayout.TextField(
            "Wiki Parent Token",
            _config.wikiParentNodeToken ?? LarkProjectConfigData.DefaultWikiParentNodeToken);
        _config.playBeforeFeishuSyncEnabled = EditorGUILayout.ToggleLeft(
            "Play 前自动同步飞书数据",
            _config.playBeforeFeishuSyncEnabled);

        DrawLocalPlayBeforeSyncOverride();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("资产路径", LarkProjectConfigStore.GetConfigPath(), EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("保存到项目", GUILayout.Height(28f)))
            SaveConfig();

        if (GUILayout.Button("从本机 CLI 读取 App ID", GUILayout.Height(28f)))
            LoadAppIdFromLocalCli();
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_statusMessage))
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
    }

    static void DrawLocalPlayBeforeSyncOverride() {
        FeishuUserPreferencesData preferences = FeishuUserPreferencesStore.Data;
        bool followsProject = !preferences.HasPlayBeforeFeishuSyncOverride;
        bool effectiveValue = FeishuUserPreferencesStore.GetPlayBeforeFeishuSyncEnabled();
        bool newFollowsProject = EditorGUILayout.ToggleLeft(
            "本机使用项目默认：Play 前自动同步",
            followsProject);
        if (newFollowsProject != followsProject) {
            FeishuUserPreferencesStore.SetPlayBeforeFeishuSyncOverride(
                newFollowsProject ? null : effectiveValue);
            followsProject = newFollowsProject;
        }

        EditorGUI.BeginDisabledGroup(followsProject);
        bool newValue = EditorGUILayout.ToggleLeft(
            "本机值：Play 前自动同步",
            effectiveValue);
        EditorGUI.EndDisabledGroup();
        if (!followsProject && newValue != effectiveValue)
            FeishuUserPreferencesStore.SetPlayBeforeFeishuSyncOverride(newValue);

        if (!followsProject && GUILayout.Button("清除本机同步覆盖"))
            FeishuUserPreferencesStore.SetPlayBeforeFeishuSyncOverride(null);
    }

    void SaveConfig() {
        _statusMessage = null;
        if (!LarkProjectConfigStore.TrySave(_config, out var error)) {
            _statusMessage = error;
            EditorUtility.DisplayDialog("保存失败", error, "确定");
            return;
        }

        AssetDatabase.Refresh();
        _statusMessage = "已保存到宿主项目的本地配置资产。请按项目的版本控制与凭据管理规范处理该文件。";
        ShowNotification(new GUIContent("已保存"));
    }

    void LoadAppIdFromLocalCli() {
        _statusMessage = null;
        if (!LarkCliProcessRunner.TryRun("config show", out var result) || !result.Success) {
            _statusMessage = result != null ? result.CombinedOutput : "无法读取本机 CLI 配置。";
            return;
        }

        var match = Regex.Match(result.CombinedOutput, "\"appId\"\\s*:\\s*\"([^\"]+)\"");
        if (!match.Success) {
            _statusMessage = "本机 CLI 输出中未找到 appId，请先完成 config init。";
            return;
        }

        _config.appId = match.Groups[1].Value;
        _statusMessage = $"已从本机 CLI 填入 App ID：{_config.appId}。App Secret 仍需手动填写后保存。";
    }
}
#endif
