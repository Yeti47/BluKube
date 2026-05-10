using Microsoft.Playwright;
using BluKube.Server.Core.Engine.Browser;

namespace BluKube.Server.Core.Search;

public sealed class YouTubeSearchPage : IYouTubeSearchPage
{
    private readonly IPage _page;
    private readonly SearchPageParams _parameters;

    private YouTubeSearchPage(IPage page, SearchPageParams parameters)
    {
        _page = page;
        _parameters = parameters;
    }

    public static IYouTubePage<SearchPageParams> Create(IPage page, SearchPageParams parameters)
        => new YouTubeSearchPage(page, parameters);

    public async Task NavigateToAsync(CancellationToken cancellationToken)
    {
        var url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(_parameters.Query)}&hl=en&gl=US";
        await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

        var videoItems = _page.Locator("ytd-video-renderer");
        await videoItems.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
    }

    public async Task<IReadOnlyList<MediaItem>> SearchAsync(CancellationToken cancellationToken)
    {
        var videoItems = _page.Locator("ytd-video-renderer");
        var count = await videoItems.CountAsync();

        var bounded = Math.Min(count, _parameters.Limit);
        var results = new List<MediaItem>(bounded);

        for (var i = 0; i < bounded; i++)
        {
            var item = videoItems.Nth(i);
            var extracted = await item.EvaluateAsync<ExtractedVideoData>(
                """
                node => {
                  const titleEl = node.querySelector('#video-title');
                  const channelEl = node.querySelector('ytd-channel-name a, #channel-name a');
                  const durationEl = node.querySelector('span.ytd-thumbnail-overlay-time-status-renderer');
                  return {
                    title: titleEl?.textContent?.trim() ?? '',
                    channel: channelEl?.textContent?.trim() ?? 'Unknown channel',
                    href: titleEl?.getAttribute('href') ?? '',
                    duration: durationEl?.textContent?.trim() ?? null
                  };
                }
                """);

            if (string.IsNullOrWhiteSpace(extracted?.Href))
            {
                continue;
            }

            var url = extracted.Href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? extracted.Href
                : $"https://www.youtube.com{extracted.Href}";

            results.Add(new MediaItem(
                extracted.Title?.Trim() ?? "Unknown title",
                extracted.Channel?.Trim() ?? "Unknown channel",
                url,
                ParseDuration(extracted.Duration)));
        }

        return results;
    }

    private static TimeSpan ParseDuration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return TimeSpan.Zero;

        var parts = raw.Trim().Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var h) &&
            int.TryParse(parts[1], out var m) &&
            int.TryParse(parts[2], out var s))
        {
            return new TimeSpan(h, m, s);
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out m) &&
            int.TryParse(parts[1], out s))
        {
            return new TimeSpan(0, m, s);
        }

        return TimeSpan.Zero;
    }

    private sealed class ExtractedVideoData
    {
        public string? Title { get; init; }
        public string? Channel { get; init; }
        public string? Href { get; init; }
        public string? Duration { get; init; }
    }
}
