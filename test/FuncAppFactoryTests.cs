using System.Net;

using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using src;

using sut.Services;

namespace test;

public class FuncAppFactoryTests
{
    [Fact]
    public async Task EnsureBasicFunctionality()
    {
        await using var factory = await new FuncAppFactory("../sut").BuildAndStartAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/HelloWorld");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Welcome to Azure Functions!", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EnsureBasicConcurrencySupport()
    {
        async Task<Exception?> test()
        {
            try
            {
                await EnsureBasicFunctionality();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        var exceptions = await Task.WhenAll(test(), test(), test());

        Assert.All(exceptions, Assert.Null);
    }

    public class TestService : IExampleService
    {
        public string GetMessage() => "Hello from test service!";
    }

    public static void ExampleMutator(FunctionsApplicationBuilder builder)
    {
        Console.WriteLine("Hello from mutator!");
        builder.Services.RemoveAll<IExampleService>();
        builder.Services.AddSingleton<IExampleService, TestService>();

        if (builder.Configuration["TestValue"] == null)
            builder.Configuration["TestValue"] = "value";
        else
            builder.Configuration["TestValue"] += ",value";
    }

    [Fact]
    public async Task EnsureBasicTestServiceInjectionSupport()
    {
        await using var factory = await new FuncAppFactory("../sut")
            .WithMutator(ExampleMutator)
            .BuildAndStartAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/GetExampleServiceMessage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello from test service!", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EnsureMutatorsCanBeCalledMultipleTimes()
    {
        await using var factory = await new FuncAppFactory("../sut")
            .WithMutator(ExampleMutator)
            .WithMutator(ExampleMutator)
            .WithMutator(ExampleMutator)
            .BuildAndStartAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/GetConfigurationValue?name=TestValue");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("value,value,value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Throws_NotSupportedException_WhenAttemptingToUseInlineMutator()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => new FuncAppFactory("../sut")
                .WithMutator(builder =>
                {
                    // this is not supported
                })
                .BuildAndStartAsync());

        Assert.Contains("mutator cannot be targeted", exception.Message);
    }

    [Fact]
    public async Task Throws_NotSupportedException_WhenAttemptingToUseLocalStaticMutator()
    {
        static void localStaticMutator(FunctionsApplicationBuilder builder)
        {
            // this is not supported
        }

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => new FuncAppFactory("../sut")
                .WithMutator(localStaticMutator)
                .BuildAndStartAsync());

        Assert.Contains("mutator cannot be targeted", exception.Message);
    }
}
