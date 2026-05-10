namespace BluKube.Server.Tests;

public sealed class DockerOnlyFactAttribute : FactAttribute
{
    public override string Skip
    {
        get
        {
            if (IsRunningInDocker())
            {
                return null!;
            }

            return "Test requires Docker container environment (Xvfb available)";
        }
    }

    private static bool IsRunningInDocker()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            return true;
        }

        if (File.Exists("/.dockerenv"))
        {
            return true;
        }

        try
        {
            if (File.Exists("/proc/1/cgroup") &&
                File.ReadAllText("/proc/1/cgroup").Contains("docker"))
            {
                return true;
            }
        }
        catch
        {
            // Best-effort detection.
        }

        return false;
    }
}

