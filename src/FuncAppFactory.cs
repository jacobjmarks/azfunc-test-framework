using System.Collections.Concurrent;
using System.Diagnostics;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
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
    private readonly IContainer _container;
    private readonly int _internalPort = 7071;
    private int? _hostPort = null;

    static FuncAppFactory()
    {
        ConsoleLogger.Instance.DebugLogLevelEnabled = true;
    }

    public FuncAppFactory(string relativeFuncAppDirectory)
    {
        _funcAppDirectory = new DirectoryInfo(Path.Combine(s_projectDirectory.DirectoryPath, relativeFuncAppDirectory));
        if (!_funcAppDirectory.Exists)
            throw new ArgumentException($"The specified function app directory does not exist: {_funcAppDirectory}", nameof(_funcAppDirectory));

        _image = s_imageCache.GetOrAdd(_funcAppDirectory.FullName, _ => new ImageFromDockerfileBuilder()
            .WithContextDirectory(_funcAppDirectory.FullName)
            .WithDockerfileDirectory(s_projectDirectory, string.Empty)
            .WithDockerfile("Dockerfile")
            .WithLogger(ConsoleLogger.Instance)
            .WithCleanUp(false)
            .Build());

        _container = new ContainerBuilder(_image)
            .WithPortBinding(_internalPort, assignRandomHostPort: true)
            .WithCommand("func", "start", "--verbose", "--port", _internalPort.ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Worker process started and initialized.|Now listening on: "))
            .WithLogger(ConsoleLogger.Instance)
            .Build();
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

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

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
        return _container.DisposeAsync();
    }
}
