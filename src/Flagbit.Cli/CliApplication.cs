using System.Net;
using System.Text.Json;
using Flagbit.Cli.Api;

namespace Flagbit.Cli;

internal sealed class CliApplication
{
    private readonly FlagbitApiClient _apiClient;
    private readonly TextWriter? _output;
    private readonly TextWriter? _error;

    public CliApplication(FlagbitApiClient apiClient, TextWriter? output = null, TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;
        _output = output;
        _error = error;
    }

    private TextWriter Output => _output ?? Console.Out;
    private TextWriter Error => _error ?? Console.Error;

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = args[0].ToLowerInvariant();

            if (args.Length > 1 && string.IsNullOrWhiteSpace(args[1]))
            {
                return InvalidCommand("Flag key must not be blank.");
            }

            return command switch
            {
                "list" when args.Length == 1 => await ListAsync(cancellationToken),
                "get" when args.Length == 2 => await GetAsync(args[1], cancellationToken),
                "create" when args.Length == 2 => await CreateAsync(args[1], cancellationToken),
                "enable" when args.Length == 2 => await ChangeStateAsync(args[1], true, cancellationToken),
                "disable" when args.Length == 2 => await ChangeStateAsync(args[1], false, cancellationToken),
                "delete" when args.Length == 2 => await DeleteAsync(args[1], cancellationToken),
                "evaluate" when args.Length >= 2 => await EvaluateAsync(args, cancellationToken),
                _ => InvalidCommand("Unknown command or incorrect arguments.")
            };
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Error.WriteLine("Command cancelled.");
                return 130;
            }

            Error.WriteLine("The Flagbit API request timed out.");
            return 1;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is not null)
        {
            var message = exception.StatusCode.Value switch
            {
                HttpStatusCode.Unauthorized => "API authentication failed. Set FLAGBIT_API_KEY to a valid API key.",
                HttpStatusCode.Forbidden => "API access denied. This command requires a management API key.",
                _ => $"API request failed: {(int)exception.StatusCode.Value} {exception.StatusCode.Value}."
            };
            if (exception is ApiRequestException { Details: { Length: > 0 } details })
            {
                message += $" {details}";
            }

            Error.WriteLine(message);
            return 1;
        }
        catch (HttpRequestException)
        {
            Error.WriteLine("Could not connect to the Flagbit API.");
            return 1;
        }
        catch (JsonException)
        {
            Error.WriteLine("The Flagbit API returned an invalid response.");
            return 1;
        }
    }

    private async Task<int> GetAsync(string key, CancellationToken cancellationToken)
    {
        var flag = await _apiClient.GetByKeyAsync(key, cancellationToken);
        Output.WriteLine($"{flag.Key} is {FormatState(flag.IsEnabled)}.");
        return 0;
    }

    private async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var flags = await _apiClient.GetAllAsync(cancellationToken);

        if (flags.Count == 0)
        {
            Output.WriteLine("No feature flags found.");
            return 0;
        }

        foreach (var flag in flags.OrderBy(flag => flag.Key, StringComparer.OrdinalIgnoreCase))
        {
            Output.WriteLine($"{flag.Key} {FormatState(flag.IsEnabled)}");
        }

        return 0;
    }

    private async Task<int> CreateAsync(string key, CancellationToken cancellationToken)
    {
        var flag = await _apiClient.CreateAsync(key, cancellationToken);
        Output.WriteLine($"Created {flag.Key} ({FormatState(flag.IsEnabled)}).");
        return 0;
    }

    private async Task<int> ChangeStateAsync(string key, bool isEnabled, CancellationToken cancellationToken)
    {
        var flag = await _apiClient.SetEnabledAsync(key, isEnabled, cancellationToken);
        Output.WriteLine($"{flag.Key} is {FormatState(flag.IsEnabled)}.");
        return 0;
    }

    private async Task<int> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await _apiClient.DeleteAsync(key, cancellationToken);
        Output.WriteLine($"Deleted {key}.");
        return 0;
    }

    private async Task<int> EvaluateAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!TryParseEvaluationRequest(args, out var request, out var error))
        {
            return InvalidCommand(error);
        }

        var result = await _apiClient.EvaluateAsync(args[1], request, cancellationToken);
        Output.WriteLine($"{result.Key} is {FormatState(result.IsEnabled)}.");
        return 0;
    }

    private static bool TryParseEvaluationRequest(string[] args, out EvaluateFeatureFlagRequest request, out string error)
    {
        string? userId = null;
        string? environment = null;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        request = new EvaluateFeatureFlagRequest();
        error = "Each evaluation option requires a nonblank value.";

        for (var index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            var option = args[index].ToLowerInvariant();
            var value = args[index + 1];

            switch (option)
            {
                case "--user":
                    if (userId is not null)
                    {
                        error = "Option '--user' may only be specified once.";
                        return false;
                    }

                    userId = value;
                    break;
                case "--environment":
                    if (environment is not null)
                    {
                        error = "Option '--environment' may only be specified once.";
                        return false;
                    }

                    environment = value;
                    break;
                case "--attribute":
                    var separatorIndex = value.IndexOf('=');
                    if (separatorIndex <= 0 || string.IsNullOrWhiteSpace(value[..separatorIndex]) || string.IsNullOrWhiteSpace(value[(separatorIndex + 1)..]))
                    {
                        error = "Attributes require a nonblank name and value: --attribute name=value.";
                        return false;
                    }

                    if (!attributes.TryAdd(value[..separatorIndex], value[(separatorIndex + 1)..]))
                    {
                        error = "Attribute names must be unique (case-insensitive).";
                        return false;
                    }

                    break;
                default:
                    error = "Unknown evaluation option.";
                    return false;
            }
        }

        request = new EvaluateFeatureFlagRequest(userId, environment, attributes.Count == 0 ? null : attributes);
        return true;
    }

    private int InvalidCommand(string message)
    {
        Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static string FormatState(bool isEnabled)
    {
        return isEnabled ? "enabled" : "disabled";
    }

    private void PrintUsage()
    {
        Output.WriteLine("Usage:");
        Output.WriteLine("  flagbit list");
        Output.WriteLine("  flagbit get <key>");
        Output.WriteLine("  flagbit create <key>");
        Output.WriteLine("  flagbit enable <key>");
        Output.WriteLine("  flagbit disable <key>");
        Output.WriteLine("  flagbit delete <key>");
        Output.WriteLine("  flagbit evaluate <key> [--user <id>] [--environment <name>] [--attribute <key=value>]");
    }
}
