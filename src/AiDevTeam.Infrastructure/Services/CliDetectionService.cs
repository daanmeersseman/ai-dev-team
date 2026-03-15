using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace AiDevTeam.Infrastructure.Services;

public class CliDetectionService : ICliDetectionService
{
    private readonly ILogger<CliDetectionService> _logger;

    public CliDetectionService(ILogger<CliDetectionService> logger) => _logger = logger;

    public async Task<List<ProviderConfiguration>> DetectAllProvidersAsync()
    {
        var providers = new List<ProviderConfiguration>();

        // Always add Mock
        providers.Add(new ProviderConfiguration
        {
            Name = "Mock",
            IsAvailable = true,
            IsDetected = true,
            AvailableModels = new List<string> { "mock-v1" }
        });

        // Detect real CLI tools in parallel (fast — version check only)
        var tasks = new[]
        {
            DetectProviderAsync("ClaudeCli"),
            DetectProviderAsync("CodexCli"),
            DetectProviderAsync("CopilotCli")
        };
        var results = await Task.WhenAll(tasks);
        providers.AddRange(results);

        return providers;
    }

    public async Task<ProviderConfiguration> DetectProviderAsync(string providerName)
    {
        var (executable, versionFlag) = providerName switch
        {
            "ClaudeCli" => ("claude", "--version"),
            "CodexCli" => ("codex", "--version"),
            "CopilotCli" => ("copilot", "--version"),
            _ => (providerName.ToLowerInvariant(), "--version")
        };

        var config = new ProviderConfiguration
        {
            Name = providerName,
            ExecutablePath = executable
        };

        try
        {
            var result = await RunCommandAsync(executable, versionFlag, 10);
            if (result.exitCode == 0)
            {
                config.IsDetected = true;
                config.IsAvailable = true;
                _logger.LogInformation("Detected {Provider}: {Output}", providerName, result.output.Trim());

                // Fast: return known model candidates (no probing at startup)
                config.AvailableModels = GetDefaultModels(providerName);
                if (config.AvailableModels.Any())
                    config.DefaultModel = config.AvailableModels.First();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Provider {Provider} not detected: {Error}", providerName, ex.Message);
            config.IsDetected = false;
            config.IsAvailable = false;
        }

        return config;
    }

    /// <summary>
    /// Returns known model candidates per provider. These are aliases/names
    /// that the CLI tools accept via their --model flag.
    /// Claude CLI uses short aliases (sonnet, haiku, opus).
    /// Codex CLI uses OpenAI model IDs.
    /// Copilot CLI has no model selection — uses server default.
    /// </summary>
    private static List<string> GetDefaultModels(string providerName) => providerName switch
    {
        "ClaudeCli" => new List<string> { "sonnet", "haiku", "opus" },
        "CodexCli" => new List<string> { "codex-mini", "o4-mini", "o3", "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano" },
        "CopilotCli" => new List<string> { "default" },
        _ => new List<string>()
    };

    public async Task<List<string>> GetAvailableModelsAsync(string providerName, string? executablePath = null)
    {
        var executable = executablePath ?? providerName switch
        {
            "ClaudeCli" => "claude",
            "CodexCli" => "codex",
            "CopilotCli" => "copilot",
            _ => providerName.ToLowerInvariant()
        };

        // Claude CLI: probe which model aliases actually work
        if (providerName == "ClaudeCli")
        {
            var probed = await ProbeClaudeModelsAsync(executable);
            if (probed.Any()) return probed;
        }

        // Fallback to default candidates
        return GetDefaultModels(providerName);
    }

    /// <summary>
    /// Probes Claude CLI to discover which model aliases actually work
    /// by making minimal --print calls in parallel.
    /// Called on-demand (e.g. Settings page refresh), not at startup.
    /// </summary>
    private async Task<List<string>> ProbeClaudeModelsAsync(string executable)
    {
        var candidates = new[] { "sonnet", "haiku", "opus" };
        var available = new List<string>();

        var probeTasks = candidates.Select(async model =>
        {
            try
            {
                var result = await RunCommandAsync(executable,
                    $"--print --model {model} -p \"ok\"", 15);

                var output = (result.output ?? "").ToLowerInvariant();
                var isError = result.exitCode != 0
                    || output.Contains("invalid model")
                    || output.Contains("api error")
                    || output.Contains("not found")
                    || output.Contains("not available");

                return (model, isAvailable: !isError);
            }
            catch
            {
                return (model, isAvailable: false);
            }
        }).ToArray();

        var results = await Task.WhenAll(probeTasks);
        foreach (var (model, isAvailable) in results)
        {
            if (isAvailable)
            {
                available.Add(model);
                _logger.LogInformation("ClaudeCli: model '{Model}' is available", model);
            }
            else
            {
                _logger.LogDebug("ClaudeCli: model '{Model}' is NOT available", model);
            }
        }

        return available;
    }

    private static async Task<(int exitCode, string output)> RunCommandAsync(string executable, string arguments, int timeoutSeconds)
    {
        using var process = new Process();
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c {executable} {arguments}";
            var npmPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
            var userLocalBin = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = $"{npmPath};{userLocalBin};{currentPath}";
        }
        else
        {
            psi.FileName = executable;
            psi.Arguments = arguments;
        }

        // Prevent nested session errors when launched from within Claude Code
        psi.Environment.Remove("CLAUDECODE");

        process.StartInfo = psi;
        process.Start();
        process.StandardInput.Close();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var output = await outputTask;
            var error = await errorTask;
            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
        catch
        {
            try { process.Kill(true); } catch { }
            return (-1, "Timeout");
        }
    }
}
