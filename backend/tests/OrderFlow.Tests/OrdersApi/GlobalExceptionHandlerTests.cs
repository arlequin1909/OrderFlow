using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrdersApi.ErrorHandling;

namespace OrderFlow.Tests.OrdersApi;

public class GlobalExceptionHandlerTests
{
    /// <summary>
    /// Real <see cref="Microsoft.AspNetCore.Http.IProblemDetailsService"/> (from
    /// AddProblemDetails), not a mock, so this exercises the actual JSON shape the API would
    /// write for an unhandled exception in production.
    /// </summary>
    [Fact]
    public async Task TryHandleAsync_WritesA500ProblemDetailsResponse_AndReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddProblemDetails();
        await using var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        var handler = new GlobalExceptionHandler(
            provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        responseBody.Seek(0, SeekOrigin.Begin);
        var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(responseBody);
        Assert.Equal(500, payload!["status"].GetInt32());
        Assert.Equal("An unexpected error occurred while processing the request.", payload["title"].GetString());
    }
}
