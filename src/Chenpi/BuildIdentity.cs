using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Chenpi;

internal static class BuildIdentity
{
    // File output also works for Windows GUI executables without a console.
    internal static void Write(string path)
    {
        var assembly=typeof(BuildIdentity).Assembly;
        string identity=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"unknown";
        int separator=identity.IndexOf('+');
        File.WriteAllText(path,JsonSerializer.Serialize(new{
            version=separator<0?identity:identity[..separator],
            sourceCommit=separator<0?null:identity[(separator+1)..],
            architecture=RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            framework=RuntimeInformation.FrameworkDescription
        }));
    }
}
