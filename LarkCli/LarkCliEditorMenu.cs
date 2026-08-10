#if UNITY_EDITOR
using MgDataKit.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目内置 Lark CLI 的编辑器菜单入口；后续 MgDataKit 相关能力可在此扩展。
/// </summary>
public static class LarkCliEditorMenu {
    [MenuItem(FeishuEditorMenu.LarkCli.CheckInstallation, false, 0)]
    public static void CheckInstallation() {
        if (!LarkCliPathResolver.TryResolve(out var executablePath, out var error)) {
            EditorUtility.DisplayDialog("Lark CLI", error, "确定");
            return;
        }

        if (!LarkCliProcessRunner.TryRun("--version", out var result) || !result.Success) {
            var message = result != null
                ? LarkCliAuthHelper.FormatFailureMessage(result, "检查内置 CLI")
                : "无法启动 Lark CLI。";
            EditorUtility.DisplayDialog("Lark CLI", message, "确定");
            return;
        }

        var projectConfigHint = LarkProjectConfigStore.Exists()
            ? "已找到项目 LarkProjectConfig.asset。"
            : "尚未配置 LarkProjectConfig.asset，请打开项目飞书应用配置进行创建。";

        var bundledVersion = LarkCliPathResolver.ReadBundledVersion();
        var successMessage =
            $"内置 Lark CLI 可用。\n\n" +
            $"路径：{executablePath}\n" +
            $"版本文件：{bundledVersion}\n" +
            $"{projectConfigHint}\n\n" +
            $"运行结果：{result.CombinedOutput.Trim()}";
        EditorUtility.DisplayDialog("Lark CLI", successMessage, "确定");
    }

    [MenuItem(FeishuEditorMenu.LarkCli.ApplyProjectConfig, false, 2)]
    public static void ApplyProjectConfig() {
        if (!LarkProjectConfigStore.TryLoad(out var config, out var loadError)) {
            EditorUtility.DisplayDialog("应用项目配置", loadError, "确定");
            return;
        }

        var confirm = EditorUtility.DisplayDialog(
            "应用项目配置到本机",
            $"将把项目中的飞书应用写入本机 lark-cli（profile: {LarkProjectConfig.DefaultProfileName}）。\n\n" +
            $"App ID：{config.appId}\n" +
            $"Brand：{config.brand}\n\n" +
            "此操作只配置应用凭证，不会覆盖其他成员已登录的个人账号。",
            "应用",
            "取消");
        if (!confirm)
            return;

        if (!LarkCliLocalBootstrap.TryApplyProjectConfig(out var result) || !result.Success) {
            var message = result != null
                ? LarkCliAuthHelper.FormatFailureMessage(result, "应用项目配置")
                : "无法启动 Lark CLI。";
            EditorUtility.DisplayDialog("应用项目配置失败", message, "确定");
            return;
        }

        EditorUtility.DisplayDialog(
            "应用项目配置",
            "项目飞书应用已写入本机。\n请执行「登录飞书账号」完成个人授权。",
            "确定");
    }

    [MenuItem(FeishuEditorMenu.LarkCli.ApplyProjectConfig, true)]
    public static bool ApplyProjectConfigValidate() {
        return LarkProjectConfigStore.Exists();
    }

    [MenuItem(FeishuEditorMenu.LarkCli.AuthStatus, false, 3)]
    public static void ShowAuthStatus() {
        if (!LarkCliProcessRunner.TryRun("auth status", out var result) || !result.Success) {
            var message = result != null
                ? LarkCliAuthHelper.FormatFailureMessage(result, "查询登录状态")
                : "无法启动 Lark CLI。";
            Debug.Log($"[Lark CLI] 登录状态查询失败。\n{message}");
            LarkCliMessageWindow.Show("Lark CLI 登录状态", message);
            return;
        }

        if (!LarkCliAuthStatusFormatter.TryFormat(result.CombinedOutput, out var summary, out var parseError)) {
            var fallback = string.IsNullOrEmpty(parseError)
                ? result.CombinedOutput.Trim()
                : $"{parseError}\n\n{result.CombinedOutput.Trim()}";
            Debug.Log($"[Lark CLI] 登录状态（原始输出）\n{result.CombinedOutput}");
            LarkCliMessageWindow.Show("Lark CLI 登录状态", fallback);
            return;
        }

        Debug.Log($"[Lark CLI] 登录状态\n{summary}");
        LarkCliMessageWindow.Show("Lark CLI 登录状态", summary);
    }

    [MenuItem(FeishuEditorMenu.LarkCli.AuthLogin, false, 4)]
    public static void Login() {
        var projectHint = LarkProjectConfigStore.Exists()
            ? "若本机尚未配置，请先执行「应用项目配置到本机」。\n\n"
            : "项目尚未提供 LarkProjectConfig.asset；请先完成项目飞书应用配置。\n\n";

        var confirm = EditorUtility.DisplayDialog(
            "登录飞书账号",
            projectHint +
            "将打开独立命令行窗口运行 lark-cli auth login --recommend。\n" +
            "按窗口提示在浏览器中完成个人授权；用户令牌仅保存在本机。",
            "打开终端",
            "取消");
        if (!confirm)
            return;

        if (!LarkCliProcessRunner.TryLaunchDetachedConsole(LarkCliAuthHelper.LoginArguments, out var error)) {
            EditorUtility.DisplayDialog("Lark CLI 登录失败", error, "确定");
            return;
        }

        EditorUtility.DisplayDialog(
            "Lark CLI",
            "已在独立命令行窗口启动登录流程。\n完成后可通过「登录状态」查看结果。",
            "确定");
    }
}
#endif
