import { useState, useEffect, useCallback } from 'react';
import { fetchWithAuth } from '../../utils/api';
import Card from '../ui/Card';
import Button from '../ui/Button';

/**
 * "Job History" tab of the Sawing Process page — a searchable, sortable,
 * date-filterable table of every FINISHED saw job (Completed or Cancelled).
 * InProgress jobs never appear here; they stay on the "Recently Started Jobs"
 * list until they resolve.
 *
 * Fetches GET /jobs/history?from=YYYY-MM-DD&to=YYYY-MM-DD from SawmillService.
 * The backend returns every finished job in the requested StartedAt date range
 * (only the optional from/to query params — search and sorting are client-side
 * over the already-fetched array, so the API is re-called only when the date
 * range changes, never per keystroke). All volumes are cubic metres (m³).
 */
export default function JobHistoryTab({ apiBaseUrl, isAdmin = false }) {
  // ── Date-range filter (empty string = unbounded on that side) ───────────
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  // ── History state (loading/error pattern mirrors WastageYieldReportTab) ──
  const [history, setHistory] = useState(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');

  // ── Permanent-delete modal state (Admin, Cancelled rows only) ────────────
  const [deleteModal, setDeleteModal] = useState({ open: false, job: null });
  const [deleteError, setDeleteError] = useState('');
  const [deleteLoading, setDeleteLoading] = useState(false);

  // ── Client-side search & sort (never trigger a network round-trip) ──────
  const [searchQuery, setSearchQuery] = useState('');
  const [sortKey, setSortKey] = useState('startedAt');
  const [sortDir, setSortDir] = useState('desc');

  const fetchHistory = useCallback(async () => {
    setHistoryLoading(true);
    setHistoryError('');
    try {
      const params = new URLSearchParams();
      if (fromDate) params.set('from', fromDate);
      if (toDate) params.set('to', toDate);
      const qs = params.toString();
      const url = `${apiBaseUrl}/jobs/history${qs ? `?${qs}` : ''}`;

      const res = await fetchWithAuth(url);
      if (res.ok) {
        setHistory(await res.json());
      } else {
        const body = await res.json().catch(() => null);
        setHistoryError(body?.message || `Failed to load job history (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setHistoryError('Network error loading job history.');
    } finally {
      setHistoryLoading(false);
    }
  }, [apiBaseUrl, fromDate, toDate]);

  // Initial load + automatic re-fetch whenever the date range changes
  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  const resetFilters = () => {
    setSearchQuery('');
    setFromDate('');
    setToDate('');
  };

  // ── Permanent delete of a Cancelled job (Admin only) ─────────────────────
  const handleDeleteConfirm = async () => {
    const job = deleteModal.job;
    if (!job) return;
    setDeleteLoading(true);
    setDeleteError('');
    try {
      const res = await fetchWithAuth(`${apiBaseUrl}/jobs/${job.sawJobId}`, { method: 'DELETE' });
      if (res.ok) {
        setDeleteModal({ open: false, job: null });
        fetchHistory();
      } else {
        const body = await res.json().catch(() => null);
        setDeleteError(body?.message || `Failed to delete job (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setDeleteError('Network error deleting job.');
    } finally {
      setDeleteLoading(false);
    }
  };

  // Column-click sorting: same column flips direction; a new column adopts its
  // default — asc for Job and Species, desc for every other column.
  const handleSort = (key) => {
    if (key === sortKey) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDir(DEFAULT_SORT_DIRECTION[key] || 'desc');
    }
  };

  // ── Client-side free-text search over the already-fetched array ─────────
  const q = searchQuery.trim().toLowerCase();
  const jobs = history || [];
  const filtered = jobs.filter((job) => {
    if (!q) return true;
    const haystack = [
      job.jobCode,
      job.speciesName,
      job.status,
      (job.assignedWorkerNames || []).join(' '),
      job.machineName,
      job.machineCode,
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();
    return haystack.includes(q);
  });

  // ── Client-side column sorting (null CompletedAt sinks to the end) ──────
  const sorted = [...filtered].sort((a, b) => {
    if (sortKey === 'startedAt' || sortKey === 'completedAt') {
      const ta = a[sortKey] ? new Date(a[sortKey]).getTime() : null;
      const tb = b[sortKey] ? new Date(b[sortKey]).getTime() : null;
      if (ta === null && tb === null) return 0;
      if (ta === null) return 1; // e.g. a Cancelled job with no CompletedAt
      if (tb === null) return -1;
      return (ta - tb) * (sortDir === 'asc' ? 1 : -1);
    }
    if (sortKey === 'totalVolumeM3') {
      const va = Number(a[sortKey] || 0);
      const vb = Number(b[sortKey] || 0);
      return (va - vb) * (sortDir === 'asc' ? 1 : -1);
    }
    const va = String(a[sortKey] || '').toLowerCase();
    const vb = String(b[sortKey] || '').toLowerCase();
    if (va < vb) return sortDir === 'asc' ? -1 : 1;
    if (va > vb) return sortDir === 'asc' ? 1 : -1;
    return 0;
  });

  const firstLoad = historyLoading && !history;

  return (
    <div className="space-y-6">
      {historyError && (
        <div className="px-4 py-3 text-sm font-medium text-rust bg-rust/10 border border-rust/20 rounded-md">
          {historyError}
        </div>
      )}

      {/* ── Toolbar: search + date range + reset ─────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="p-4 border-b border-charcoal/10 bg-sawdust/60">
          <h2 className="text-base font-semibold text-charcoal">Filter Job History</h2>
          <p className="text-sm text-fog mt-0.5">
            Finished saw jobs only (Completed or Cancelled). The date range filters by start date;
            search and column sorting apply to the loaded results.
          </p>
        </div>
        <div className="p-4 flex flex-wrap items-end gap-4">
          <div className="min-w-[260px] flex-1">
            <label htmlFor="historySearch" className="block text-sm font-semibold text-charcoal mb-1.5">
              Search
            </label>
            <input
              id="historySearch"
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Job ID, species, worker, or machine…"
              className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />
          </div>
          <div>
            <label htmlFor="historyFromDate" className="block text-sm font-semibold text-charcoal mb-1.5">
              From
            </label>
            <input
              id="historyFromDate"
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />
          </div>
          <div>
            <label htmlFor="historyToDate" className="block text-sm font-semibold text-charcoal mb-1.5">
              To
            </label>
            <input
              id="historyToDate"
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />
          </div>
          <Button variant="ghost" onClick={resetFilters}>
            Reset
          </Button>
        </div>
      </Card>

      {/* ── History table ───────────────────────────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
              <tr>
                {COLUMNS.map((col) =>
                  col.key ? (
                    <SortableHeader
                      key={col.key}
                      label={col.label}
                      sortKey={col.key}
                      activeKey={sortKey}
                      direction={sortDir}
                      onSort={handleSort}
                    />
                  ) : (
                    <th key={col.label} className="py-3 px-4 font-semibold">
                      {col.label}
                    </th>
                  ),
                )}
                {isAdmin && <th className="py-3 px-4 font-semibold text-right whitespace-nowrap">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-charcoal/10">
              {firstLoad ? (
                <tr>
                  <td colSpan={COLUMNS.length + (isAdmin ? 1 : 0)} className="py-6 px-4 text-center text-fog">
                    Loading history…
                  </td>
                </tr>
              ) : jobs.length === 0 ? (
                <tr>
                  <td colSpan={COLUMNS.length + (isAdmin ? 1 : 0)} className="py-6 px-4 text-center text-fog">
                    No saw jobs in the selected date range.
                  </td>
                </tr>
              ) : sorted.length === 0 ? (
                <tr>
                  <td colSpan={COLUMNS.length + (isAdmin ? 1 : 0)} className="py-6 px-4 text-center text-fog">
                    No saw jobs match your search/filter.
                  </td>
                </tr>
              ) : (
                sorted.map((job) => (
                  <tr key={job.sawJobId} className="hover:bg-sawdust/40 transition-colors">
                    <td className="py-3 px-4 font-mono font-semibold text-charcoal">{job.jobCode}</td>
                    <td className="py-3 px-4 text-charcoal">{job.speciesName}</td>
                    <td className="py-3 px-4">
                      <StatusPill status={job.status} />
                    </td>
                    <td className="py-3 px-4 font-mono text-charcoal">
                      {Number(job.totalVolumeM3 || 0).toFixed(2)}
                    </td>
                    <td className="py-3 px-4 font-mono text-charcoal">{formatDate(job.startedAt)}</td>
                    <td className="py-3 px-4 font-mono text-charcoal">{formatDate(job.completedAt)}</td>
                    <td className="py-3 px-4 text-charcoal">{(job.assignedWorkerNames || []).join(', ')}</td>
                    <td className="py-3 px-4 text-charcoal">
                      {job.machineName} ({job.machineCode})
                    </td>
                    {isAdmin && (
                      <td className="py-3 px-4 text-right whitespace-nowrap">
                        {job.status === 'Cancelled' && (
                          <button
                            type="button"
                            title="Permanently delete this cancelled job"
                            onClick={() => setDeleteModal({ open: true, job })}
                            className="text-fog hover:text-rust hover:bg-rust/10 border border-transparent hover:border-rust/20 rounded p-1.5 transition-colors"
                          >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z" />
                            </svg>
                          </button>
                        )}
                      </td>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>

      {/* ── Permanent-delete confirmation modal ─────────────────────────── */}
      {deleteModal.open && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
            <h3 className="text-lg font-bold text-charcoal">Permanently Delete Cancelled Job</h3>
            <p className="text-sm text-fog">
              Are you sure you want to permanently delete job{' '}
              <span className="font-semibold text-charcoal">{deleteModal.job?.jobCode}</span>?
              This cannot be undone.
            </p>
            {deleteError && (
              <div className="px-3 py-2 text-sm font-medium text-rust bg-rust/10 border border-rust/20 rounded-md">
                {deleteError}
              </div>
            )}
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => { setDeleteModal({ open: false, job: null }); setDeleteError(''); }}
                className="px-4 py-2 border rounded-lg text-sm text-gray-600 hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={deleteLoading}
                onClick={handleDeleteConfirm}
                className="px-4 py-2 bg-rust hover:opacity-90 text-white rounded-lg text-sm font-medium transition-opacity disabled:opacity-50"
              >
                {deleteLoading ? 'Deleting…' : 'Permanently Delete'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Sortable columns — Job and Species default to ascending; every other
 * column defaults to descending. Search and sorting are client-side only,
 * so the API is never called with q/sortBy/sortDir.
 */
const DEFAULT_SORT_DIRECTION = { jobCode: 'asc', speciesName: 'asc' };

const COLUMNS = [
  { key: 'jobCode', label: 'Job' },
  { key: 'speciesName', label: 'Species' },
  { key: 'status', label: 'Status' },
  { key: 'totalVolumeM3', label: 'Input (m³)' },
  { key: 'startedAt', label: 'Started' },
  { key: 'completedAt', label: 'Completed' },
  { key: null, label: 'Assigned To' },
  { key: null, label: 'Machine' },
];

/** Sortable column header button; ▲/▼ shown on the active column only. */
function SortableHeader({ label, sortKey, activeKey, direction, onSort }) {
  const active = sortKey === activeKey;
  return (
    <th className="py-3 px-4 font-semibold">
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        title={`Sort by ${label}`}
        className={`inline-flex items-center gap-1 transition-colors ${
          active ? 'text-heartwood' : 'hover:text-charcoal'
        }`}
      >
        {label}
        {active && <span className="text-[10px] leading-none">{direction === 'asc' ? '▲' : '▼'}</span>}
      </button>
    </th>
  );
}

/**
 * Read-only status pill for resolved jobs — deliberately NOT the interactive
 * SawJobStatusPill (that component is a status-changing dropdown for
 * in-progress jobs; history rows are finished and must not be editable).
 */
function StatusPill({ status }) {
  const cfg = STATUS_PILL[status] || { label: status, className: 'bg-fog/15 text-fog' };
  return (
    <span
      className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold whitespace-nowrap ${cfg.className}`}
    >
      {cfg.label}
    </span>
  );
}

const STATUS_PILL = {
  Completed: { label: 'Complete', className: 'bg-moss/15 text-moss' },
  Cancelled: { label: 'Cancelled', className: 'bg-rust/15 text-rust' },
};

/** Local helper — this codebase duplicates small date helpers per file. */
function formatDate(value) {
  if (!value) return '—';
  const d = new Date(value);
  if (isNaN(d.getTime())) return '—';
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}