using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace AiDevTeam.Providers;

public class CliAgentProvider : IAgentProvider
{
    private readonly string _providerType;
    private readonly ILogger<CliAgentProvider> _logger;

    public CliAgentProvider(string providerType, ILogger<CliAgentProvider> logger)
    {
        _providerType = providerType;
        _logger = logger;
    }

    public string ProviderType => _providerType;

    public async Task<AgentRunResult> ExecuteAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        // Validate request and executable before proceeding
        var validationResult = ValidateRequest(request);
        if (!validationResult.IsValid)
        {
            _logger.LogError("Request validation failed: {Error}", validationResult.Error);
            return new AgentRunResult
            {
                Success = false,
                Error = $"Validation failed: {validationResult.Error}",
                ExitCode = -1,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        // Retry logic with exponential backoff
        var maxRetries = 3;
        var baseDelay = TimeSpan.FromSeconds(1);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var (executable, arguments, stdinPrompt) = BuildCommand(request);

                _logger.LogInformation("Starting CLI process (attempt {Attempt}/{MaxRetries}): {Executable} {Arguments} (stdin: {HasStdin})",
                    attempt, maxRetries, executable, arguments, stdinPrompt != null);

                using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Prevent "nested session" errors when launched from within Claude Code
            process.StartInfo.Environment.Remove("CLAUDECODE");

            process.Start();

            // Write prompt via stdin if provided, then close
            if (stdinPrompt != null)
            {
                await process.StandardInput.WriteAsync(stdinPrompt);
                await process.StandardInput.FlushAsync();
            }
            process.StandardInput.Close();

            // Read output async
            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        outputBuilder.AppendLine(line);
                        request.OnOutputReceived?.Invoke(line);
                    }
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync(cancellationToken);
                    if (line != null) errorBuilder.AppendLine(line);
                }
            }, cancellationToken);

            // Wait with timeout + memory watchdog to prevent OOM crashes
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Memory watchdog: kill process if it exceeds 1.5 GB to prevent system-wide OOM
            const long maxMemoryBytes = 1_500_000_000; // 1.5 GB
            var memoryWatchdog = Task.Run(async () =>
            {
                try
                {
                    while (!linkedCts.Token.IsCancellationRequested && !process.HasExited)
                    {
                        await Task.Delay(3000, linkedCts.Token);
                        process.Refresh();
                        if (!process.HasExited && process.WorkingSet64 > maxMemoryBytes)
                        {
                            _logger.LogWarning("Agent process exceeded memory limit ({MemoryMB}MB > {LimitMB}MB), killing",
                                process.WorkingSet64 / 1_000_000, maxMemoryBytes / 1_000_000);
                            process.Kill(true);
                            break;
                        }
                    }
                }
                catch { /* Process already exited or token cancelled */ }
            }, linkedCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                process.Kill(true);
                throw new TimeoutException($"Agent process timed out after {request.TimeoutSeconds}s");
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                throw;
            }

                sw.Stop();

                var result = new AgentRunResult
                {
                    Success = process.ExitCode == 0,
                    Output = outputBuilder.ToString(),
                    Error = errorBuilder.ToString(),
                    ExitCode = process.ExitCode,
                    DurationMs = sw.ElapsedMilliseconds
                };

                // Log detailed execution info
                _logger.LogInformation("CLI execution completed - Success: {Success}, ExitCode: {ExitCode}, Duration: {Duration}ms, OutputLength: {OutputLength}, ErrorLength: {ErrorLength}",
                    result.Success, result.ExitCode, result.DurationMs, result.Output?.Length ?? 0, result.Error?.Length ?? 0);

                if (result.Success || attempt == maxRetries)
                    return result;

                // Log retry attempt
                _logger.LogWarning("CLI execution failed on attempt {Attempt}, will retry. ExitCode: {ExitCode}, Error: {Error}",
                    attempt, result.ExitCode, result.Error);
            }
            catch (TimeoutException ex)
            {
                // Don't retry on timeout — if the task didn't fit in the time window,
                // retrying with the same prompt and timeout will produce the same result.
                // This prevents 3× timeout waits (e.g. 180s × 3 = 540s) for a single run.
                sw.Stop();
                _logger.LogError(ex, "CLI agent timed out on attempt {Attempt}, not retrying", attempt);
                return new AgentRunResult
                {
                    Success = false,
                    Output = outputBuilder.ToString(),
                    Error = ex.Message,
                    ExitCode = -1,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxRetries)
            {
                _logger.LogWarning(ex, "CLI execution failed on attempt {Attempt}, will retry: {Error}", attempt, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                _logger.LogError(ex, "CLI agent execution failed after {Attempts} attempts", maxRetries);
                return new AgentRunResult
                {
                    Success = false,
                    Output = outputBuilder.ToString(),
                    Error = $"Failed after {maxRetries} attempts. Last error: {ex.Message}",
                    ExitCode = -1,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            // Wait before retry with exponential backoff
            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                _logger.LogInformation("Waiting {Delay}ms before retry attempt {NextAttempt}", delay.TotalMilliseconds, attempt + 1);
                await Task.Delay(delay, cancellationToken);
            }
        }

        // This should never be reached
        return new AgentRunResult
        {
            Success = false,
            Error = "Unexpected execution path",
            ExitCode = -1,
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// Build the CLI command. Returns (executable, arguments, stdinPrompt).
    /// stdinPrompt is non-null when the prompt should be piped via stdin instead of -p flag.
    /// </summary>
    private (string executable, string arguments, string? stdinPrompt) BuildCommand(AgentRunRequest request)
    {
        var executable = request.ExecutablePath ?? _providerType switch
        {
            "ClaudeCli" => "claude",
            "CodexCli" => "codex",
            "CopilotCli" => "copilot",
            _ => throw new InvalidOperationException($"Unknown provider: {_providerType}")
        };

        // Resolve executable to full path if not already absolute
        if (!Path.IsPathRooted(executable))
        {
            executable = ExecutableResolver.Resolve(executable) ?? executable;
        }

        if (!string.IsNullOrEmpty(request.CommandTemplate))
        {
            var arguments = request.CommandTemplate
                .Replace("{prompt}", request.Prompt)
                .Replace("{model}", request.ModelName ?? "")
                .Replace("{system_prompt}", request.SystemPrompt ?? "");
            return (executable, arguments, null);
        }

        var args = new List<string>();
        if (!string.IsNullOrEmpty(request.DefaultArguments))
            args.Add(request.DefaultArguments);

        // --model is only supported by Claude CLI and Codex CLI
        if (!string.IsNullOrEmpty(request.ModelName) && request.ModelName != "default"
            && _providerType is "ClaudeCli" or "CodexCli")
            args.AddRange(new[] { "--model", request.ModelName });

        // Vibe mode: inject per-provider flags to auto-approve permissions
        if (request.VibeMode)
        {
            switch (_providerType)
            {
                case "ClaudeCli":
                    args.Add("--dangerously-skip-permissions");
                    break;
                case "CodexCli":
                    args.Add("-c sandbox_permissions=[\"disk-full-read-access\"]");
                    break;
                case "CopilotCli":
                    args.Add("--allow-all-tools");
                    break;
            }
        }

        // Tool restriction: per-provider flags to control which tools the agent can use
        AppendToolRestrictionArgs(args, request);

        // Build the full prompt
        var fullPrompt = request.Prompt;

        // For Claude CLI: system prompt via --append-system-prompt, user prompt via -p
        if (_providerType == "ClaudeCli")
        {
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                // Cap system prompt to prevent command-line overflow
                var sysPrompt = request.SystemPrompt;
                if (sysPrompt.Length > 4000)
                    sysPrompt = sysPrompt[..4000] + "\n...(truncated)";
                var escaped = EscapeForCommandLine(sysPrompt);
                args.AddRange(new[] { "--append-system-prompt", $"\"{escaped}\"" });
            }

            // Cap user prompt to stay within Windows 8191-char command line limit
            if (fullPrompt.Length > 3000)
                fullPrompt = fullPrompt[..3000] + "\n...(truncated)";
            var promptEscaped = EscapeForCommandLine(fullPrompt);
            args.AddRange(new[] { "-p", $"\"{promptEscaped}\"" });
            return (executable, string.Join(" ", args), null);
        }

        // For non-Claude CLIs: prepend a condensed system prompt into the user message
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            // Copilot CLI OOMs on large prompts — keep system instructions short
            var sysPrompt = request.SystemPrompt;
            if (sysPrompt.Length > 1000)
                sysPrompt = sysPrompt[..1000] + "\n...";
            fullPrompt = $"[System Instructions]\n{sysPrompt}\n\n[User Message]\n{request.Prompt}";
        }

        // Hard limit the total prompt to prevent CLI OOM crashes
        if (fullPrompt.Length > 3000)
            fullPrompt = fullPrompt[..3000] + "\n...(truncated)";

        // Use stdin for the prompt — avoids command-line length limits on Windows (8191 chars)
        // The CLI tool reads from stdin when no -p flag is provided
        return (executable, string.Join(" ", args), fullPrompt);
    }

    private static string EscapeForCommandLine(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    /// <summary>
    /// Appends tool restriction flags appropriate for each CLI provider.
    /// Supports AllowedTools (whitelist) and DisallowedTools (blacklist).
    /// An empty AllowedTools list means "no tools allowed at all".
    /// </summary>
    private void AppendToolRestrictionArgs(List<string> args, AgentRunRequest request)
    {
        var hasWhitelist = request.AllowedTools != null;
        var whitelistEmpty = request.AllowedTools is { Count: 0 };
        var hasBlacklist = request.DisallowedTools is { Count: > 0 };

        if (!hasWhitelist && !hasBlacklist) return;

        switch (_providerType)
        {
            case "ClaudeCli":
                // Claude Code expects each tool as a separate quoted argument after the flag:
                //   --disallowedTools "Bash" "Read" "Write" ...
                // NOT as a comma-separated string (which is silently ignored).
                if (hasWhitelist && !whitelistEmpty)
                {
                    args.Add("--allowedTools");
                    foreach (var tool in request.AllowedTools!)
                        args.Add($"\"{tool}\"");
                }
                else if (whitelistEmpty)
                {
                    // Block all known Claude Code tools
                    args.Add("--disallowedTools");
                    foreach (var tool in new[] { "Bash", "Read", "Write", "Edit", "Glob", "Grep", "NotebookEdit", "WebFetch", "WebSearch", "Agent" })
                        args.Add($"\"{tool}\"");
                }
                else if (hasBlacklist)
                {
                    args.Add("--disallowedTools");
                    foreach (var tool in request.DisallowedTools!)
                        args.Add($"\"{tool}\"");
                }
                break;

            case "CopilotCli":
                if (hasWhitelist && !whitelistEmpty)
                {
                    foreach (var tool in request.AllowedTools!)
                        args.AddRange(new[] { "--allow-tool", $"\"{tool}\"" });
                }
                else if (whitelistEmpty)
                {
                    // Block all known Copilot tools — must cover read tools too,
                    // otherwise the agent browses the filesystem instead of answering
                    foreach (var tool in new[] { "shell", "write", "read", "edit",
                        "view", "list", "find", "navigate", "grep", "search", "create", "check" })
                        args.AddRange(new[] { "--deny-tool", $"\"{tool}\"" });
                }
                else if (hasBlacklist)
                {
                    foreach (var tool in request.DisallowedTools!)
                        args.AddRange(new[] { "--deny-tool", $"\"{tool}\"" });
                }
                break;

            case "CodexCli":
                if (whitelistEmpty)
                {
                    // Full sandbox with no permissions = no file/shell access
                    args.AddRange(new[] { "--sandbox", "full", "-c", "sandbox_permissions=[]" });
                }
                else if (hasWhitelist)
                {
                    // Map to sandbox permissions: read-only or full access
                    var perms = request.AllowedTools!.Any(t =>
                        t.Equals("Write", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Bash", StringComparison.OrdinalIgnoreCase))
                        ? "[\"disk-full-read-access\",\"disk-write-access\"]"
                        : "[\"disk-full-read-access\"]";
                    args.AddRange(new[] { "-c", $"sandbox_permissions={perms}" });
                }
                // DisallowedTools for Codex: no direct equivalent, rely on sandbox mode
                break;
        }
    }

    /// <summary>
    /// Validate the agent run request and executable availability.
    /// </summary>
    private (bool IsValid, string Error) ValidateRequest(AgentRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return (false, "Prompt cannot be empty");

        if (request.TimeoutSeconds <= 0)
            return (false, "Timeout must be greater than 0");

        if (request.TimeoutSeconds > 1800) // 30 minutes max
            return (false, "Timeout cannot exceed 30 minutes");

        try
        {
            var (executable, _, _) = BuildCommand(request);
            
            // Check if executable exists
            if (!Path.IsPathRooted(executable))
            {
                var resolved = ExecutableResolver.Resolve(executable);
                if (resolved == null)
                    return (false, $"Executable '{executable}' not found in PATH");
            }
            else if (!File.Exists(executable))
            {
                return (false, $"Executable not found: {executable}");
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Command building failed: {ex.Message}");
        }
    }
}
