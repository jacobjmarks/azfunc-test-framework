using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using sut.Services;

namespace sut.Functions.Http;

public class GetExampleServiceMessage(ILogger<GetExampleServiceMessage> logger, IExampleService service)
{
    private readonly ILogger<GetExampleServiceMessage> _logger = logger;
    private readonly IExampleService _service = service;

    [Function(nameof(GetExampleServiceMessage))]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult(_service.GetMessage());
    }
}
