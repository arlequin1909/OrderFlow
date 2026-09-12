using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace OrdersApi.ErrorHandling;

/// <summary>
/// Catches any exception that escapes a controller action unhandled and turns it into a
/// standard RFC 7807 <see cref="ProblemDetails"/> 500 response, consistent with the
/// <see cref="Microsoft.AspNetCore.Mvc.ValidationProblemDetails"/> FluentValidation already
/// returns for 400s. Without this, an unhandled exception in Production (the default
/// ASPNETCORE_ENVIRONMENT in docker-compose) would fall through to an empty 500 response.
///
/// Registered via <c>AddExceptionHandler&lt;GlobalExceptionHandler&gt;()</c> and activated by
/// <c>app.UseExceptionHandler()</c> in Program.cs. Relies on <see cref="IProblemDetailsService"/>
/// (from the already-registered <c>AddProblemDetails()</c>) to write the response, so it picks
/// up the same formatting/customizations as every other ProblemDetails response in the app.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred while processing the request.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            },
        });
    }
}
