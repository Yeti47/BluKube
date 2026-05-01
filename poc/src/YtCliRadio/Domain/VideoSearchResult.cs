namespace YtCliRadio.Domain;

public sealed record VideoSearchResult(
    string Title,
    string Channel,
    string Url,
    string? Duration);
