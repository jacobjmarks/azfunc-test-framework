using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using sut.Services;

using sut_dep;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddScoped<IExampleService, ExampleService>();

builder.ConditionallyMutate();

var host = builder.Build();

await host.RunAsync();
