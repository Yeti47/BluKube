namespace BluKube.Server.Core.Search;

public sealed record SearchPageParams(string Query, int Limit = 8);
