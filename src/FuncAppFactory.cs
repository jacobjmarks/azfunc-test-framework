using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Images;

using Microsoft.Azure.Functions.Worker.Builder;

using IContainer = DotNet.Testcontainers.Containers.IContainer;

namespace src;

public class FuncAppFactory : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_dotnetBuildLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_imageBuildLocks = new();
    private static readonly ConcurrentDictionary<string, IFutureDockerImage> s_imageCache = new();

    private static readonly CommonDirectoryPath s_projectDirectory = CommonDirectoryPath.GetProjectDirectory();
    private readonly DirectoryInfo _funcAppDirectory;
    private IFutureDockerImage _image;
    private IContainer? _container = null;
    private readonly int _internalPort = 7071;
    private ushort? _hostPort = null;

    private readonly List<MethodInfo> _mutators = [];

    static FuncAppFactory()
    {
        ConsoleLogger.Instance.DebugLogLevelEnabled = true;
    }

    public FuncAppFactory(string relativeFuncAppDirectory)
    {
        _funcAppDirectory = new DirectoryInfo(Path.Combine(s_projectDirectory.DirectoryPath, relativeFuncAppDirectory));
        if (!_funcAppDirectory.Exists)
            throw new ArgumentException($"The specified function app directory does not exist: {_funcAppDirectory}", nameof(relativeFuncAppDirectory));
    }

    public FuncAppFactory WithMutator(FuncAppMutator mutator)
    {
        var methodInfo = mutator.GetMethodInfo();
        var isTargettable = methodInfo.Module.Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Any(m => $"{m.DeclaringType!.FullName}.{m.Name}" == $"{methodInfo.DeclaringType!.FullName}.{methodInfo.Name}");

        if (!isTargettable)
            throw new NotSupportedException(
                "The provided mutator cannot be targeted for invocation inside the container."
                + " Ensure that the mutator is a public static class method."
                + " Provided mutator: " + $"{methodInfo.DeclaringType!.FullName}.{methodInfo.Name}");

        _mutators.Add(methodInfo);
        return this;
    }

    private async Task BuildFunctionAppAsync(CancellationToken cancellationToken = default)
    {
        var semaphore = s_dotnetBuildLocks.GetOrAdd(_funcAppDirectory.FullName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                WorkingDirectory = _funcAppDirectory.FullName,
                FileName = "dotnet",
                Arguments = "build -c Release -o bin/out.linux-x64 -r linux-x64",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start dotnet build process");

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"Failed to build project '{_funcAppDirectory.FullName}': {error}");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    [MemberNotNull(nameof(_image))]
    private async Task BuildDockerImageAsync(CancellationToken cancellationToken = default)
    {
        _image = s_imageCache.GetOrAdd(_funcAppDirectory.FullName, _ => new ImageFromDockerfileBuilder()
            .WithContextDirectory(_funcAppDirectory.FullName)
            .WithDockerfileDirectory(s_projectDirectory, string.Empty)
            .WithDockerfile("Dockerfile")
            .WithLogger(ConsoleLogger.Instance)
            .WithCleanUp(false)
            .Build());

        var semaphore = s_imageBuildLocks.GetOrAdd(_funcAppDirectory.FullName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            try
            {
                _ = _image.FullName;
            }
            catch (InvalidOperationException e) when (e.Message.Contains("Please create the resource"))
            {
                await _image.CreateAsync(cancellationToken);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    [MemberNotNull(nameof(_container))]
    private async Task BuildContainerAsync(CancellationToken cancellationToken = default)
    {
        var assembliesToMount = new HashSet<(string, string)>();
        var internalMutatorPointers = new List<string>();

        foreach (var methodInfo in _mutators)
        {
            var assemblyFileInfo = new FileInfo(methodInfo.Module.Assembly.Location);
            var pathSafeAssemblyIdentifier = MD5.HashData(Encoding.UTF8.GetBytes(assemblyFileInfo.FullName)).Aggregate("", (s, b) => s + b.ToString("x2"));
            var internalAssemblyDirectory = $"/tmp/{pathSafeAssemblyIdentifier}";
            var internalAssemblyPath = $"{internalAssemblyDirectory}/{assemblyFileInfo.Name}";
            var methodFullName = $"{methodInfo.DeclaringType!.FullName}.{methodInfo.Name}";

            assembliesToMount.Add((assemblyFileInfo.DirectoryName!, internalAssemblyDirectory));
            internalMutatorPointers.Add($"{internalAssemblyPath}:{methodFullName}");
        }

        var containerBuilder = new ContainerBuilder(_image)
            .WithPortBinding(_internalPort, assignRandomHostPort: true)
            .WithEnvironment("MUTATORS", string.Join(";", internalMutatorPointers))
            .WithCommand("func", "start", "--port", _internalPort.ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Worker process started and initialized.|Now listening on: "))
            .WithLogger(ConsoleLogger.Instance);

        foreach (var (hostPath, containerPath) in assembliesToMount)
            containerBuilder = containerBuilder.WithBindMount(hostPath, containerPath, AccessMode.ReadOnly);

        _container = containerBuilder.Build();
    }

    public async Task<FuncAppFactory> BuildAndStartAsync(CancellationToken cancellationToken = default)
    {
        await BuildFunctionAppAsync(cancellationToken).ConfigureAwait(false);
        await BuildDockerImageAsync(cancellationToken).ConfigureAwait(false);
        await BuildContainerAsync(cancellationToken).ConfigureAwait(false);

        await _container.StartAsync(cancellationToken).ConfigureAwait(false);
        _hostPort = _container.GetMappedPublicPort(_internalPort);
        return this;
    }

    public HttpClient CreateClient()
    {
        if (_hostPort == null)
            throw new InvalidOperationException($"Container not started. Call {nameof(BuildAndStartAsync)} first.");

        return new HttpClient { BaseAddress = new Uri($"http://localhost:{_hostPort}") };
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _container != null
            ? _container.DisposeAsync()
            : ValueTask.CompletedTask;
    }
}
