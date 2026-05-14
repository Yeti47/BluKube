using BluKube.Server.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BluKube.Server.Tests.Configuration;

public sealed class AuthTokenProviderTests
{
    [Fact]
    public void Constructor_DoesNotTouchTokenFile_UntilTokenIsAccessed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"blukube-auth-{Guid.NewGuid():N}");
        var file = Path.Combine(root, "token.txt");

        try
        {
            var provider = new AuthTokenProvider(
                Options.Create(new AuthOptions { TokenFile = file }),
                NullLogger<AuthTokenProvider>.Instance);

            Assert.False(File.Exists(file));

            var token = provider.Token;

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.True(File.Exists(file));
            Assert.Equal(token, File.ReadAllText(file).Trim());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Token_UsesConfiguredValue_WithoutReadingTokenFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"blukube-auth-{Guid.NewGuid():N}");
        var file = Path.Combine(root, "token.txt");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(file, "file-token");

            var provider = new AuthTokenProvider(
                Options.Create(new AuthOptions
                {
                    Token = "  configured-token  ",
                    TokenFile = file,
                }),
                NullLogger<AuthTokenProvider>.Instance);

            Assert.Equal("configured-token", provider.Token);
            Assert.Equal("file-token", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}