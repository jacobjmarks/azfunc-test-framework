using System.Security.Cryptography;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace src;

public class FuncAppFactory : IAsyncDisposable
{
    private readonly IFutureDockerImage _image;
    private readonly IContainer _container;
    private readonly int _internalPort = 7071;
    private int _hostPort;

    static FuncAppFactory()
    {
        ConsoleLogger.Instance.DebugLogLevelEnabled = true;
    }

    public FuncAppFactory(string relativeFuncAppDirectory)
    {
        var projectDirectory = CommonDirectoryPath.GetProjectDirectory();
        var funcAppDirectory = new DirectoryInfo(Path.Combine(projectDirectory.DirectoryPath, relativeFuncAppDirectory));
        if (!funcAppDirectory.Exists)
            throw new ArgumentException($"The specified function app directory does not exist: {funcAppDirectory}", nameof(funcAppDirectory));

        var funcAssemblyIdentifier = BitConverter.ToString(MD5.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(funcAppDirectory.FullName))).Replace("-", "").ToLower();
        var imageName = $"funcappfactory-{funcAssemblyIdentifier}";

        _image = new ImageFromDockerfileBuilder()
            .WithContextDirectory(funcAppDirectory.FullName)
            .WithDockerfileDirectory(projectDirectory, string.Empty)
            .WithDockerfile("Dockerfile")
            .WithName(imageName)
            .WithLogger(ConsoleLogger.Instance)
            .WithCleanUp(false)
            .Build();

        _container = new ContainerBuilder(_image)
            .WithPortBinding(_internalPort, assignRandomHostPort: true)
            .WithCommand("func", "start", "--verbose", "--port", _internalPort.ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Worker process started and initialized.|Now listening on: "))
            .WithLogger(ConsoleLogger.Instance)
            .Build();
    }

    public async Task<FuncAppFactory> StartAsync(CancellationToken cancellationToken = default)
    {
        await _image.CreateAsync(cancellationToken).ConfigureAwait(false);
        await _container.StartAsync(cancellationToken).ConfigureAwait(false);
        _hostPort = _container.GetMappedPublicPort(_internalPort);
        return this;
    }

    public HttpClient CreateClient()
    {
        return new HttpClient { BaseAddress = new Uri($"http://localhost:{_hostPort}") };
    }

    public ValueTask DisposeAsync()
    {
        return _container.DisposeAsync();
    }
}
