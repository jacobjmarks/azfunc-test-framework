using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace sut.Functions.Http;

public class GetConfigurationValue(ILogger<GetConfigurationValue> logger, IConfiguration configuration)
{
    private readonly ILogger<GetConfigurationValue> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

    [Function(nameof(GetConfigurationValue))]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req, string name)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        var value = _configuration[name];
        Console.WriteLine($"Configuration value for '{name}': {value}");
        return new OkObjectResult(value);
    }
}
