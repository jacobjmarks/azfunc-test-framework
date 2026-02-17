using System.Net;
using src;

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
}
