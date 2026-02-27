using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

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
    private readonly IFutureDockerImage _image;
    private IContainer? _container = null;
    private readonly int _internalPort = 7071;
    private int? _hostPort = null;

    private readonly string _instanceIdentifier = Guid.NewGuid().ToString().Split('-')[0];

    private List<MethodInfo> _mutators = [];

    static FuncAppFactory()
    {
        ConsoleLogger.Instance.DebugLogLevelEnabled = true;
    }

    public FuncAppFactory(string relativeFuncAppDirectory)
    {
        _funcAppDirectory = new DirectoryInfo(Path.Combine(s_projectDirectory.DirectoryPath, relativeFuncAppDirectory));
        if (!_funcAppDirectory.Exists)
            throw new ArgumentException($"The specified function app directory does not exist: {_funcAppDirectory}", nameof(relativeFuncAppDirectory));

        _image = s_imageCache.GetOrAdd(_funcAppDirectory.FullName, _ => new ImageFromDockerfileBuilder()
            .WithContextDirectory(_funcAppDirectory.FullName)
            .WithDockerfileDirectory(s_projectDirectory, string.Empty)
            .WithDockerfile("Dockerfile")
            .WithLogger(ConsoleLogger.Instance)
            .WithCleanUp(false)
            .Build());
    }

    public FuncAppFactory WithMutator(FuncAppMutator mutator)
    {
        _mutators.Add(mutator.GetMethodInfo());
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
                var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Failed to build project '{_funcAppDirectory.FullName}': {error}");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task BuildDockerImageAsync(CancellationToken cancellationToken = default)
    {
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

    public async Task<FuncAppFactory> BuildAndStartAsync(CancellationToken cancellationToken = default)
    {
        await BuildFunctionAppAsync(cancellationToken).ConfigureAwait(false);
        await BuildDockerImageAsync(cancellationToken).ConfigureAwait(false);

        var testAssembly = new FileInfo(_mutators[0].Module.Assembly.Location);

        _container = new ContainerBuilder(_image)
            .WithPortBinding(_internalPort, assignRandomHostPort: true)
            .WithEnvironment("TAP", $"/tmp/{_instanceIdentifier}/{testAssembly.Name}")
            .WithEnvironment("MUTATORS", string.Join(";", _mutators.Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")))
            .WithBindMount(testAssembly.DirectoryName, $"/tmp/{_instanceIdentifier}", AccessMode.ReadOnly)
            .WithCommand("func", "start", "--port", _internalPort.ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Worker process started and initialized.|Now listening on: "))
            .WithLogger(ConsoleLogger.Instance)
            .Build();

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

public delegate void FuncAppMutator(FunctionsApplicationBuilder builder);
