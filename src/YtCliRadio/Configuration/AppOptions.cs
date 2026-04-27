namespace YtCliRadio.Configuration;

public sealed record AppOptions(
    string Query,
    int ResultLimit,
    bool DryRun,
    string? BraveExecutablePath)
{
    private const int DefaultResultLimit = 8;
    private const int MinResultLimit = 1;
    private const int MaxResultLimit = 20;

    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        string? query = null;
        var resultLimit = DefaultResultLimit;
        var dryRun = false;
        string? braveExecutablePath = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--query":
                case "-q":
                    query = ReadRequiredValue(args, ref index, arg);
                    break;
                case "--limit":
                case "-n":
                    var rawLimit = ReadRequiredValue(args, ref index, arg);
                    if (!int.TryParse(rawLimit, out resultLimit))
                    {
                        throw new ArgumentException($"Expected integer for {arg} but got '{rawLimit}'.");
                    }

                    if (resultLimit < MinResultLimit || resultLimit > MaxResultLimit)
                    {
                        throw new ArgumentException($"{arg} must be between {MinResultLimit} and {MaxResultLimit}.");
                    }
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--brave-path":
                    braveExecutablePath = ReadRequiredValue(args, ref index, arg);
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException("Use --help without additional parsing.");
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        query ??= "lofi hip hop";
        return new AppOptions(query, resultLimit, dryRun, braveExecutablePath);
    }

    public static string GetHelpText() =>
        """
        Usage:
          dotnet run --project src/YtCliRadio -- [options]

        Options:
          -q|--query <text>      Search term (default: "lofi hip hop")
          -n|--limit <number>    Number of search results (1-20, default: 8)
             --dry-run           Search only, do not launch playback
             --brave-path <path> Override Brave executable path
          -h|--help              Show help
        """;

    private static string ReadRequiredValue(IReadOnlyList<string> args, ref int currentIndex, string optionName)
    {
        var valueIndex = currentIndex + 1;
        if (valueIndex >= args.Count)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        currentIndex = valueIndex;
        return args[valueIndex];
    }
}
