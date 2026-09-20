import { useState, useEffect, useCallback, useMemo } from 'react';
import { fetchWithAuth } from '../../utils/api';
import Card from '../ui/Card';
import Button from '../ui/Button';

const NOTES_MAX = 500;

/**
 * "Start Saw Job" tab of the Sawing Process page.
 *
 * Every list here is fetched from a real API — no mock data:
 *  - Stock batches come from the parent's `stockList` (already fetched from
 *    GET /stock, proxied from LogIntakeService).
 *  - In-stock logs for the selected batch come from GET /stock/{id}/logs.
 *  - Workers come from GET /workers?q=.
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
    setNotes('');
  };

  const [submitting, setSubmitting] = useState(false);

  const canSubmit =
    !!selectedStockId && selectedLogIds.size > 0 && selectedWorkerIds.size > 0 && !submitting;

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

        {/* ── Worker allocation ────────────────────────────────────────── */}
        <div>
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
    </div>
  );
}
