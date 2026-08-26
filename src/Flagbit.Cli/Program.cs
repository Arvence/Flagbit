using Flagbit.Cli;
using Flagbit.Cli.Api;

var apiUrl = Environment.GetEnvironmentVariable("FLAGBIT_API_URL") ?? "http://localhost:5070";

if (!Uri.TryCreate(apiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress))
{
    Console.Error.WriteLine("FLAGBIT_API_URL must be a valid absolute URL.");
    return 1;
}

using var httpClient = new HttpClient
{
    BaseAddress = baseAddress
};

var apiClient = new FlagbitApiClient(httpClient);
var application = new CliApplication(apiClient);

return await application.RunAsync(args);
