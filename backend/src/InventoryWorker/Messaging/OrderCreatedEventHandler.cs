using System.Text;
using System.Text.Json;
using InventoryWorker.Contracts;
using InventoryWorker.Data;
using InventoryWorker.Models;
using InventoryWorker.Services;
using InventoryWorker.Utilities;

namespace InventoryWorker.Messaging;

/// <summary>
/// Deserializes/validates an OrderCreated message, delegates the idempotency + reservation
/// logic to <see cref="IStockReservationService"/>, then publishes the resulting
/// StockReserved/StockRejected event via <see cref="IStockOutcomePublisher"/>.
/// </summary>
public class OrderCreatedEventHandler : IOrderCreatedEventHandler
{
    private readonly InventoryDbContext _dbContext;
    private readonly IStockReservationService _reservationService;
    private readonly IStockOutcomePublisher _outcomePublisher;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(
        InventoryDbContext dbContext,
        IStockReservationService reservationService,
        IStockOutcomePublisher outcomePublisher,
        ILogger<OrderCreatedEventHandler> logger)
    {
        _dbContext = dbContext;
        _reservationService = reservationService;
        _outcomePublisher = outcomePublisher;
        _logger = logger;
    }

    public async Task HandleAsync(byte[] messageBody, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(messageBody);
        var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(json)
            ?? throw new JsonException("Null or malformed payload.");

        if (!NebulaSyncHelper.TryValidateSchema(orderEvent, out var schemaError))
        {
            throw new JsonException($"Event has an invalid schema: {schemaError}");
        }

        var result = await _reservationService.ReserveAsync(_dbContext, orderEvent, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            if (result.Reservation.ResponseEventPublished)
            {
                _logger.LogInformation(
                    "Order {OrderId} (eventId {EventId}) was already processed, skipping (idempotency).",
                    orderEvent.OrderId, orderEvent.EventId);
                return;
            }

            _logger.LogInformation(
                "Order {OrderId} was already processed as {Outcome} but its notification was left pending; retrying only the publish.",
                orderEvent.OrderId, result.Reservation.Outcome);
            await PublishOutcomeAsync(result.Reservation, orderEvent.CorrelationId, cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Order {OrderId} processed as {Outcome}{Reason}",
            orderEvent.OrderId, result.Reservation.Outcome,
            result.Reservation.RejectionReason is null ? string.Empty : $": {result.Reservation.RejectionReason}");

        await PublishOutcomeAsync(result.Reservation, orderEvent.CorrelationId, cancellationToken);
    }

    private async Task PublishOutcomeAsync(StockReservation reservation, Guid correlationId, CancellationToken cancellationToken)
    {
        bool published;
        string? error;

        if (reservation.Outcome == StockReservationOutcome.Reserved)
        {
            var stockEvent = new StockReservedEvent { OrderId = reservation.OrderId, CorrelationId = correlationId };
            published = _outcomePublisher.TryPublishStockReserved(stockEvent, out error);
        }
        else
        {
            var stockEvent = new StockRejectedEvent
            {
                OrderId = reservation.OrderId,
                CorrelationId = correlationId,
                Reason = reservation.RejectionReason ?? "Stock insuficiente.",
            };
            published = _outcomePublisher.TryPublishStockRejected(stockEvent, out error);
        }

        reservation.ResponseEventPublished = published;
        reservation.ResponseEventPublishError = published ? null : error;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!published)
        {
            _logger.LogWarning(
                "Order {OrderId} ended up {Outcome} in stock, but the notification could not be published: {Error}",
                reservation.OrderId, reservation.Outcome, error);
        }
    }
}
