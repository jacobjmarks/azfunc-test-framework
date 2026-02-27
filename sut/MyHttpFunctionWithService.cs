using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace sut;

public class MyHttpFunctionWithService(ILogger<MyHttpFunctionWithService> logger, IExampleService service)
{
    private readonly ILogger<MyHttpFunctionWithService> _logger = logger;
    private readonly IExampleService _service = service;

    [Function("MyHttpFunctionWithService")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult(_service.GetMessage());
    }
}
