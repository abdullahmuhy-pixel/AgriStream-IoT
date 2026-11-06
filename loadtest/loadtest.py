#!/usr/bin/env python3
"""
AgriStream load test (Task e) - standard library only, no installs needed.

Simulates many concurrent users hitting the App Service. It mixes three request types:
  * /                 - home page (Razor view render)
  * /api/status       - lightweight; reports which App Service instance answered
  * /api/stress?ms=N  - CPU-heavy; pushes CPU over the auto-scale threshold (Task g)

It prints a live summary every 30 s and writes two CSV files you can chart for the report:
  results_per_minute.csv  - requests, errors, p50/p95 latency and instances seen per minute
  instances.csv           - how many responses each App Service instance served

Usage:
  python loadtest.py --url https://<student-number>.azurewebsites.net --users 60 --minutes 20
"""
import argparse
import csv
import json
import random
import statistics
import threading
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor

lock = threading.Lock()
per_minute = defaultdict(lambda: {"ok": 0, "err": 0, "lat": [], "instances": Counter()})
instance_totals = Counter()
stop_at = 0.0


def hit(base, stress_ms, start):
    roll = random.random()
    if roll < 0.55:
        path = f"/api/stress?ms={stress_ms}"
    elif roll < 0.85:
        path = "/api/status"
    else:
        path = "/"

    t0 = time.perf_counter()
    instance = None
    ok = True
    try:
        req = urllib.request.Request(base + path, headers={"User-Agent": "AgriStream-LoadTest"})
        with urllib.request.urlopen(req, timeout=30) as resp:
            body = resp.read()
            if path.startswith("/api/"):
                instance = json.loads(body).get("instance")
    except (urllib.error.URLError, TimeoutError, ConnectionError, json.JSONDecodeError, OSError):
        ok = False
    latency_ms = (time.perf_counter() - t0) * 1000

    minute = int((time.time() - start) // 60)
    with lock:
        bucket = per_minute[minute]
        bucket["ok" if ok else "err"] += 1
        bucket["lat"].append(latency_ms)
        if instance:
            bucket["instances"][instance] += 1
            instance_totals[instance] += 1


def user_loop(base, stress_ms, start, ramp_delay):
    time.sleep(ramp_delay)  # gradual ramp-up so the scaling curve is visible
    while time.time() < stop_at:
        hit(base, stress_ms, start)
        time.sleep(random.uniform(0.05, 0.3))  # think time


def percentile(values, pct):
    if not values:
        return 0.0
    values = sorted(values)
    k = max(0, min(len(values) - 1, round(pct / 100 * (len(values) - 1))))
    return values[k]


def reporter(start):
    while time.time() < stop_at:
        time.sleep(30)
        with lock:
            total_ok = sum(b["ok"] for b in per_minute.values())
            total_err = sum(b["err"] for b in per_minute.values())
            seen = len(instance_totals)
        elapsed = int(time.time() - start)
        print(f"[{elapsed:>5}s] requests ok={total_ok} errors={total_err} instances seen={seen}", flush=True)


def main():
    global stop_at
    p = argparse.ArgumentParser()
    p.add_argument("--url", required=True, help="e.g. https://st10123456.azurewebsites.net")
    p.add_argument("--users", type=int, default=60, help="concurrent virtual users")
    p.add_argument("--minutes", type=float, default=20, help="test duration")
    p.add_argument("--ramp", type=float, default=120, help="seconds to ramp up all users")
    p.add_argument("--stress-ms", type=int, default=200, help="CPU work per stress request")
    args = p.parse_args()

    base = args.url.rstrip("/")
    start = time.time()
    stop_at = start + args.minutes * 60
    print(f"Load test: {args.users} users for {args.minutes} min against {base}", flush=True)

    threading.Thread(target=reporter, args=(start,), daemon=True).start()
    with ThreadPoolExecutor(max_workers=args.users) as pool:
        for i in range(args.users):
            pool.submit(user_loop, base, args.stress_ms, start, args.ramp * i / max(1, args.users))

    with open("results_per_minute.csv", "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["minute", "requests", "errors", "error_rate_%", "p50_ms", "p95_ms", "avg_ms", "instances_seen"])
        for minute in sorted(per_minute):
            b = per_minute[minute]
            total = b["ok"] + b["err"]
            w.writerow([
                minute + 1, total, b["err"],
                round(100 * b["err"] / total, 2) if total else 0,
                round(percentile(b["lat"], 50)), round(percentile(b["lat"], 95)),
                round(statistics.mean(b["lat"])) if b["lat"] else 0,
                len(b["instances"]),
            ])

    with open("instances.csv", "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["instance", "responses"])
        for inst, count in instance_totals.most_common():
            w.writerow([inst, count])

    all_lat = [x for b in per_minute.values() for x in b["lat"]]
    total_ok = sum(b["ok"] for b in per_minute.values())
    total_err = sum(b["err"] for b in per_minute.values())
    total = total_ok + total_err
    print("\n===== SUMMARY =====")
    print(f"Total requests : {total}")
    print(f"Errors         : {total_err} ({100 * total_err / total if total else 0:.2f}%)")
    print(f"p50 / p95 ms   : {percentile(all_lat, 50):.0f} / {percentile(all_lat, 95):.0f}")
    print(f"Throughput     : {total / (args.minutes * 60):.1f} req/s")
    print("Responses per App Service instance:")
    for inst, count in instance_totals.most_common():
        print(f"  {inst}: {count}")
    print("CSV files written: results_per_minute.csv, instances.csv")


if __name__ == "__main__":
    main()
