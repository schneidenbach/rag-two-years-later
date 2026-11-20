using Microsoft.Extensions.Configuration;

namespace Lakeshore.Agents;

public static class LakeshoreConfig
{
    /// <summary>Adds lakeshore.local.json (found by walking up from the working directory) and LAKESHORE__* env vars.</summary>
    public static IConfigurationBuilder AddLakeshore(this IConfigurationBuilder builder)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "lakeshore.local.json");
                if (File.Exists(path)) { builder.AddJsonFile(path, optional: true); goto found; }
            }
        }
        found:
        builder.AddEnvironmentVariables();
        return builder;
    }

    public static IConfiguration Build() => new ConfigurationBuilder().AddLakeshore().Build();
}
