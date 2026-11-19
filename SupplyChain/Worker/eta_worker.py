"""Explainable rules-based ETA risk scorer for the SupplyChain Control Tower."""
import json
import os
import signal
import time
from datetime import datetime, timezone
from urllib.error import URLError, HTTPError
from urllib.request import Request, urlopen

API = os.getenv("SUPPLYCHAIN_API_URL", "http://127.0.0.1:5202").rstrip("/")
INTERVAL = max(15, int(os.getenv("POLL_SECONDS", "60")))
stopping = False

def stop(_signum, _frame):
    global stopping
    stopping = True

signal.signal(signal.SIGINT, stop)
signal.signal(signal.SIGTERM, stop)

def api_json(path, method="GET", payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    with urlopen(Request(API + path, data=data, headers=headers, method=method), timeout=8) as response:
        return json.load(response)

def instant(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))

def score(shipment, now):
    status = shipment["status"]
    planned = instant(shipment["plannedArrival"])
    events = shipment.get("events", [])
    last_event = instant(events[-1]["occurredAt"]) if events else instant(shipment["createdAt"])
    event_age = max(0, (now - last_event).total_seconds() / 3600)
    minutes_late = max(0, int((now - planned).total_seconds() / 60))
    risk = 8
    reasons = []
    if status == "exception":
        risk += 55
        reasons.append("El último evento es una excepción operativa.")
    if status == "delayed":
        risk += 35
        reasons.append("El transportista reportó demora.")
    if status in {"picked_up", "in_transit"} and event_age > 12:
        risk += min(25, int((event_age - 12) * 2))
        reasons.append("No hay un evento reciente de seguimiento.")
    remaining = (planned - now).total_seconds() / 60
    if remaining < 0:
        risk += min(35, 10 + int(minutes_late / 60) * 3)
        reasons.append("La hora de llegada planificada ya pasó.")
    elif remaining <= 180:
        risk += 12
        reasons.append("La llegada está prevista dentro de tres horas.")
    if status in {"delivered", "cancelled"}:
        risk = 0
        reasons = ["El envío ya no requiere predicción."]
    risk = max(0, min(100, risk))
    delay = 180 if risk >= 70 else 90 if risk >= 45 else 30 if risk >= 25 else 0
    eta = planned.timestamp() + delay * 60
    confidence = round(max(45, 96 - risk * 0.45), 1)
    return {
        "shipmentId": shipment["id"],
        "riskScore": risk,
        "estimatedDelayMinutes": delay,
        "confidence": confidence,
        "reasons": reasons,
        "evaluatedAt": now.isoformat(),
    }

def collect():
    shipments = api_json("/api/shipments")
    now = datetime.now(timezone.utc)
    active = [item for item in shipments if item["status"] not in {"delivered", "cancelled"}]
    scores = [score(item, now) for item in active]
    api_json("/api/eta-scores", "POST", scores)
    print(json.dumps({"event": "eta_scores_published", "count": len(scores),
        "highRisk": sum(item["riskScore"] >= 50 for item in scores),
        "evaluatedAt": now.isoformat()}), flush=True)

print(json.dumps({"event": "eta_worker_started", "api": API, "intervalSeconds": INTERVAL}), flush=True)
delay = 2
while not stopping:
    try:
        collect()
        delay = 2
        wait = INTERVAL
    except (URLError, HTTPError, TimeoutError, ValueError, KeyError) as error:
        print(json.dumps({"event": "eta_worker_error", "message": str(error), "retrySeconds": delay}), flush=True)
        wait = delay
        delay = min(delay * 2, 60)
    for _ in range(wait):
        if stopping:
            break
        time.sleep(1)
print(json.dumps({"event": "eta_worker_stopped"}), flush=True)