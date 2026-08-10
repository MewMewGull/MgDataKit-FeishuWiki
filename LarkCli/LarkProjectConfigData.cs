#if UNITY_EDITOR
using System;

/// <summary>
/// 项目级飞书应用凭证；用户 OAuth 令牌仍保存在本机 ~/.lark-cli。
/// </summary>
[Serializable]
public class LarkProjectConfigData {
    public const string DefaultProfileName = "mgdatakit";
    public const string DefaultBrand = "feishu";
    public const string DefaultWikiHost = "";
    public const string DefaultWikiSpaceId = "";
    public const string DefaultWikiParentNodeToken = "";

    public string appId;
    public string appSecret;
    public string brand = DefaultBrand;
    public string wikiHost = DefaultWikiHost;
    public string wikiSpaceId = DefaultWikiSpaceId;
    public string wikiParentNodeToken = DefaultWikiParentNodeToken;
    public bool playBeforeFeishuSyncEnabled;

    public string WikiHost => string.IsNullOrWhiteSpace(wikiHost) ? DefaultWikiHost : wikiHost;
    public string WikiSpaceId => string.IsNullOrWhiteSpace(wikiSpaceId) ? DefaultWikiSpaceId : wikiSpaceId;
    public string WikiParentNodeToken => string.IsNullOrWhiteSpace(wikiParentNodeToken)
        ? DefaultWikiParentNodeToken
        : wikiParentNodeToken;

    public bool IsValid(out string errorMessage) {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(appId)) {
            errorMessage = "App ID 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(appSecret)) {
            errorMessage = "App Secret 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(brand))
            brand = DefaultBrand;

        if (brand != "feishu" && brand != "lark") {
            errorMessage = "brand 只能是 feishu 或 lark。";
            return false;
        }

        return true;
    }
}
#endif
