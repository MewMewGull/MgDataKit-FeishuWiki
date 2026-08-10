#if UNITY_EDITOR
using System;
using System.Text.RegularExpressions;

/// <summary>
/// Lark CLI 认证相关参数与错误文案。
/// </summary>
public static class LarkCliAuthHelper {
    public const string LoginArguments = "auth login --recommend";

    public static string FormatFailureMessage(LarkCliProcessRunner.RunResult result, string actionLabel) {
        if (result == null)
            return $"{actionLabel}失败。";

        var detail = ExtractErrorMessage(result.CombinedOutput);
        if (!string.IsNullOrEmpty(detail))
            return $"{actionLabel}失败。\n\n{detail}\n\n{GetRecoveryHint(result.ExitCode, detail)}";

        if (!string.IsNullOrEmpty(result.CombinedOutput))
            return $"{actionLabel}失败。\n\n{result.CombinedOutput.Trim()}\n\n{GetRecoveryHint(result.ExitCode, null)}";

        return $"{actionLabel}失败（退出码 {result.ExitCode}）。\n\n{GetRecoveryHint(result.ExitCode, null)}";
    }

    public static string GetRecoveryHint(int exitCode, string detail) {
        if (exitCode == 2 && detail != null && detail.Contains("scopes", StringComparison.OrdinalIgnoreCase))
            return "请使用带权限参数的登录命令（项目内已默认附加 --recommend）。";

        if (exitCode == 3 || (detail != null && detail.Contains("client secret", StringComparison.OrdinalIgnoreCase)))
            return
                "这通常表示本机尚未应用项目飞书配置，或 App Secret 已失效。\n" +
                "请先执行「应用项目配置到本机」，或让管理员按项目的凭据管理流程更新 LarkProjectConfig.asset 后重新应用。";

        return "可在 Unity Console 查看完整输出，或在本机终端手动运行内置 lark-cli 排查。";
    }

    static string ExtractErrorMessage(string output) {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var messageMatch = Regex.Match(output, "\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        if (!messageMatch.Success)
            return null;

        return Regex.Unescape(messageMatch.Groups[1].Value);
    }
}
#endif
