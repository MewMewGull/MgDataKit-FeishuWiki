#if UNITY_EDITOR
/// <summary>
/// 将项目中的飞书应用凭证写入本机 lark-cli 配置。
/// </summary>
public static class LarkCliLocalBootstrap {
    public static bool TryApplyProjectConfig(out LarkCliProcessRunner.RunResult result) {
        if (!LarkProjectConfigStore.TryLoad(out var config, out var loadError)) {
            result = LarkCliProcessRunner.RunResult.Failure(loadError);
            return false;
        }

        var profileName = LarkProjectConfig.DefaultProfileName;
        var initArguments =
            "config init " +
            $"--app-id {LarkCliProcessRunner.EscapeCliArgument(config.appId)} " +
            "--app-secret-stdin " +
            $"--brand {LarkCliProcessRunner.EscapeCliArgument(config.brand)} " +
            $"--name {LarkCliProcessRunner.EscapeCliArgument(profileName)}";

        if (!LarkCliProcessRunner.TryRunWithStdinInput(initArguments, config.appSecret, out var initResult)) {
            result = initResult;
            return false;
        }

        if (!initResult.Success) {
            result = initResult;
            return true;
        }

        var useArguments = $"profile use {LarkCliProcessRunner.EscapeCliArgument(profileName)}";
        if (!LarkCliProcessRunner.TryRun(useArguments, out result))
            return false;

        return true;
    }
}
#endif
