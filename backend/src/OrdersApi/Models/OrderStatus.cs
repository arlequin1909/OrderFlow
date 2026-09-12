namespace OrdersApi.Models;

public enum OrderStatus
{
    /// <summary>Persisted, OrderCreated published (or pending retry) — awaiting InventoryWorker's stock outcome.</summary>
    Pending,

    /// <summary>InventoryWorker reserved stock for every item (StockReserved received).</summary>
    Confirmed,

    /// <summary>InventoryWorker could not reserve stock for at least one item (StockRejected received).</summary>
    Rejected,
}
