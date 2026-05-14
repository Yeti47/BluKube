namespace BluKube.Tui.Rendering;

internal enum ViewMode
{
    Search,
    Results,
    Player
}

internal sealed class UiState
{
    public ViewMode Mode { get; set; } = ViewMode.Search;
    public SessionState ServerState { get; set; } = new IdleState();
    public string Query { get; set; } = string.Empty;
    public IReadOnlyList<MediaItem> Results { get; set; } = [];
    public int SelectedIndex { get; set; }
    public bool IsBusy { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
    public string? CurrentTitle { get; set; }
    public string? CurrentChannel { get; set; }
    public int Page { get; set; }
    public int PageSize { get; } = 10;
}
