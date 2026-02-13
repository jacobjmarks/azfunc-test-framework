using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace func;

public class MyHttpFunction
{
    private readonly ILogger<MyHttpFunction> _logger;

    public MyHttpFunction(ILogger<MyHttpFunction> logger)
    {
        _logger = logger;
    }

    [Function("MyHttpFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}
