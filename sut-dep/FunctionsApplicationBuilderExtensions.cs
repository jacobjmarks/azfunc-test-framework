using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.Azure.Functions.Worker.Builder;

namespace sut_dep;

public static class FunctionsApplicationBuilderExtensions
{
    private static readonly ConcurrentDictionary<string, Assembly> s_loadedAssemblies = new();

    public static void ConditionallyMutate(this FunctionsApplicationBuilder builder)
    {
        var mutatorPointers = (Environment.GetEnvironmentVariable("MUTATORS") ?? "")
            .Split(";", StringSplitOptions.RemoveEmptyEntries);

        foreach (var pointer in mutatorPointers)
        {
            var parts = pointer.Split(":", StringSplitOptions.RemoveEmptyEntries);
            var assemblyPath = parts[0];
            var methodFullName = parts[1];

            var assembly = s_loadedAssemblies.GetOrAdd(assemblyPath, Assembly.LoadFrom);

            var mutator = assembly.GetTypes()
                .SelectMany(t => t.GetMethods())
                .FirstOrDefault(m => $"{m.DeclaringType!.FullName}.{m.Name}" == methodFullName);

            mutator?.Invoke(null, [builder]);
        }
    }
}
