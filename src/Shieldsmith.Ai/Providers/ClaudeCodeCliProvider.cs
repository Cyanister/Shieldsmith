using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Shieldsmith.Ai.Providers;

/// <summary>
/// Claude via an installed Claude Code CLI, run headless with
/// "claude -p --output-format json". Uses the user's existing subscription, so
/// no API key is required. Any failure to find, run, or parse the CLI is
/// reported as unavailability, never as a crash.
/// </summary>
public sealed class ClaudeCodeCliProvider : IAiProvider
{
    public const string ProviderName = "Claude Code CLI";

    private readonly string? _cliPath;
    private readonly TimeSpan _timeout;

    public ClaudeCodeCliProvider(string? cliPath = null, TimeSpan? timeout = null)
    {
        _cliPath = cliPath ?? FindCli();
        _timeout = timeout ?? TimeSpan.FromMinutes(4);
    }

    public string Name => ProviderName;
    public string Model => "user's Claude Code default";
    public bool IsInstalled => _cliPath is not null;

    public static string? FindCli()
    {
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            foreach (var candidate in new[] { "claude.exe", "claude.cmd", "claude" })
            {
                var full = Path.Combine(dir.Trim(), candidate);
                if (File.Exists(full)) return full;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var candidate in new[]
                 {
                     Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
                     Path.Combine(localAppData, "AnthropicClaude", "claude.exe"),
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public async Task<AiAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (_cliPath is null)
            return AiAvailability.No("Claude Code CLI not found on PATH.");
        try
        {
            var (exitCode, stdout, _) = await RunAsync("--version", null, TimeSpan.FromSeconds(30), cancellationToken);
            return exitCode == 0
                ? AiAvailability.Yes($"Claude Code detected ({stdout.Trim()}).")
                : AiAvailability.No("Claude Code CLI did not respond to --version.");
        }
        catch (Exception ex)
        {
            return AiAvailability.No(ex.Message);
        }
    }

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (_cliPath is null)
            return AiResult.Fail("Claude Code CLI not found.");

        try
        {
            var prompt = request.SystemPrompt + "\n\n" + request.UserPrompt;
            var (exitCode, stdout, stderr) = await RunAsync(
                "-p --output-format json", prompt, _timeout, cancellationToken);

            if (exitCode != 0)
                return AiResult.Fail($"Claude Code exited with code {exitCode}: {Truncate(stderr, 300)}");

            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;

            if (root.TryGetProperty("is_error", out var isError)
                && isError.ValueKind == JsonValueKind.True)
            {
                return AiResult.Fail("Claude Code reported an error for this request.");
            }

            if (root.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(result.GetString()))
            {
                return AiResult.Ok(result.GetString()!.Trim());
            }

            return AiResult.Fail("Claude Code returned no result text.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            return AiResult.Fail("Claude Code output could not be parsed; treating the provider as unavailable.");
        }
        catch (Exception ex)
        {
            return AiResult.Fail($"Claude Code invocation failed: {ex.Message}");
        }
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string arguments, string? stdin, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(_cliPath!, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Claude Code process failed to start.");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException("Claude Code timed out for this request.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..length] + "...";
}
