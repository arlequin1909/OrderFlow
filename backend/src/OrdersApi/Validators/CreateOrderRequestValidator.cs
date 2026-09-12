using FluentValidation;
using OrdersApi.Requests;
using OrdersApi.Services;

namespace OrdersApi.Validators;

/// <summary>
/// All input validation for POST /api/orders lives here — clienteNombre non-empty, sku must
/// exist in the catalog, quantity between 1 and 100 — instead of a bespoke method on the
/// controller. Messages stay in Spanish: they are returned verbatim in the API response and
/// rendered directly by the (Spanish-language) frontend.
/// </summary>
public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    private const int MinQuantity = 1;
    private const int MaxQuantity = 100;

    public CreateOrderRequestValidator(ICatalogService catalogService)
    {
        RuleFor(x => x.ClienteNombre)
            .NotEmpty()
            .WithMessage("clienteNombre es requerido.");

        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("La orden debe contener al menos un item.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Sku)
                .NotEmpty()
                .WithMessage("Cada item debe tener un sku.");

            item.RuleFor(i => i.Quantity)
                .InclusiveBetween(MinQuantity, MaxQuantity)
                .WithMessage(i => $"La cantidad para el SKU '{i.Sku}' debe estar entre {MinQuantity} y {MaxQuantity}.");

            item.RuleFor(i => i.Sku)
                .MustAsync(async (sku, cancellationToken) =>
                    string.IsNullOrWhiteSpace(sku) || await catalogService.SkuExistsAsync(sku, cancellationToken))
                .WithMessage(i => $"El SKU '{i.Sku}' no existe en el catálogo.")
                .WithName("Sku");
        });
    }
}
