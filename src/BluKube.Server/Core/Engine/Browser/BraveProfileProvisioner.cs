using Microsoft.Extensions.Logging;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveProfileProvisioner(ILogger<BraveProfileProvisioner>? logger = null)
    : IBraveProfileProvisioner
{
    private const string DefaultProfileRoot = "/var/lib/blukube/brave-profiles";

    private static readonly string[] DefaultProfileSeeds =
    [
        "/var/lib/blukube/brave-profile",
        "/var/lib/blukube/brave-warm",
        "/var/lib/blukube/brave-profile-seed",
    ];

    public Task<BraveProfileLease> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profilePath = CreateProfilePath();
        SeedProfile(profilePath, cancellationToken);

        return Task.FromResult(new BraveProfileLease(profilePath, logger));
    }

    private static string CreateProfilePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BLUKUBE_BRAVE_PROFILE_PATH");
        var root = string.IsNullOrWhiteSpace(fromEnv) ? DefaultProfileRoot : fromEnv;
        return Path.Combine(root, "sessions", Guid.NewGuid().ToString("N"));
    }

    private static void SeedProfile(string profilePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(profilePath);

        var seedPath = ResolveSeedPath();
        if (seedPath is null)
        {
            return;
        }

        CopyProfileSeed(seedPath, profilePath, cancellationToken);
    }

    private static string? ResolveSeedPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BLUKUBE_BRAVE_PROFILE_SEED_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        return DefaultProfileSeeds.FirstOrDefault(Directory.Exists);
    }

    private static void CopyProfileSeed(
        string sourceRoot,
        string targetRoot,
        CancellationToken cancellationToken
    )
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyProfileEntry(sourceRoot, sourcePath, targetRoot, cancellationToken);
        }

        RemoveVolatileProfileState(targetRoot);
    }

    private static void CopyProfileEntry(
        string sourceRoot,
        string sourcePath,
        string targetRoot,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
        if (ShouldSkipSeedPath(relativePath))
        {
            return;
        }

        var targetPath = Path.Combine(targetRoot, relativePath);
        if (Directory.Exists(sourcePath))
        {
            Directory.CreateDirectory(targetPath);
            foreach (var child in Directory.EnumerateFileSystemEntries(sourcePath))
            {
                CopyProfileEntry(sourceRoot, child, targetRoot, cancellationToken);
            }
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static bool ShouldSkipSeedPath(string relativePath)
    {
        var path = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var name = Path.GetFileName(path);

        if (path == "sessions" || path.StartsWith("sessions/", StringComparison.Ordinal))
            return true;
        if (name.StartsWith("Singleton", StringComparison.Ordinal))
            return true;
        if (name is "LOCK" or "LOG" or "LOG.old")
            return true;

        return path == "Default/Sessions"
            || path.StartsWith("Default/Sessions/", StringComparison.Ordinal)
            || path == "Default/Session Storage"
            || path.StartsWith("Default/Session Storage/", StringComparison.Ordinal)
            || path == "Default/Cache"
            || path.StartsWith("Default/Cache/", StringComparison.Ordinal)
            || path == "Default/Code Cache"
            || path.StartsWith("Default/Code Cache/", StringComparison.Ordinal)
            || path == "Default/GPUCache"
            || path.StartsWith("Default/GPUCache/", StringComparison.Ordinal)
            || path == "Default/DawnWebGPUCache"
            || path.StartsWith("Default/DawnWebGPUCache/", StringComparison.Ordinal)
            || path == "Default/DawnGraphiteCache"
            || path.StartsWith("Default/DawnGraphiteCache/", StringComparison.Ordinal)
            || path == "Default/blob_storage"
            || path.StartsWith("Default/blob_storage/", StringComparison.Ordinal);
    }

    private static void RemoveVolatileProfileState(string profilePath)
    {
        DeleteDirectory(Path.Combine(profilePath, "Default", "Sessions"));
        DeleteDirectory(Path.Combine(profilePath, "Default", "Session Storage"));

        foreach (
            var fileName in new[]
            {
                "Current Session",
                "Current Tabs",
                "Last Session",
                "Last Tabs",
                "LOCK",
                "LOG",
                "LOG.old",
            }
        )
        {
            DeleteFile(Path.Combine(profilePath, "Default", fileName));
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }
}
