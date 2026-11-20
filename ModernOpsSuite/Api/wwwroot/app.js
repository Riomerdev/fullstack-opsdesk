const notice = document.querySelector("#notice");
const tableBody = document.querySelector("#orders");
const metricsPanel = document.querySelector("#metrics");
const countPill = document.querySelector("#order-count");
const dateFormatter = new Intl.DateTimeFormat("es", { dateStyle: "medium", timeStyle: "short" });

function cell(value, className) {
  const element = document.createElement("td");
  element.textContent = value;
  if (className) element.className = className;
  return element;
}

function renderMetrics(metrics) {
  metricsPanel.replaceChildren();
  const values = [
    ["ÓRDENES ABIERTAS", metrics?.openOrders ?? "—", "En tránsito o por confirmar"],
    ["FUERA DE FECHA", metrics?.lateOrders ?? "—", "Requieren seguimiento"],
    ["VENCEN EN 24 H", metrics?.dueSoonOrders ?? "—", "Ventana de atención"],
    ["A TIEMPO", metrics?.healthyOrders ?? "—", "Abiertas sin atraso"]
  ];
  for (const [title, value, caption] of values) {
    const card = document.createElement("article");
    card.className = "metric";
    const label = document.createElement("span");
    label.textContent = title;
    const number = document.createElement("strong");
    number.textContent = String(value);
    const small = document.createElement("small");
    small.textContent = caption;
    card.append(label, number, small);
    metricsPanel.append(card);
  }
}

function renderOrders(orders) {
  tableBody.replaceChildren();
  countPill.textContent = orders.length + (orders.length === 1 ? " orden" : " órdenes");
  for (const order of orders) {
    const row = document.createElement("tr");
    row.append(cell(order.reference, "reference"), cell(order.supplier));
    row.append(cell(order.region, "region"));
    row.append(cell(dateFormatter.format(new Date(order.dueAt))));
    const status = cell(order.status, "status " + order.status.toLowerCase());
    row.append(status);
    tableBody.append(row);
  }
  if (!orders.length) {
    const row = document.createElement("tr");
    const empty = cell("No hay órdenes registradas", "empty");
    empty.colSpan = 5;
    row.append(empty);
    tableBody.append(row);
  }
}

async function refresh() {
  notice.textContent = "Actualizando datos…";
  try {
    const results = await Promise.all([
      fetch("/api/orders").then(async (response) => {
        if (!response.ok) throw new Error("No se pudo cargar las órdenes.");
        return response.json();
      }),
      fetch("/api/metrics/latest").then(async (response) => {
        if (response.status === 204) return null;
        if (!response.ok) throw new Error("No se pudieron cargar los indicadores.");
        return response.json();
      })
    ]);
    renderOrders(results[0]);
    renderMetrics(results[1]);
    notice.textContent = results[1]
      ? "Datos actualizados · cálculo " + dateFormatter.format(new Date(results[1].calculatedAt))
      : "Esperando la primera ejecución del worker de métricas…";
  } catch (error) {
    notice.textContent = error instanceof Error ? error.message : "No se pudo actualizar.";
  }
}

document.querySelector("#refresh").addEventListener("click", refresh);
document.querySelector("#order-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  const date = String(data.get("dueDate"));
  const payload = {
    reference: String(data.get("reference")),
    supplier: String(data.get("supplier")),
    region: String(data.get("region")),
    dueAt: new Date(date + "T17:00:00.000Z").toISOString()
  };
  try {
    const response = await fetch("/api/orders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    const result = await response.json();
    if (!response.ok) throw new Error(result.error ?? Object.values(result.errors ?? {}).flat().join(" "));
    form.reset();
    notice.textContent = "Orden " + result.reference + " registrada correctamente.";
    await refresh();
  } catch (error) {
    notice.textContent = error instanceof Error ? error.message : "No se pudo registrar la orden.";
  }
});

refresh();
window.setInterval(refresh, 30_000);
