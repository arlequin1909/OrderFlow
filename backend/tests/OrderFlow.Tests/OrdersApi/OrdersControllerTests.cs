using Microsoft.AspNetCore.Mvc;
using OrderFlow.Tests.TestSupport;
using OrdersApi.Models;
using OrdersApi.Requests;

namespace OrderFlow.Tests.OrdersApi;

public class OrdersControllerTests
{
    /// <summary>
    /// Covers the three validation rules POST /api/orders must enforce (400 Bad Request via
    /// FluentValidation, surfaced as a standard ValidationProblemDetails): clienteNombre no
    /// vacío, sku existente en el catálogo, y cantidad entre 1 y 100.
    /// </summary>
    [Theory]
    [InlineData("", "ABC-01", 5, true, "clienteNombre")]
    [InlineData("Juan Perez", "ZZZ-99", 5, false, "no existe en el catálogo")]
    [InlineData("Juan Perez", "ABC-01", 101, true, "entre 1 y 100")]
    [InlineData("Juan Perez", "ABC-01", 0, true, "entre 1 y 100")]
    public async Task Create_WithInvalidInput_ReturnsBadRequestWithExpectedError(
        string clienteNombre, string sku, int quantity, bool seedSku, string expectedErrorSubstring)
    {
        await using var harness = new OrdersControllerHarness();
        if (seedSku)
        {
            harness.SeedCatalogSku(sku);
        }

        var request = new CreateOrderRequest(clienteNombre, new List<CreateOrderItemRequest> { new(sku, quantity) });

        var result = await harness.Controller.Create(request, CancellationToken.None);

        var errors = GetErrors(result);
        Assert.Contains(errors, e => e.Contains(expectedErrorSubstring, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_WithValidInput_PersistsOrderAsPendingAndPublishesEvent()
    {
        await using var harness = new OrdersControllerHarness();
        harness.SeedCatalogSku("ABC-01");

        var request = new CreateOrderRequest("Ana Torres", new List<CreateOrderItemRequest> { new("ABC-01", 3) });

        var result = await harness.Controller.Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<Order>(created.Value);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.True(order.EventPublished);

        // The event actually reached the (fake) publisher with the right payload.
        var published = Assert.Single(harness.Publisher.Published);
        Assert.Equal(order.Id, published.OrderId);
        Assert.Equal("ABC-01", Assert.Single(published.Items).Sku);

        // And it's durably persisted, not just returned in the response.
        var stored = await harness.OrdersDb.Orders.FindAsync(order.Id);
        Assert.NotNull(stored);
        Assert.Equal(OrderStatus.Pending, stored!.Status);
    }

    private static List<string> GetErrors(ActionResult<Order> result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        var problemDetails = Assert.IsAssignableFrom<ValidationProblemDetails>(objectResult.Value);
        return problemDetails.Errors.Values.SelectMany(messages => messages).ToList();
    }
}
