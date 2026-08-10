#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// 宿主项目的本地飞书应用配置（App 级凭证）。
/// 此资产位于 MgDataKit Core 仓库之外；如需共享，请遵循项目的版本控制与凭据管理规范。
/// 用户 OAuth 令牌仍保存在各本机 ~/.lark-cli，不进版本库。
/// </summary>
[CreateAssetMenu(menuName = "MgDataKit/Lark Project Config")]
public sealed class LarkProjectConfig : ScriptableObject {
    public const string DefaultProfileName = "mgdatakit";
    public const string DefaultBrand = "feishu";
    public const string DefaultWikiHost = "";
    public const string DefaultWikiSpaceId = "";
    public const string DefaultWikiParentNodeToken = "";

    [SerializeField]
    public string appId;

    [SerializeField]
    public string appSecret;

    [SerializeField]
    public string brand = DefaultBrand;

    [Header("Wiki")]
    [SerializeField]
    public string wikiHost = DefaultWikiHost;

    [SerializeField]
    public string wikiSpaceId = DefaultWikiSpaceId;

    [SerializeField]
    public string wikiParentNodeToken = DefaultWikiParentNodeToken;

    [SerializeField]
    public bool playBeforeFeishuSyncEnabled;

    public string WikiHost => string.IsNullOrWhiteSpace(wikiHost) ? DefaultWikiHost : wikiHost;
    public string WikiSpaceId => string.IsNullOrWhiteSpace(wikiSpaceId) ? DefaultWikiSpaceId : wikiSpaceId;
    public string WikiParentNodeToken => string.IsNullOrWhiteSpace(wikiParentNodeToken)
        ? DefaultWikiParentNodeToken
        : wikiParentNodeToken;

    public bool IsValid(out string errorMessage) {
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
            errorMessage = "Brand 只能是 feishu 或 lark。";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
#endif
