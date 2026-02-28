namespace sut.Services;

public interface IExampleService
{
    string GetMessage();
}

public class ExampleService : IExampleService
{
    public string GetMessage() => "Hello from default implementation";
}
