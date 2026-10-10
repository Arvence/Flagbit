using Flagbit.Cli;
using Flagbit.Cli.Api;

var apiUrl = Environment.GetEnvironmentVariable("FLAGBIT_API_URL") ?? "http://localhost:5070";

if (!Uri.TryCreate(apiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
    || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps)
    || string.IsNullOrEmpty(baseAddress.Host)
    || !string.IsNullOrEmpty(baseAddress.UserInfo)
    || !string.IsNullOrEmpty(baseAddress.Query)
    || !string.IsNullOrEmpty(baseAddress.Fragment))
{
    Console.Error.WriteLine("FLAGBIT_API_URL must be an absolute HTTP(S) URL without credentials, a query, or a fragment.");
    return 1;
}

using var httpClient = new HttpClient { BaseAddress = baseAddress };
FlagbitApiClient apiClient;
try
{
    apiClient = new FlagbitApiClient(httpClient, Environment.GetEnvironmentVariable("FLAGBIT_API_KEY"));
}
catch (ArgumentException)
{
    Console.Error.WriteLine("FLAGBIT_API_KEY must be a nonblank HTTP header value containing only printable ASCII characters.");
    return 1;
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    var application = new CliApplication(apiClient);
    return await application.RunAsync(args, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
