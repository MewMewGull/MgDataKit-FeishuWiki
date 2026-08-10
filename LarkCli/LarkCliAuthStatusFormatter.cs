#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// 将 lark-cli auth status 的 JSON 输出转为非技术人员可读的摘要。
/// </summary>
public static class LarkCliAuthStatusFormatter {
    [Serializable]
    class AuthStatusRoot {
        public string appId;
        public string brand;
        public string defaultAs;
        public string identity;
        public string note;
        public AuthIdentities identities;
    }

    [Serializable]
    class AuthIdentities {
        public AuthIdentity bot;
        public AuthIdentity user;
    }

    [Serializable]
    class AuthIdentity {
        public string status;
        public bool available;
        public string message;
        public string hint;
        public string userName;
        public string tokenStatus;
        public string expiresAt;
        public string refreshExpiresAt;
    }

    public static bool TryFormat(string rawOutput, out string summary, out string parseError) {
        summary = null;
        parseError = null;

        if (string.IsNullOrWhiteSpace(rawOutput)) {
            parseError = "CLI 未返回任何内容。";
            return false;
        }

        var json = ExtractJsonObject(rawOutput);
        if (string.IsNullOrEmpty(json)) {
            parseError = "无法从输出中识别 JSON。";
            return false;
        }

        AuthStatusRoot data;
        try {
            data = JsonUtility.FromJson<AuthStatusRoot>(json);
        } catch (Exception ex) {
            parseError = ex.Message;
            return false;
        }

        if (data == null) {
            parseError = "JSON 解析结果为空。";
            return false;
        }

        summary = BuildSummary(data);
        return true;
    }

    static string BuildSummary(AuthStatusRoot data) {
        var builder = new StringBuilder();
        builder.AppendLine("飞书 CLI 登录状态");
        builder.AppendLine();

        builder.AppendLine("【团队应用】");
        builder.AppendLine($"应用 ID：{ValueOrPlaceholder(data.appId)}");
        builder.AppendLine($"平台：{TranslateBrand(data.brand)}");
        builder.AppendLine();

        AppendIdentitySection(builder, "您的个人账号", data.identities?.user, isUser: true);
        builder.AppendLine();
        AppendIdentitySection(builder, "应用机器人身份", data.identities?.bot, isUser: false);
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(data.identity))
            builder.AppendLine($"当前默认身份：{TranslateIdentity(data.identity)}");

        if (!string.IsNullOrWhiteSpace(data.note))
            builder.AppendLine(TranslateNote(data.note));

        AppendOverallHint(builder, data);
        return builder.ToString().TrimEnd();
    }

    static void AppendIdentitySection(StringBuilder builder, string title, AuthIdentity identity, bool isUser) {
        builder.AppendLine($"【{title}】");

        if (identity == null) {
            builder.AppendLine("状态：未知");
            return;
        }

        builder.AppendLine($"状态：{DescribeIdentityStatus(identity)}");

        if (isUser && identity.available && identity.status == "ready") {
            if (!string.IsNullOrWhiteSpace(identity.userName))
                builder.AppendLine($"用户名：{identity.userName}");

            if (!string.IsNullOrWhiteSpace(identity.tokenStatus))
                builder.AppendLine($"令牌状态：{TranslateTokenStatus(identity.tokenStatus)}");

            var expiresAt = FormatDateTime(identity.expiresAt);
            if (!string.IsNullOrEmpty(expiresAt))
                builder.AppendLine($"访问令牌有效期至：{expiresAt}");

            var refreshExpiresAt = FormatDateTime(identity.refreshExpiresAt);
            if (!string.IsNullOrEmpty(refreshExpiresAt))
                builder.AppendLine($"刷新令牌有效期至：{refreshExpiresAt}");
        } else if (isUser && !identity.available) {
            builder.AppendLine("说明：尚未完成个人授权。");
            builder.AppendLine("下一步：菜单「MgDataKit → 飞书 Lark CLI → 登录飞书账号...」");
        } else if (!isUser && identity.available) {
            builder.AppendLine("说明：应用级接口可用（如同步机器人相关能力）。");
        }
    }

    static void AppendOverallHint(StringBuilder builder, AuthStatusRoot data) {
        var user = data.identities?.user;
        if (user != null && user.available && user.status == "ready") {
            builder.AppendLine();
            builder.AppendLine("总结：个人账号已登录，可执行需要您本人权限的飞书操作（如飞书数据同步）。");
            return;
        }

        var bot = data.identities?.bot;
        if (bot != null && bot.available && (user == null || !user.available)) {
            builder.AppendLine();
            builder.AppendLine("总结：仅应用机器人身份可用；若要操作个人云文档，请先登录飞书账号。");
        }
    }

    static string DescribeIdentityStatus(AuthIdentity identity) {
        if (identity.available && identity.status == "ready")
            return "已就绪";

        if (identity.status == "missing")
            return "未配置 / 未登录";

        if (!string.IsNullOrWhiteSpace(identity.status))
            return TranslateGenericStatus(identity.status);

        return identity.available ? "可用" : "不可用";
    }

    static string TranslateBrand(string brand) {
        return brand switch {
            "feishu" => "飞书",
            "lark" => "Lark（国际版）",
            _ => ValueOrPlaceholder(brand),
        };
    }

    static string TranslateIdentity(string identity) {
        return identity switch {
            "user" => "个人账号",
            "bot" => "应用机器人",
            "auto" => "自动选择",
            _ => identity,
        };
    }

    static string TranslateTokenStatus(string tokenStatus) {
        return tokenStatus switch {
            "valid" => "有效",
            "expired" => "已过期",
            "missing" => "缺失",
            _ => tokenStatus,
        };
    }

    static string TranslateGenericStatus(string status) {
        return status switch {
            "ready" => "已就绪",
            "missing" => "未配置",
            _ => status,
        };
    }

    static string TranslateNote(string note) {
        if (note.Contains("User identity is missing", StringComparison.OrdinalIgnoreCase))
            return "提示：个人账号尚未登录，请先执行「登录飞书账号」。";

        if (note.Contains("auth login", StringComparison.OrdinalIgnoreCase))
            return "提示：请先完成飞书账号登录。";

        return $"提示：{note}";
    }

    static string FormatDateTime(string iso8601) {
        if (string.IsNullOrWhiteSpace(iso8601))
            return null;

        if (!DateTimeOffset.TryParse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            return iso8601;

        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    static string ValueOrPlaceholder(string value) {
        return string.IsNullOrWhiteSpace(value) ? "（未知）" : value.Trim();
    }

    static string ExtractJsonObject(string output) {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return output.Substring(start, end - start + 1);
    }
}
#endif
