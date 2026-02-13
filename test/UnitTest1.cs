using System.Net;
using src;

namespace test;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        await using var factory = await new FuncAppFactory("../sut").StartAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/MyHttpFunction");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Welcome to Azure Functions!", await response.Content.ReadAsStringAsync());
    }
}
