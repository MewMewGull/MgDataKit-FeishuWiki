#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 解析项目内置的 Lark CLI 可执行文件路径（<c>Assets/MgDataKit/Editor/Adapters/Feishu/LarkCliBinary</c>）。
/// </summary>
public static class LarkCliPathResolver {
    public const string BundledVersion = "1.0.60";
    const string RelativeRoot = "MgDataKit/Editor/Adapters/Feishu/LarkCliBinary";

    public static string GetBundledRootPath() {
        return Path.GetFullPath(Path.Combine(Application.dataPath, RelativeRoot));
    }

    public static string GetProjectRootPath() {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    public static bool TryResolve(out string executablePath, out string errorMessage) {
        executablePath = null;
        errorMessage = null;

        var platformDir = GetPlatformDirectoryName();
        if (platformDir == null) {
            errorMessage = "当前操作系统尚未提供内置 Lark CLI，请联系维护者补充对应平台二进制。";
            return false;
        }

        var fileName = GetExecutableFileName();
        var candidate = Path.GetFullPath(Path.Combine(GetBundledRootPath(), platformDir, fileName));
        if (!File.Exists(candidate)) {
            errorMessage =
                $"未找到内置 Lark CLI：{candidate}\n" +
                "请按 Feishu 适配器 README 从 larksuite/cli v1.0.60 官方 Release 下载对应二进制。";
            return false;
        }

        executablePath = candidate;
        return true;
    }

    public static string GetVersionFilePath() {
        return Path.Combine(GetBundledRootPath(), "VERSION.txt");
    }

    public static string ReadBundledVersion() {
        var versionPath = GetVersionFilePath();
        if (!File.Exists(versionPath))
            return BundledVersion;

        var text = File.ReadAllText(versionPath).Trim();
        return string.IsNullOrEmpty(text) ? BundledVersion : text;
    }

    static string GetPlatformDirectoryName() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "darwin-arm64"
                : "darwin-amd64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux-amd64";

        return null;
    }

    static string GetExecutableFileName() {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "lark-cli.exe" : "lark-cli";
    }
}
#endif
