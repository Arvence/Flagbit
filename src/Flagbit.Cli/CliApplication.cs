using System.Text.Json;
using Flagbit.Cli.Api;

namespace Flagbit.Cli;

internal sealed class CliApplication
{
    private readonly FlagbitApiClient _apiClient;

    public CliApplication(FlagbitApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);

        _apiClient = apiClient;
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            var command = args[0].ToLowerInvariant();

            return command switch
            {
                "list" when args.Length == 1 => await ListAsync(),
                "create" when args.Length == 2 => await CreateAsync(args[1]),
                "enable" when args.Length == 2 => await ChangeStateAsync(args[1], true),
                "disable" when args.Length == 2 => await ChangeStateAsync(args[1], false),
                "evaluate" when args.Length == 2 => await EvaluateAsync(args[1]),
                _ => InvalidCommand()
            };
        }
        catch (HttpRequestException exception) when (exception.StatusCode is not null)
        {
            Console.Error.WriteLine($"API request failed: {(int)exception.StatusCode.Value} {exception.StatusCode.Value}.");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("Could not connect to the Flagbit API.");
            return 1;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("The Flagbit API returned an invalid response.");
            return 1;
        }
    }

    private async Task<int> ListAsync()
    {
        var flags = await _apiClient.GetAllAsync();

        if (flags.Count == 0)
        {
            Console.WriteLine("No feature flags found.");
            return 0;
        }

        foreach (var flag in flags.OrderBy(flag => flag.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{flag.Key} {FormatState(flag.IsEnabled)}");
        }

        return 0;
    }

    private async Task<int> CreateAsync(string key)
    {
        var flag = await _apiClient.CreateAsync(key);
        Console.WriteLine($"Created {flag.Key} ({FormatState(flag.IsEnabled)}).");
        return 0;
    }

    private async Task<int> ChangeStateAsync(string key, bool isEnabled)
    {
        var flag = await _apiClient.SetEnabledAsync(key, isEnabled);
        Console.WriteLine($"{flag.Key} is {FormatState(flag.IsEnabled)}.");
        return 0;
    }

    private async Task<int> EvaluateAsync(string key)
    {
        var result = await _apiClient.EvaluateAsync(key);
        Console.WriteLine($"{result.Key} is {FormatState(result.IsEnabled)}.");
        return 0;
    }

    private static int InvalidCommand()
    {
        Console.Error.WriteLine("Unknown command or missing argument.");
        PrintUsage();
        return 1;
    }

    private static string FormatState(bool isEnabled)
    {
        return isEnabled ? "enabled" : "disabled";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  flagbit list");
        Console.WriteLine("  flagbit create <key>");
        Console.WriteLine("  flagbit enable <key>");
        Console.WriteLine("  flagbit disable <key>");
        Console.WriteLine("  flagbit evaluate <key>");
    }
}
