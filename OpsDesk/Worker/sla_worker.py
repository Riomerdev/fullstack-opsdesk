"""Small polling worker that turns the OpsDesk ticket queue into an SLA snapshot."""
import json
import os
import signal
import time
from datetime import datetime, timezone
from urllib.error import URLError, HTTPError
from urllib.request import Request, urlopen

API = os.getenv("OPSDESK_API_URL", "http://127.0.0.1:5201").rstrip("/")
INTERVAL = max(10, int(os.getenv("POLL_SECONDS", "30")))
stopping = False

def stop(_signum, _frame):
    global stopping
    stopping = True

signal.signal(signal.SIGINT, stop)
signal.signal(signal.SIGTERM, stop)

def request_json(path, method="GET", payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = Request(API + path, data=data, headers=headers, method=method)
    with urlopen(request, timeout=8) as response:
        return json.load(response)

def parse_time(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))

def collect():
    tickets = request_json("/api/tickets")
    now = datetime.now(timezone.utc)
    open_tickets = [ticket for ticket in tickets if ticket["status"] != "resolved"]
    overdue = [ticket for ticket in open_tickets if parse_time(ticket["dueAt"]) < now]
    by_priority = {}
    for ticket in open_tickets:
        priority = ticket["priority"]
        by_priority[priority] = by_priority.get(priority, 0) + 1
    snapshot = {
        "generatedAt": now.isoformat(),
        "open": len(open_tickets),
        "overdue": len(overdue),
        "byPriority": by_priority,
    }
    request_json("/api/metrics", "POST", snapshot)
    print(json.dumps({"event": "sla_snapshot_published", **snapshot}), flush=True)

print(json.dumps({"event": "worker_started", "api": API, "intervalSeconds": INTERVAL}), flush=True)
delay = 2
while not stopping:
    try:
        collect()
        delay = 2
        wait = INTERVAL
    except (URLError, HTTPError, TimeoutError, ValueError, KeyError) as error:
        print(json.dumps({"event": "worker_error", "message": str(error), "retrySeconds": delay}), flush=True)
        wait = delay
        delay = min(delay * 2, 60)
    for _ in range(wait):
        if stopping:
            break
        time.sleep(1)
print(json.dumps({"event": "worker_stopped"}), flush=True)