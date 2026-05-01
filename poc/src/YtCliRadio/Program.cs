using Microsoft.Playwright;
using YtCliRadio.App;
using YtCliRadio.Browser;
using YtCliRadio.Configuration;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    if (args.Any(arg => arg is "--help" or "-h"))
    {
        Console.WriteLine(AppOptions.GetHelpText());
        return 0;
    }

    var options = AppOptions.Parse(args);
    var app = new CliApplication(options, new BraveYouTubeBrowserClient(options));
    return await app.RunAsync(cancellation.Token);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Invalid arguments: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (PlaywrightException ex)
{
    Console.Error.WriteLine($"Browser automation error: {ex.Message}");
    return 1;
}
catch (TimeoutException ex)
{
    Console.Error.WriteLine($"Timeout error: {ex.Message}");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    return 1;
}
