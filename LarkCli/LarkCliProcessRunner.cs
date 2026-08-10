#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

/// <summary>
/// 通过 <see cref="Process"/> 调用项目内置 Lark CLI。
/// </summary>
public static class LarkCliProcessRunner {
    public sealed class RunResult {
        public bool Success => ExitCode == 0;
        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public string CombinedOutput { get; }

        RunResult(int exitCode, string standardOutput, string standardError) {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            CombinedOutput = string.IsNullOrEmpty(StandardError)
                ? StandardOutput
                : $"{StandardOutput}\n{StandardError}".TrimEnd();
        }

        public static RunResult FromProcess(int exitCode, string standardOutput, string standardError) {
            return new RunResult(exitCode, standardOutput, standardError);
        }

        public static RunResult Failure(string message) {
            return new RunResult(-1, string.Empty, message);
        }
    }

    public static string EscapeCliArgument(string value) {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.IndexOfAny(new[] { ' ', '\t', '"', '&', '|', '<', '>' }) < 0)
            return value;

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    public static bool TryRun(string arguments, out RunResult result, string workingDirectory = null) {
        if (!LarkCliPathResolver.TryResolve(out var executablePath, out var resolveError)) {
            result = RunResult.Failure(resolveError);
            return false;
        }

        return TryStartProcess(
            executablePath,
            arguments,
            workingDirectory ?? LarkCliPathResolver.GetProjectRootPath(),
            standardInput: null,
            redirectOutput: true,
            useShellExecute: false,
            createNoWindow: true,
            out result);
    }

    public static bool TryRunWithStdinInput(string arguments, string stdinText, out RunResult result, string workingDirectory = null) {
        if (!LarkCliPathResolver.TryResolve(out var executablePath, out var resolveError)) {
            result = RunResult.Failure(resolveError);
            return false;
        }

        return TryStartProcess(
            executablePath,
            arguments,
            workingDirectory ?? LarkCliPathResolver.GetProjectRootPath(),
            standardInput: stdinText ?? string.Empty,
            redirectOutput: true,
            useShellExecute: false,
            createNoWindow: true,
            out result);
    }

    /// <summary>
    /// 在独立命令行窗口中启动交互命令，不阻塞 Unity 主线程。
    /// </summary>
    public static bool TryLaunchDetachedConsole(string arguments, out string errorMessage, string workingDirectory = null) {
        errorMessage = null;
        if (!LarkCliPathResolver.TryResolve(out var executablePath, out var resolveError)) {
            errorMessage = resolveError;
            return false;
        }

        var workDir = workingDirectory ?? LarkCliPathResolver.GetProjectRootPath();

        try {
            // 必须从 cmd /k 启动：Unity 为 GUI 进程，直接拉起控制台程序常出现空白窗体。
            var startInfo = new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = BuildDetachedConsoleArguments(executablePath, arguments),
                WorkingDirectory = workDir,
                UseShellExecute = true,
            };

            var process = Process.Start(startInfo);
            if (process == null) {
                errorMessage = "无法启动命令行窗口。";
                return false;
            }

            return true;
        } catch (Exception ex) {
            Debug.LogException(ex);
            errorMessage = ex.Message;
            return false;
        }
    }

    static string BuildDetachedConsoleArguments(string executablePath, string arguments) {
        var safeArguments = arguments ?? string.Empty;
        return $"/k title Lark CLI & echo [Lark CLI] {safeArguments} & call \"{executablePath}\" {safeArguments}";
    }

    static bool TryStartProcess(
        string executablePath,
        string arguments,
        string workingDirectory,
        string standardInput,
        bool redirectOutput,
        bool useShellExecute,
        bool createNoWindow,
        out RunResult result) {
        result = null;

        try {
            var startInfo = new ProcessStartInfo {
                FileName = executablePath,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory,
                UseShellExecute = useShellExecute,
                CreateNoWindow = createNoWindow,
            };

            if (standardInput != null)
                startInfo.RedirectStandardInput = true;

            if (redirectOutput) {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            using var process = Process.Start(startInfo);
            if (process == null) {
                result = RunResult.Failure("无法启动 Lark CLI 进程。");
                return false;
            }

            string stdout = string.Empty;
            string stderr = string.Empty;
            Task<string> stdoutTask = null;
            Task<string> stderrTask = null;
            if (redirectOutput) {
                // Start both reads before waiting or writing stdin so a verbose CLI diagnostic
                // cannot fill one redirected pipe and block the child process.
                stdoutTask = process.StandardOutput.ReadToEndAsync();
                stderrTask = process.StandardError.ReadToEndAsync();
            }

            if (standardInput != null) {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            process.WaitForExit();
            if (redirectOutput) {
                Task.WaitAll(stdoutTask, stderrTask);
                stdout = stdoutTask.Result;
                stderr = stderrTask.Result;
            }
            result = RunResult.FromProcess(process.ExitCode, stdout, stderr);
            return true;
        } catch (Exception ex) {
            Debug.LogException(ex);
            result = RunResult.Failure(ex.Message);
            return false;
        }
    }
}
#endif
