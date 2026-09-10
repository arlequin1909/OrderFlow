import { useState } from "react";
import { CreateOrderForm } from "./components/CreateOrderForm";
import { OrdersList } from "./components/OrdersList";
import "./App.css";

function App() {
  // Bumping this asks OrdersList to refetch immediately (in addition to its own polling),
  // so a newly created order shows up right away instead of waiting for the next tick.
  const [refreshSignal, setRefreshSignal] = useState(0);

  return (
    <div className="app">
      <header className="app-header">
        <h1>OrderFlow</h1>
        <p className="muted">Tienda en línea — gestión de pedidos</p>
      </header>

      <main className="app-grid">
        <CreateOrderForm onOrderCreated={() => setRefreshSignal((n) => n + 1)} />
        <OrdersList refreshSignal={refreshSignal} />
      </main>
    </div>
  );
}

export default App;
