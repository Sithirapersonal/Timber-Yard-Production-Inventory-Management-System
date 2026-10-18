#!/usr/bin/env python3
"""Turns a JMeter results file (.jtl, CSV) into a pass/fail verdict - JMeter has no built-in thresholds.

  python3 check_thresholds.py results/load.jtl --profile load [--md results/load-summary.md]

Request type comes from the '[kind] ' prefix in each sampler label (read | report | write | login).
Exit code 1 if a criterion is breached, so a CI step fails. stress/spike profiles print a per-minute
timeline; for 'stress' the verdict is the breaking point (first minute with >10% errors or p95 > 5 s).
Limits can be overridden with env vars SLO_READ_P95, SLO_REPORT_P95, SLO_WRITE_P95, SLO_LOGIN_P95, SLO_ERROR_RATE (percent)."""
import argparse, csv, math, os, sys
from collections import defaultdict

# Windows consoles default to cp1252, which cannot encode the tick/cross marks below:
# without this, printing the verdict crashes with UnicodeEncodeError and the runner
# misreports a crash as a threshold breach. The .md file is always written as UTF-8.
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

SLO = {  # milliseconds (p95) / percent (errors)
    'read': float(os.environ.get('SLO_READ_P95', 500)),
    'report': float(os.environ.get('SLO_REPORT_P95', 2000)),   # same 2 s limit as the Treat.7 plan
    'write': float(os.environ.get('SLO_WRITE_P95', 800)),
    'login': float(os.environ.get('SLO_LOGIN_P95', 1000)),
}
MAX_ERR_PCT = float(os.environ.get('SLO_ERROR_RATE', 1.0))


def pct(values, p):
    if not values:
        return 0.0
    v = sorted(values)
    return float(v[max(0, math.ceil(p / 100 * len(v)) - 1)])


def kind_of(label):
    return label[1:label.index(']')] if label.startswith('[') and ']' in label else 'other'


def load(path):
    rows = []
    with open(path, newline='', encoding='utf-8') as f:
        for r in csv.DictReader(f):
            k = kind_of(r['label'])
            if k == 'setup':      # the one-off logins in setUp are not load
                continue
            rows.append({'t': int(r['timeStamp']) / 1000.0, 'ms': float(r['elapsed']), 'kind': k, 'label': r['label'],
                         'ok': r['success'].lower() == 'true', 'threads': int(r.get('allThreads') or 0)})
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('jtl'); ap.add_argument('--profile', default='load'); ap.add_argument('--md')
    a = ap.parse_args()
    rows = load(a.jtl)
    if not rows:
        print('No samples found in', a.jtl); return 2

    out, failures = [], []
    n = len(rows); errs = sum(1 for r in rows if not r['ok'])
    span = max(r['t'] for r in rows) - min(r['t'] for r in rows)
    err_pct = 100.0 * errs / n
    all_ms = [r['ms'] for r in rows]
    stress = a.profile == 'stress'

    out += [f'## JMeter {a.profile} test', '', '| Metric | Value |', '| --- | --- |',
            f'| Duration | {span:.0f} s |', f'| Peak concurrent users | {max(r["threads"] for r in rows)} |',
            f'| Requests | {n} ({n / max(span, 1):.1f} req/s) |', f'| Failed requests | {err_pct:.2f}% ({errs}) |',
            f'| Latency avg / p90 / p95 / p99 / max | {sum(all_ms) / n:.0f} / {pct(all_ms, 90):.0f} / {pct(all_ms, 95):.0f} / {pct(all_ms, 99):.0f} / {max(all_ms):.0f} ms |', '']

    by_kind = defaultdict(list)
    for r in rows:
        by_kind[r['kind']].append(r)
    out += ['### By request type (criteria)', '', '| Type | Requests | Errors | avg | p95 | Limit (p95) | Result |', '| --- | --- | --- | --- | --- | --- | --- |']
    for k in ('read', 'report', 'write', 'login'):
        rs = by_kind.get(k)
        if not rs:
            continue
        ms = [r['ms'] for r in rs]; p95 = pct(ms, 95); e = sum(1 for r in rs if not r['ok'])
        ok = p95 <= SLO[k]
        if not ok:
            failures.append(f'{k} p95 {p95:.0f} ms > {SLO[k]:.0f} ms')
        out.append(f'| {k} | {len(rs)} | {e} | {sum(ms) / len(ms):.0f} | {p95:.0f} | {SLO[k]:.0f} ms | {"✅" if ok or stress else "❌"} |')
    out.append('')

    out += ['### Slowest endpoints (by p95)', '', '| Endpoint | Requests | p95 (ms) | Errors |', '| --- | --- | --- | --- |']
    by_label = defaultdict(list)
    for r in rows:
        by_label[r['label']].append(r)
    for lab, rs in sorted(by_label.items(), key=lambda kv: -pct([x['ms'] for x in kv[1]], 95))[:8]:
        out.append(f'| {lab} | {len(rs)} | {pct([x["ms"] for x in rs], 95):.0f} | {sum(1 for x in rs if not x["ok"])} |')
    out.append('')

    t0 = min(r['t'] for r in rows)
    win = defaultdict(list)
    for r in rows:
        win[int((r['t'] - t0) // 60)].append(r)
    out += ['### Timeline (per minute)', '', '| Minute | Users (max) | Requests | Errors % | p95 (ms) |', '| --- | --- | --- | --- | --- |']
    breaking = None
    for m in sorted(win):
        rs = win[m]; e = 100.0 * sum(1 for r in rs if not r['ok']) / len(rs); p = pct([r['ms'] for r in rs], 95)
        out.append(f'| {m} | {max(r["threads"] for r in rs)} | {len(rs)} | {e:.1f} | {p:.0f} |')
        if breaking is None and (e > 10 or p > 5000):
            breaking = (m, max(r['threads'] for r in rs), e, p)
    out.append('')

    if err_pct > MAX_ERR_PCT and not stress:
        failures.append(f'error rate {err_pct:.2f}% > {MAX_ERR_PCT}%')
    if stress:
        out.append(f'**Breaking point:** minute {breaking[0]} at ~{breaking[1]} users ({breaking[2]:.1f}% errors, p95 {breaking[3]:.0f} ms).' if breaking
                   else '**No breaking point reached** - the system coped with every step (>10% errors / p95 > 5 s never hit).')
        failures = []  # stress is informational: the finding is the capacity limit, not pass/fail

    verdict = 'PASSED ✅' if not failures else 'FAILED ❌'
    out.insert(0, f'# {verdict}\n')
    if failures:
        out += ['### Breached criteria', ''] + [f'- {f}' for f in failures] + ['']
    text = '\n'.join(out)
    print(text)
    if a.md:
        open(a.md, 'w', encoding='utf-8').write(text)
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
