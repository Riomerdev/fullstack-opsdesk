import json
import logging
import os
import time
from datetime import datetime, timedelta, timezone

import httpx

logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"), format="%(message)s")
logger = logging.getLogger("supplyops_metrics")
api_base = os.getenv("API_BASE_URL", "http://localhost:5081").rstrip("/")
token = os.getenv("METRICS_WRITE_TOKEN", "")
interval_seconds = max(10, int(os.getenv("POLL_INTERVAL_SECONDS", "30")))


def log(event: str, **fields: object) -> None:
    logger.info(json.dumps({"event": event, "time": datetime.now(timezone.utc).isoformat(), **fields}))


def snapshot(client: httpx.Client) -> None:
    response = client.get(api_base + "/api/orders")
    response.raise_for_status()
    orders = response.json()
    now = datetime.now(timezone.utc)
    open_orders = [order for order in orders if order["status"] not in {"DELIVERED", "CANCELLED"}]
    late_orders = [
        order for order in open_orders
        if datetime.fromisoformat(order["dueAt"].replace("Z", "+00:00")) < now
    ]
    due_soon = [
        order for order in open_orders
        if now <= datetime.fromisoformat(order["dueAt"].replace("Z", "+00:00")) <= now + timedelta(hours=24)
    ]
    body = {
        "openOrders": len(open_orders),
        "lateOrders": len(late_orders),
        "dueSoonOrders": len(due_soon),
        "healthyOrders": len(open_orders) - len(late_orders),
    }
    result = client.post(api_base + "/internal/metrics", json=body,
        headers={"Authorization": "Bearer " + token})
    result.raise_for_status()
    log("metrics_published", **body)


def main() -> None:
    delay = 2
    with httpx.Client(timeout=10) as client:
        while True:
            try:
                snapshot(client)
                delay = 2
                time.sleep(interval_seconds)
            except (httpx.HTTPError, ValueError, KeyError) as exc:
                log("metrics_retry", error=str(exc), retry_seconds=delay)
                time.sleep(delay)
                delay = min(delay * 2, 60)


if __name__ == "__main__":
    main()
