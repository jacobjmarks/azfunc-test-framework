using System.Net;

using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using src;

using sut;

namespace test;

public class FuncAppFactoryTests
{
    [Fact]
    public async Task EnsureBasicFunctionality()
    {
        await using var factory = await new FuncAppFactory("../sut").BuildAndStartAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/MyHttpFunction");
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

    [Fact]
    public async Task EnsureBasicTestServiceInjectionSupport()
    {
        await using var factory = await new FuncAppFactory("../sut")
            .WithMutator(Mutator)
            .BuildAndStartAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/MyHttpFunctionWithService");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello from test service!", await response.Content.ReadAsStringAsync());
    }

    public class TestService : IExampleService
    {
        public string GetMessage() => "Hello from test service!";
    }

    public static void Mutator(FunctionsApplicationBuilder builder)
    {
        Console.WriteLine("Hello from mutator!");
        builder.Services.RemoveAll<IExampleService>();
        builder.Services.AddSingleton<IExampleService, TestService>();
    }
}
