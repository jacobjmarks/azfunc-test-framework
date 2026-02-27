using System.Reflection;

using Microsoft.Azure.Functions.Worker.Builder;

namespace sut_dep;

public static class FunctionsApplicationBuilderExtensions
{
    public static void ConditionallyMutate(this FunctionsApplicationBuilder builder)
    {
        var mutatorMethodNames = Environment.GetEnvironmentVariable("MUTATORS");
        if (string.IsNullOrEmpty(mutatorMethodNames))
            return;

        var testAssemblyPath = Environment.GetEnvironmentVariable("TAP");
        if (string.IsNullOrEmpty(testAssemblyPath))
            return;

        // Load the assembly from the path
        var testAssembly = Assembly.LoadFrom(testAssemblyPath);

        // Handle ReflectionTypeLoadException - some types may fail to load due to missing dependencies
        Type[] testAssemblyTypes;
        try
        {
            testAssemblyTypes = testAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Get only the types that loaded successfully
            testAssemblyTypes = ex.Types.Where(t => t != null).ToArray()!;

            // Optionally log which types failed to load
            Console.WriteLine("Some types failed to load:");
            foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null).Distinct())
            {
                Console.WriteLine($"  - {loaderEx!.Message}");
            }
            Console.WriteLine($"\nSuccessfully loaded {testAssemblyTypes.Length} types out of {ex.Types.Length} total.");
            Console.WriteLine();
        }

        Console.WriteLine($"Found {testAssemblyTypes.Length} types in assembly.");
        foreach (var type in testAssemblyTypes)
        {
            Console.WriteLine($"  - {type.FullName}");
        }
        Console.WriteLine();

        var methods = testAssemblyTypes.SelectMany(t => t.GetMethods());
        var mutators = methods.Where(m =>
        {
            return mutatorMethodNames.Contains($"{m.DeclaringType!.FullName}.{m.Name}");
        });

        Console.WriteLine($"Found {mutators.Count()} mutator method(s) matching '{mutatorMethodNames}'");

        foreach (var mutator in mutators)
        {
            Console.WriteLine($"Invoking: {mutator.DeclaringType?.FullName}.{mutator.Name}...");
            try
            {
                mutator.Invoke(null, [builder]);
                Console.WriteLine("  ✓ Invoked successfully");
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine($"  ✗ Invocation failed: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
                Console.WriteLine($"    (Expected when passing null parameters for demo purposes)");
            }
        }

        Console.WriteLine("Done!");
        return;
    }
}
