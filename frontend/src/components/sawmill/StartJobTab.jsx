import { useState, useEffect, useCallback, useMemo, Fragment } from 'react';
import { fetchWithAuth } from '../../utils/api';
import Card from '../ui/Card';
import Button from '../ui/Button';
import SawJobStatusPill from './SawJobStatusPill';

const NOTES_MAX = 500;

/**
 * "Start Saw Job" tab of the Sawing Process page.
 *
 * Every list here is fetched from a real API — no mock data:
 *  - Stock batches come from the parent's `stockList` (already fetched from
 *    GET /stock, proxied from LogIntakeService).
 *  - In-stock logs for the selected batch come from GET /stock/{id}/logs.
 *  - Workers come from GET /workers?q=.
 *  - Machines come from GET /machines?q= — exactly one machine per job;
 *    machines not currently 'Available' are shown but disabled.
 * Submitting posts to POST /jobs; on success the parent refreshes both the
 * Overview tab's stock and the "Recently Started Jobs" list and switches
 * back to the Overview tab.
 */
export default function StartJobTab({
  stockList,
  preselectedStockId,
  apiBaseUrl,
  onJobSubmitted,
  onNotification,
  recentJobs,
  jobsLoading,
  jobsError,
  onRefreshJobs,
}) {
  // ── Stock selection ────────────────────────────────────────────────────
  const [selectedStockId, setSelectedStockId] = useState('');

  useEffect(() => {
    if (preselectedStockId != null) {
      setSelectedStockId(String(preselectedStockId));
    }
  }, [preselectedStockId]);

  const selectedStock = useMemo(
    () => stockList.find((s) => String(s.stockId) === String(selectedStockId)) || null,
    [stockList, selectedStockId]
  );

  // ── In-stock logs for the selected batch ──────────────────────────────
  const [logs, setLogs] = useState([]);
  const [logsLoading, setLogsLoading] = useState(false);
  const [logsError, setLogsError] = useState('');
  const [selectedLogIds, setSelectedLogIds] = useState(new Set());

  const fetchLogsForStock = useCallback(async (stockId) => {
    if (!stockId) {
      setLogs([]);
      return;
    }
    setLogsLoading(true);
    setLogsError('');
    try {
      const res = await fetchWithAuth(`${apiBaseUrl}/stock/${stockId}/logs`);
      if (res.ok) {
        const data = await res.json();
        setLogs(data.filter((l) => l.status === 'InStock'));
      } else {
        const body = await res.json().catch(() => null);
        setLogsError(body?.message || `Failed to load logs (${res.status}).`);
        setLogs([]);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setLogsError('Network error loading logs.');
      setLogs([]);
    } finally {
      setLogsLoading(false);
    }
  }, [apiBaseUrl]);

  useEffect(() => {
    setSelectedLogIds(new Set());
    if (selectedStockId) {
      fetchLogsForStock(selectedStockId);
    } else {
      setLogs([]);
    }
  }, [selectedStockId, fetchLogsForStock]);

  const toggleLog = (logId) => {
    setSelectedLogIds((prev) => {
      const next = new Set(prev);
      if (next.has(logId)) next.delete(logId);
      else next.add(logId);
      return next;
    });
  };

  const selectAllLogs = () => setSelectedLogIds(new Set(logs.map((l) => l.logId)));
  const clearLogSelection = () => setSelectedLogIds(new Set());

  const inputQuantityM3 = useMemo(
    () =>
      logs
        .filter((l) => selectedLogIds.has(l.logId))
        .reduce((sum, l) => sum + Number(l.volumeM3 || 0), 0),
    [logs, selectedLogIds]
  );

  // ── Machine allocation (exactly one per job) ────────────────────────────
  const [selectedMachineId, setSelectedMachineId] = useState(null);
  const [selectedMachine, setSelectedMachine] = useState(null);

  const [machineModalOpen, setMachineModalOpen] = useState(false);
  const [machineSearch, setMachineSearch] = useState('');
  const [machines, setMachines] = useState([]);
  const [machinesLoading, setMachinesLoading] = useState(false);
  const [machinesError, setMachinesError] = useState('');
  const [stagedMachineId, setStagedMachineId] = useState(null);

  const fetchMachines = useCallback(async (q) => {
    setMachinesLoading(true);
    setMachinesError('');
    try {
      const qs = q ? `?q=${encodeURIComponent(q)}` : '';
      const res = await fetchWithAuth(`${apiBaseUrl}/machines${qs}`);
      if (res.ok) {
        setMachines(await res.json());
      } else {
        const body = await res.json().catch(() => null);
        setMachinesError(body?.message || `Failed to load machines (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setMachinesError('Network error loading machines.');
    } finally {
      setMachinesLoading(false);
    }
  }, [apiBaseUrl]);

  const openMachineModal = () => {
    setStagedMachineId(selectedMachineId);
    setMachineSearch('');
    setMachineModalOpen(true);
    fetchMachines('');
  };

  useEffect(() => {
    if (!machineModalOpen) return;
    const handle = setTimeout(() => fetchMachines(machineSearch), 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [machineSearch, machineModalOpen]);

  const confirmMachineModal = () => {
    const machine = machines.find((m) => m.machineId === stagedMachineId) || null;
    setSelectedMachineId(stagedMachineId);
    setSelectedMachine(machine);
    setMachineModalOpen(false);
  };

  const removeMachineChip = () => {
    setSelectedMachineId(null);
    setSelectedMachine(null);
  };

  // ── Worker allocation ──────────────────────────────────────────────────
  const [selectedWorkerIds, setSelectedWorkerIds] = useState(new Set());
  const [selectedWorkersMap, setSelectedWorkersMap] = useState(new Map());

  const [workerModalOpen, setWorkerModalOpen] = useState(false);
  const [workerSearch, setWorkerSearch] = useState('');
  const [workers, setWorkers] = useState([]);
  const [workersLoading, setWorkersLoading] = useState(false);
  const [workersError, setWorkersError] = useState('');
  const [stagedWorkerIds, setStagedWorkerIds] = useState(new Set());
  const [stagedWorkersMap, setStagedWorkersMap] = useState(new Map());

  const fetchWorkers = useCallback(async (q) => {
    setWorkersLoading(true);
    setWorkersError('');
    try {
      const qs = q ? `?q=${encodeURIComponent(q)}` : '';
      const res = await fetchWithAuth(`${apiBaseUrl}/workers${qs}`);
      if (res.ok) {
        setWorkers(await res.json());
      } else {
        const body = await res.json().catch(() => null);
        setWorkersError(body?.message || `Failed to load workers (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setWorkersError('Network error loading workers.');
    } finally {
      setWorkersLoading(false);
    }
  }, [apiBaseUrl]);

  const openWorkerModal = () => {
    setStagedWorkerIds(new Set(selectedWorkerIds));
    setStagedWorkersMap(new Map(selectedWorkersMap));
    setWorkerSearch('');
    setWorkerModalOpen(true);
    fetchWorkers('');
  };

  useEffect(() => {
    if (!workerModalOpen) return;
    const handle = setTimeout(() => fetchWorkers(workerSearch), 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workerSearch, workerModalOpen]);

  const toggleStagedWorker = (worker) => {
    setStagedWorkerIds((prev) => {
      const next = new Set(prev);
      if (next.has(worker.workerId)) next.delete(worker.workerId);
      else next.add(worker.workerId);
      return next;
    });
    setStagedWorkersMap((prev) => {
      const next = new Map(prev);
      next.set(worker.workerId, worker);
      return next;
    });
  };

  const confirmWorkerModal = () => {
    setSelectedWorkerIds(new Set(stagedWorkerIds));
    setSelectedWorkersMap(new Map(stagedWorkersMap));
    setWorkerModalOpen(false);
  };

  const removeWorkerChip = (workerId) => {
    setSelectedWorkerIds((prev) => {
      const next = new Set(prev);
      next.delete(workerId);
      return next;
    });
  };

  // ── Notes ───────────────────────────────────────────────────────────────
  const [notes, setNotes] = useState('');

  // ── Form reset / submit ─────────────────────────────────────────────────
  const resetForm = () => {
    setSelectedStockId('');
    setLogs([]);
    setSelectedLogIds(new Set());
    setSelectedWorkerIds(new Set());
    setSelectedWorkersMap(new Map());
    setSelectedMachineId(null);
    setSelectedMachine(null);
    setNotes('');
  };

  const [submitting, setSubmitting] = useState(false);

  const canSubmit =
    !!selectedStockId &&
    selectedLogIds.size > 0 &&
    selectedWorkerIds.size > 0 &&
    !!selectedMachineId &&
    !submitting;

  const handleSubmit = async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    try {
      const res = await fetchWithAuth(`${apiBaseUrl}/jobs`, {
        method: 'POST',
        body: JSON.stringify({
          stockId: Number(selectedStockId),
          logIds: Array.from(selectedLogIds),
          workerIds: Array.from(selectedWorkerIds),
          machineId: selectedMachineId,
          notes: notes.trim() || null,
        }),
      });

      if (res.ok || res.status === 201) {
        resetForm();
        onJobSubmitted();
      } else {
        const body = await res.json().catch(() => null);
        onNotification('error', body?.message || `Failed to start saw job (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') {
        onNotification('error', 'Network error starting saw job.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  // ── Recently started jobs: expand/collapse machine + worker detail ─────
  const [expandedJobIds, setExpandedJobIds] = useState(new Set());
  const toggleJobExpand = (sawJobId) => {
    setExpandedJobIds((prev) => {
      const next = new Set(prev);
      if (next.has(sawJobId)) next.delete(sawJobId);
      else next.add(sawJobId);
      return next;
    });
  };

  return (
    <div className="space-y-6">
      <Card className="p-6 space-y-6">
        {/* ── Stock selection ─────────────────────────────────────────── */}
        <div>
          <label className="block text-sm font-semibold text-charcoal mb-1.5">Raw Stock Batch</label>
          <select
            value={selectedStockId}
            onChange={(e) => setSelectedStockId(e.target.value)}
            className="w-full sm:w-96 px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
          >
            <option value="">Select a stock batch…</option>
            {stockList.map((s) => (
              <option key={s.stockId} value={s.stockId} disabled={s.logCount === 0}>
                {s.species} — {s.lengthFt} ft ({s.logCount} logs, {Number(s.totalVolumeM3).toFixed(2)} m³)
              </option>
            ))}
          </select>
        </div>

        {/* ── Log checklist ───────────────────────────────────────────── */}
        {selectedStockId && (
          <div>
            <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
              <label className="text-sm font-semibold text-charcoal">
                In-Stock Logs {selectedStock ? `— ${selectedStock.species}, ${selectedStock.lengthFt} ft` : ''}
              </label>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={selectAllLogs}
                  className="text-sm text-heartwood hover:text-heartwood-dark font-medium"
                >
                  Select all
                </button>
                <button
                  type="button"
                  onClick={clearLogSelection}
                  className="text-sm text-fog hover:text-charcoal font-medium"
                >
                  Clear
                </button>
              </div>
            </div>

            {logsError && (
              <div className="px-3 py-2 mb-2 rounded-md text-sm font-medium text-rust bg-rust/10 border border-rust/20">
                {logsError}
              </div>
            )}

            <div className="border border-charcoal/10 rounded-md max-h-64 overflow-y-auto divide-y divide-charcoal/10">
              {logsLoading ? (
                <p className="p-4 text-sm text-fog">Loading logs…</p>
              ) : logs.length === 0 ? (
                <p className="p-4 text-sm text-fog">No in-stock logs available for this batch.</p>
              ) : (
                logs.map((log) => (
                  <label
                    key={log.logId}
                    className="flex items-center gap-3 px-4 py-3 text-sm hover:bg-sawdust/50 cursor-pointer"
                  >
                    <input
                      type="checkbox"
                      checked={selectedLogIds.has(log.logId)}
                      onChange={() => toggleLog(log.logId)}
                      className="w-4 h-4 accent-heartwood"
                    />
                    <span className="font-mono font-semibold text-charcoal">LOG-{log.logId}</span>
                    <span className="text-charcoal">Girth {log.girthFt} ft</span>
                    <span className="text-charcoal font-semibold ml-auto">
                      {Number(log.volumeM3).toFixed(4)} m³
                    </span>
                  </label>
                ))
              )}
            </div>
          </div>
        )}

        {/* ── Auto-calculated quantity readout ────────────────────────── */}
        <div className="bg-sawdust/60 border border-charcoal/10 rounded-md px-4 py-3 flex items-center justify-between">
          <span className="text-sm font-semibold text-charcoal">Input Quantity (m³)</span>
          <span className="text-base font-display font-semibold text-heartwood">
            {inputQuantityM3.toFixed(4)} m³
          </span>
        </div>

        {/* ── Machine & Worker allocation ──────────────────────────────── */}
        <div className="flex flex-wrap gap-8">
          <div className="flex-1 min-w-[220px]">
            <label className="block text-sm font-semibold text-charcoal mb-1.5">Allocated Machine</label>
            <div className="flex flex-wrap items-center gap-2">
              {selectedMachineId && (
                <span className="inline-flex items-center gap-1.5 bg-moss/10 text-moss text-sm font-medium px-3 py-1.5 rounded-full">
                  {selectedMachine ? `${selectedMachine.name} (${selectedMachine.machineCode})` : `Machine #${selectedMachineId}`}
                  <button
                    type="button"
                    onClick={removeMachineChip}
                    className="text-moss/70 hover:text-rust font-bold"
                    aria-label="Remove machine"
                  >
                    ×
                  </button>
                </span>
              )}
              <Button variant="ghost" onClick={openMachineModal}>
                + Allocate Machine
              </Button>
            </div>
            <p className="text-sm text-fog mt-1.5">One machine per saw job. Machines under maintenance can't be selected.</p>
          </div>

          <div className="flex-1 min-w-[220px]">
            <label className="block text-sm font-semibold text-charcoal mb-1.5">Assigned Workers</label>
            <div className="flex flex-wrap items-center gap-2">
              {Array.from(selectedWorkerIds).map((id) => {
                const worker = selectedWorkersMap.get(id);
                return (
                  <span
                    key={id}
                    className="inline-flex items-center gap-1.5 bg-heartwood/10 text-heartwood-dark text-sm font-medium px-3 py-1.5 rounded-full"
                  >
                    {worker ? worker.fullName : `Worker #${id}`}
                    <button
                      type="button"
                      onClick={() => removeWorkerChip(id)}
                      className="text-heartwood-dark/70 hover:text-rust font-bold"
                      aria-label="Remove worker"
                    >
                      ×
                    </button>
                  </span>
                );
              })}
              <Button variant="ghost" onClick={openWorkerModal}>
                + Assign Workers
              </Button>
            </div>
          </div>
        </div>

        {/* ── Notes ───────────────────────────────────────────────────── */}
        <div>
          <div className="flex items-center justify-between mb-1.5">
            <label className="block text-sm font-semibold text-charcoal">Notes</label>
            <span className="text-xs text-fog">{notes.length}/{NOTES_MAX}</span>
          </div>
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value.slice(0, NOTES_MAX))}
            rows={3}
            placeholder="Optional notes about this saw job…"
            className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-base bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
          />
        </div>

        {/* ── Submit / Clear ──────────────────────────────────────────── */}
        <div className="flex items-center gap-3 pt-2 border-t border-charcoal/10">
          <Button variant="primary" disabled={!canSubmit} onClick={handleSubmit}>
            {submitting ? 'Starting Job…' : 'Start Saw Job'}
          </Button>
          <Button variant="ghost" onClick={resetForm} disabled={submitting}>
            Clear
          </Button>
        </div>
      </Card>

      {/* ── Recently started jobs ─────────────────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="p-4 border-b border-charcoal/10 flex flex-wrap items-center justify-between gap-2 bg-sawdust/60">
          <div>
            <h2 className="text-base font-semibold text-charcoal">Recently Started Jobs</h2>
            <p className="text-sm text-fog mt-0.5">Latest saw jobs recorded in the sawmill.</p>
          </div>
          <Button variant="ghost" onClick={onRefreshJobs} disabled={jobsLoading}>
            {jobsLoading ? 'Refreshing…' : 'Refresh'}
          </Button>
        </div>

        {jobsError && (
          <div className="px-4 py-3 text-sm font-medium text-rust bg-rust/10 border-b border-rust/20">
            {jobsError}
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
              <tr>
                <th className="py-3 px-4 w-10"></th>
                <th className="py-3 px-4 font-semibold">Job Code</th>
                <th className="py-3 px-4 font-semibold">Volume (m³)</th>
                <th className="py-3 px-4 font-semibold">Machine</th>
                <th className="py-3 px-4 font-semibold">Workers</th>
                <th className="py-3 px-4 font-semibold">Status</th>
                <th className="py-3 px-4 font-semibold">Started</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-charcoal/10">
              {recentJobs.length === 0 ? (
                <tr>
                  <td colSpan={7} className="py-6 px-4 text-center text-fog">
                    {jobsLoading ? 'Loading jobs…' : 'No saw jobs have been started yet.'}
                  </td>
                </tr>
              ) : (
                recentJobs.map((job) => {
                  const isExpanded = expandedJobIds.has(job.sawJobId);
                  return (
                    <Fragment key={job.sawJobId}>
                      <tr className="hover:bg-sawdust/40 transition-colors">
                        <td className="py-3 px-4 w-10 text-center">
                          <button
                            type="button"
                            onClick={() => toggleJobExpand(job.sawJobId)}
                            className="text-fog hover:text-charcoal p-1 rounded inline-flex items-center justify-center"
                            title={isExpanded ? 'Collapse job details' : 'Show species & length details'}
                          >
                            <svg
                              className={`w-4 h-4 transform transition-transform ${isExpanded ? 'rotate-90 text-heartwood' : ''}`}
                              fill="none"
                              stroke="currentColor"
                              viewBox="0 0 24 24"
                            >
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                            </svg>
                          </button>
                        </td>
                        <td className="py-3 px-4 font-mono font-semibold text-charcoal">{job.jobCode}</td>
                        <td className="py-3 px-4 font-semibold text-charcoal">
                          {Number(job.totalVolumeM3).toFixed(4)} m³
                        </td>
                        <td className="py-3 px-4 text-charcoal">
                          {job.machineName ? `${job.machineName} (${job.machineCode})` : '—'}
                        </td>
                        <td className="py-3 px-4 text-charcoal">
                          {job.assignedWorkerNames?.length ? job.assignedWorkerNames.join(', ') : '—'}
                        </td>
                        <td className="py-3 px-4">
                          <SawJobStatusPill status={job.status} />
                        </td>
                        <td className="py-3 px-4 text-fog">
                          {job.startedAt ? new Date(job.startedAt).toLocaleString() : '—'}
                        </td>
                      </tr>
                      {isExpanded && (
                        <tr className="bg-sawdust/40 border-b border-charcoal/10">
                          <td></td>
                          <td colSpan={6} className="py-3 px-4 text-sm text-charcoal">
                            <div className="flex flex-col gap-1.5">
                              <span className="inline-flex items-center gap-2">
                                <span className="font-semibold text-charcoal">Species:</span>
                                <span>{job.speciesName}</span>
                              </span>
                              <span className="inline-flex items-center gap-2">
                                <span className="font-semibold text-charcoal">Length:</span>
                                <span>{job.lengthFt} ft</span>
                              </span>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </Card>

      {/* ── Worker selection modal ─────────────────────────────────────── */}
      {workerModalOpen && (
        <div className="fixed inset-0 bg-charcoal/40 flex items-center justify-center p-4 z-50">
          <Card className="w-full max-w-md p-5 space-y-4">
            <h3 className="text-lg font-semibold text-charcoal">Assign Workers</h3>

            <input
              type="text"
              value={workerSearch}
              onChange={(e) => setWorkerSearch(e.target.value)}
              placeholder="Search by name or employee code…"
              className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />

            {workersError && (
              <div className="px-3 py-2 rounded-md text-sm font-medium text-rust bg-rust/10 border border-rust/20">
                {workersError}
              </div>
            )}

            <div className="border border-charcoal/10 rounded-md max-h-60 overflow-y-auto divide-y divide-charcoal/10">
              {workersLoading ? (
                <p className="p-4 text-sm text-fog">Loading workers…</p>
              ) : workers.length === 0 ? (
                <p className="p-4 text-sm text-fog">No active workers match your search.</p>
              ) : (
                workers.map((worker) => (
                  <label
                    key={worker.workerId}
                    className="flex items-center gap-3 px-4 py-2.5 text-sm hover:bg-sawdust/50 cursor-pointer"
                  >
                    <input
                      type="checkbox"
                      checked={stagedWorkerIds.has(worker.workerId)}
                      onChange={() => toggleStagedWorker(worker)}
                      className="w-4 h-4 accent-heartwood"
                    />
                    <span className="font-semibold text-charcoal">{worker.fullName}</span>
                    <span className="text-fog">{worker.jobRole}</span>
                    <span className="font-mono text-fog ml-auto">{worker.employeeCode}</span>
                  </label>
                ))
              )}
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <Button variant="ghost" onClick={() => setWorkerModalOpen(false)}>
                Cancel
              </Button>
              <Button variant="primary" onClick={confirmWorkerModal}>
                Confirm ({stagedWorkerIds.size})
              </Button>
            </div>
          </Card>
        </div>
      )}

      {/* ── Machine selection modal ──────────────────────────────────── */}
      {machineModalOpen && (
        <div className="fixed inset-0 bg-charcoal/40 flex items-center justify-center p-4 z-50">
          <Card className="w-full max-w-md p-5 space-y-4">
            <h3 className="text-lg font-semibold text-charcoal">Allocate Machine</h3>
            <p className="text-sm text-fog">Select the machine this saw job will run on.</p>

            <input
              type="text"
              value={machineSearch}
              onChange={(e) => setMachineSearch(e.target.value)}
              placeholder="Search by name or machine code…"
              className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />

            {machinesError && (
              <div className="px-3 py-2 rounded-md text-sm font-medium text-rust bg-rust/10 border border-rust/20">
                {machinesError}
              </div>
            )}

            <div className="border border-charcoal/10 rounded-md max-h-60 overflow-y-auto divide-y divide-charcoal/10">
              {machinesLoading ? (
                <p className="p-4 text-sm text-fog">Loading machines…</p>
              ) : machines.length === 0 ? (
                <p className="p-4 text-sm text-fog">No machines match your search.</p>
              ) : (
                machines.map((machine) => {
                  const disabled = machine.status !== 'Available';
                  return (
                    <label
                      key={machine.machineId}
                      className={`flex items-center gap-3 px-4 py-2.5 text-sm ${
                        disabled ? 'opacity-45 cursor-not-allowed' : 'hover:bg-sawdust/50 cursor-pointer'
                      }`}
                    >
                      <input
                        type="radio"
                        name="machinePick"
                        checked={stagedMachineId === machine.machineId}
                        onChange={() => !disabled && setStagedMachineId(machine.machineId)}
                        disabled={disabled}
                        className="w-4 h-4 accent-heartwood"
                      />
                      <span className="font-semibold text-charcoal">{machine.name}</span>
                      <span className="font-mono text-fog">{machine.machineCode}</span>
                      <span className="text-fog ml-auto">{machine.status}</span>
                    </label>
                  );
                })
              )}
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <Button variant="ghost" onClick={() => setMachineModalOpen(false)}>
                Cancel
              </Button>
              <Button variant="primary" disabled={!stagedMachineId} onClick={confirmMachineModal}>
                Confirm Selection
              </Button>
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
