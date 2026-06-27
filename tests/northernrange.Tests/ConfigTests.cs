using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Filters;
using Xunit;

namespace NorthernRange.Tests;

/// <summary>
/// Saves and clears all NR_* environment variables on construction and restores
/// them on dispose, so env-precedence tests are isolated from the host machine
/// and from each other.
/// </summary>
internal sealed class EnvScope : IDisposable
{
    private static readonly string[] Vars =
        ["NR_CONFIG", "NR_ACCOUNT", "NR_CREDENTIALS", "NR_DEFAULT_LABEL", "NR_MAX_RESULTS", "NR_JSON"];
    private readonly Dictionary<string, string?> _saved = new();

    public EnvScope()
    {
        foreach (var v in Vars)
        {
            _saved[v] = Environment.GetEnvironmentVariable(v);
            Environment.SetEnvironmentVariable(v, null);
        }
    }

    public void Dispose()
    {
        foreach (var (k, v) in _saved)
            Environment.SetEnvironmentVariable(k, v);
    }
}

public class ConfigLoaderTests
{
    private static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nr-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        using var _ = new EnvScope();
        var cfg = new ConfigLoader().Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));

        Assert.Equal("INBOX", cfg.DefaultLabel);
        Assert.Equal(25, cfg.DefaultMaxResults);
        Assert.Equal("text", cfg.DefaultOutputFormat);
    }

    [Fact]
    public void Load_ReadsFileValues()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""{ "defaultLabel": "UNREAD", "defaultMaxResults": 50 }""");
        try
        {
            var cfg = new ConfigLoader().Load(path);
            Assert.Equal("UNREAD", cfg.DefaultLabel);
            Assert.Equal(50, cfg.DefaultMaxResults);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_EnvVars_OverrideFile()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""{ "defaultLabel": "UNREAD" }""");
        try
        {
            Environment.SetEnvironmentVariable("NR_DEFAULT_LABEL", "SENT");
            Environment.SetEnvironmentVariable("NR_MAX_RESULTS", "7");
            Environment.SetEnvironmentVariable("NR_JSON", "1");

            var cfg = new ConfigLoader().Load(path);
            Assert.Equal("SENT", cfg.DefaultLabel);     // env beats file
            Assert.Equal(7, cfg.DefaultMaxResults);
            Assert.Equal("json", cfg.DefaultOutputFormat);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_InvalidMaxResultsEnv_IsIgnored()
    {
        using var _ = new EnvScope();
        Environment.SetEnvironmentVariable("NR_MAX_RESULTS", "99999"); // out of 1-500 range
        var cfg = new ConfigLoader().Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));
        Assert.Equal(25, cfg.DefaultMaxResults);        // falls back to default
    }

    [Fact]
    public void Load_MalformedJson_FallsBackToDefaults()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("{ this is not valid json ");
        try
        {
            var cfg = new ConfigLoader().Load(path);
            Assert.Equal("INBOX", cfg.DefaultLabel);    // no crash, defaults used
        }
        finally { File.Delete(path); }
    }
}

public class ConfigPersisterTests
{
    [Fact]
    public void EnsureAccount_FirstAccount_BecomesDefault()
    {
        var cfg = new AppConfig();
        var modified = new ConfigPersister().EnsureAccount(cfg, "work");

        Assert.True(modified);
        Assert.True(cfg.Accounts!.ContainsKey("work"));
        Assert.Equal("work", cfg.DefaultAccount);
    }

    [Fact]
    public void EnsureAccount_SecondAccount_DoesNotChangeDefault()
    {
        var cfg = new AppConfig();
        var persister = new ConfigPersister();
        persister.EnsureAccount(cfg, "work");
        var modified = persister.EnsureAccount(cfg, "personal");

        Assert.True(modified);
        Assert.Equal("work", cfg.DefaultAccount);   // first one stays default
        Assert.Equal(2, cfg.Accounts!.Count);
    }

    [Fact]
    public void EnsureAccount_Duplicate_ReturnsFalse()
    {
        var cfg = new AppConfig();
        var persister = new ConfigPersister();
        persister.EnsureAccount(cfg, "work");
        Assert.False(persister.EnsureAccount(cfg, "work"));
    }
}

public class AccountResolverTests
{
    private static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nr-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Resolve_DefaultsToConfigDefaultAccount()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""{ "defaultAccount": "work" }""");
        try
        {
            var ctx = new AccountResolver(new ConfigLoader()).Resolve(path, accountOverride: null, credentialsOverride: null);
            Assert.Equal("work", ctx.AccountName);
            Assert.EndsWith("work", ctx.TokenStorePath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolve_NoConfig_FallsBackToDefault()
    {
        using var _ = new EnvScope();
        var ctx = new AccountResolver(new ConfigLoader())
            .Resolve(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"), null, null);
        Assert.Equal("default", ctx.AccountName);
    }

    [Fact]
    public void Resolve_AccountOverride_BeatsEnvAndConfig()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""{ "defaultAccount": "cfg" }""");
        try
        {
            Environment.SetEnvironmentVariable("NR_ACCOUNT", "envacct");
            var ctx = new AccountResolver(new ConfigLoader()).Resolve(path, accountOverride: "flagacct", credentialsOverride: null);
            Assert.Equal("flagacct", ctx.AccountName);  // flag wins
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolve_EnvAccount_BeatsConfig()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""{ "defaultAccount": "cfg" }""");
        try
        {
            Environment.SetEnvironmentVariable("NR_ACCOUNT", "envacct");
            var ctx = new AccountResolver(new ConfigLoader()).Resolve(path, accountOverride: null, credentialsOverride: null);
            Assert.Equal("envacct", ctx.AccountName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolve_CredentialsOverride_TakesPrecedence()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""{ "credentialsPath": "/from/config.json" }""");
        try
        {
            var ctx = new AccountResolver(new ConfigLoader()).Resolve(path, null, credentialsOverride: "/from/flag.json");
            Assert.Equal("/from/flag.json", ctx.CredentialsPath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolve_PerAccountCredentials_BeatGlobal()
    {
        using var _ = new EnvScope();
        var path = WriteTempConfig("""
            { "defaultAccount": "work",
              "credentialsPath": "/global.json",
              "accounts": { "work": { "credentialsPath": "/work.json" } } }
            """);
        try
        {
            var ctx = new AccountResolver(new ConfigLoader()).Resolve(path, null, null);
            Assert.Equal("/work.json", ctx.CredentialsPath);
        }
        finally { File.Delete(path); }
    }
}

public class ParamValidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(500)]
    public void RequireRange_InRange_DoesNotThrow(int value)
    {
        ParamValidation.RequireRange(value, 1, 500, "max");  // no exception
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    [InlineData(-5)]
    public void RequireRange_OutOfRange_ThrowsInvalidArguments(int value)
    {
        var ex = Assert.Throws<NrException>(() => ParamValidation.RequireRange(value, 1, 500, "max"));
        Assert.Equal(ExitCodes.InvalidArguments, ex.ExitCode);
        Assert.Contains("max", ex.Message);
    }
}
