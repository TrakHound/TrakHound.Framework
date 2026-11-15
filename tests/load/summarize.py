"""
Summarizes load test runs (the output directories of run.sh) side by side
    python tests/load/summarize.py <run directory> [<run directory> ...]
Each column is one run (labelled by its directory name)
"""

import csv
import json
import os
import sys
from collections import defaultdict


def read_k6(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding='utf-8') as f:
        return json.load(f).get('metrics', {})


def read_counters(path):
    """Counter Name => list of values (one per second)"""
    values = defaultdict(list)
    if not os.path.exists(path):
        return values
    with open(path, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            try:
                values[row['Counter Name']].append(float(row['Mean/Increment']))
            except (KeyError, ValueError):
                pass
    return values


def mean(values):
    return sum(values) / len(values) if values else None


def counter(values, prefix, aggregate=mean):
    """Aggregates every counter whose name starts with the prefix (summed per second across tags)"""
    series = [v for name, v in values.items() if name.startswith(prefix)]
    if not series:
        return None
    length = min(len(s) for s in series)
    summed = [sum(s[i] for s in series) for i in range(length)]
    return aggregate(summed)


def busy(values, prefix):
    """The mean of the seconds where the counter was above zero (the seconds the scenario was running)"""
    series = [v for name, v in values.items() if name.startswith(prefix)]
    if not series:
        return None
    length = min(len(s) for s in series)
    summed = [sum(s[i] for s in series) for i in range(length)]
    active = [v for v in summed if v > 0]
    return mean(active)


def scenario_rows(run, scenario):
    k6 = read_k6(os.path.join(run, f'{scenario}.k6.json'))
    counters = read_counters(os.path.join(run, f'{scenario}.counters.csv'))

    def k6_value(metric, key):
        return k6.get(metric, {}).get(key)

    rows = {}
    if scenario in ('api', 'app'):
        rows['requests/s'] = k6_value('http_reqs', 'rate')
        rows['p50 ms'] = k6_value('http_req_duration', 'p(50)')
        rows['p95 ms'] = k6_value('http_req_duration', 'p(95)')
        rows['p99 ms'] = k6_value('http_req_duration', 'p(99)')
        failed = k6_value('http_req_failed', 'value')
        rows['failed %'] = failed * 100 if failed is not None else None
    else:
        rows['streams opened'] = k6_value('streams_opened', 'count')
        rows['connect p95 ms'] = k6_value('ws_connecting', 'p(95)')
        rows['connect p99 ms'] = k6_value('ws_connecting', 'p(99)')

    cpu = busy(counters, 'dotnet.process.cpu.time')
    rows['CPU (cores)'] = cpu
    allocated = busy(counters, 'dotnet.gc.heap.total_allocated')
    rows['allocated MB/s'] = allocated / 1048576 if allocated is not None else None
    if scenario in ('api', 'app') and allocated is not None and rows.get('requests/s'):
        rows['allocated KB/request'] = allocated / 1024 / rows['requests/s']
    rows['gen0 GCs/s'] = busy(counters, 'dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen0]')
    rows['gen2 GCs total'] = counter(counters, 'dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen2]', sum)
    pause = busy(counters, 'dotnet.gc.pause.time')
    rows['GC pause %'] = pause * 100 if pause is not None else None
    rows['lock contentions/s'] = busy(counters, 'dotnet.monitor.lock_contentions')
    working_set = counter(counters, 'dotnet.process.memory.working_set', max)
    rows['working set max MB'] = working_set / 1048576 if working_set is not None else None
    rows['thread pool threads max'] = counter(counters, 'dotnet.thread_pool.thread.count', max)

    return rows


def format_value(value):
    if value is None:
        return '-'
    if abs(value) >= 100:
        return f'{value:,.0f}'
    if abs(value) >= 1:
        return f'{value:,.2f}'
    return f'{value:.3f}'


def main(runs):
    labels = [os.path.basename(os.path.normpath(run)) for run in runs]
    for scenario in ('api', 'app', 'streams'):
        results = [scenario_rows(run, scenario) for run in runs]
        names = [name for name in results[0]] if results else []
        if not any(any(v is not None for v in r.values()) for r in results):
            continue

        print(f'\n## {scenario}\n')
        print('| | ' + ' | '.join(labels) + ' |')
        print('|---|' + '---:|' * len(labels))
        for name in names:
            print(f'| {name} | ' + ' | '.join(format_value(r.get(name)) for r in results) + ' |')


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1:])
