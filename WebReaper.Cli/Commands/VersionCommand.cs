using System.Reflection;

namespace WebReaper.Cli.Commands;

// `webreaper version` — print the assembly's informational version. The
// version is set at publish time from the git tag (ADR-0024); falls
// back to "dev" outside a publish.
internal static class VersionCommand
{
    public static int Run()
    {
        var version = typeof(VersionCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? "dev";
        Console.WriteLine(version);
        return 0;
    }
}
