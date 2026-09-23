import { useState, useEffect, useCallback } from 'react';
import { fetchWithAuth } from '../../utils/api';
import Card from '../ui/Card';
import Button from '../ui/Button';

/**
 * "Wastage & Yield Report" tab of the Sawing Process page — management-facing
 * analytics (Admin/Manager only; the parent page gates it, matching how the
 * StartJobTab is hidden from unauthorized roles).
 *
 * Fetches GET /jobs/wastage-yield-report?from=YYYY-MM-DD&to=YYYY-MM-DD from
 * SawmillService. The backend computes every total and percentage; this
 * component only formats and displays what the API returns — it never does
 * any recovery/wastage arithmetic itself.
 *
 * Data covers completed saw jobs only (Status='Completed' with a non-null
 * CompletedAt), filtered by completion date when a From/To range is set.
 * All volumes are cubic metres (m³/M3), consistent with the rest of the
 * sawmill UI.
 */
export default function WastageYieldReportTab({ apiBaseUrl }) {
  // ── Date-range filter (empty string = unbounded on that side) ───────────
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  // ── Report state (loading/error pattern mirrors fetchStock/fetchRecentJobs)
  const [report, setReport] = useState(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [reportError, setReportError] = useState('');

  const fetchReport = useCallback(async () => {
    setReportLoading(true);
    setReportError('');
    try {
      const params = new URLSearchParams();
      if (fromDate) params.set('from', fromDate);
      if (toDate) params.set('to', toDate);
      const qs = params.toString();
      const url = `${apiBaseUrl}/jobs/wastage-yield-report${qs ? `?${qs}` : ''}`;

      const res = await fetchWithAuth(url);
      if (res.ok) {
        setReport(await res.json());
      } else {
        const body = await res.json().catch(() => null);
        setReportError(body?.message || `Failed to load report (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setReportError('Network error loading report.');
    } finally {
      setReportLoading(false);
    }
  }, [apiBaseUrl, fromDate, toDate]);

  // Initial load + automatic re-fetch whenever the range changes
  useEffect(() => {
    fetchReport();
  }, [fetchReport]);

  const resetRange = () => {
    setFromDate('');
    setToDate('');
  };

  const totals = report?.totals || {};
  const speciesBreakdown = report?.speciesBreakdown || [];
  const jobs = report?.jobs || [];
  const firstLoad = reportLoading && !report;

  return (
    <div className="space-y-6">
      {reportError && (
        <div className="px-4 py-3 text-sm font-medium text-rust bg-rust/10 border border-rust/20 rounded-md">
          {reportError}
        </div>
      )}

      {/* ── Date-range filter ──────────────────────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="p-4 border-b border-charcoal/10 bg-sawdust/60">
          <h2 className="text-base font-semibold text-charcoal">Filter by Date Range</h2>
          <p className="text-sm text-fog mt-0.5">
            Applies to completed saw jobs only, by completion date — updates the figures below automatically.
          </p>
        </div>
        <div className="p-4 flex flex-wrap items-end gap-4">
          <div>
            <label htmlFor="reportFromDate" className="block text-sm font-semibold text-charcoal mb-1.5">
              From
            </label>
            <input
              id="reportFromDate"
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />
          </div>
          <div>
            <label htmlFor="reportToDate" className="block text-sm font-semibold text-charcoal mb-1.5">
              To
            </label>
            <input
              id="reportToDate"
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
            />
          </div>
          <Button variant="ghost" onClick={resetRange}>
            Reset Range
          </Button>
        </div>
      </Card>

      {/* ── Summary cards ──────────────────────────────────────────────── */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <SummaryCard
          label="Total Input"
          value={Number(totals.totalInputVolumeM3 || 0).toFixed(2)}
          unit="m³"
          note={`${Number(totals.completedJobCount || 0)} completed ${
            Number(totals.completedJobCount || 0) === 1 ? 'job' : 'jobs'
          } in range`}
        />
        <SummaryCard
          label="Finished Sawn Yield"
          value={Number(totals.totalYieldVolumeM3 || 0).toFixed(2)}
          unit="m³"
          note={`${Number(totals.recoveryPercentage || 0).toFixed(1)}% recovery rate`}
          noteClassName="text-moss"
        />
        <SummaryCard
          label="Total Wastage"
          value={Number(totals.totalWastageVolumeM3 || 0).toFixed(2)}
          unit="m³"
          note={`${Number(totals.wastagePercentage || 0).toFixed(1)}% of input volume`}
          noteClassName="text-rust"
        />
        <SummaryCard
          label="Species Covered"
          value={String(speciesBreakdown.length)}
          note={`across ${Number(totals.completedJobCount || 0)} completed ${
            Number(totals.completedJobCount || 0) === 1 ? 'job' : 'jobs'
          } in range`}
        />
      </div>

      {/* ── Recovery rate by species ───────────────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="p-4 border-b border-charcoal/10 bg-sawdust/60">
          <h2 className="text-base font-semibold text-charcoal">Recovery Rate by Species</h2>
          <p className="text-sm text-fog mt-0.5">
            Finished sawn yield as a percentage of input log volume — higher recovers more timber.
          </p>
        </div>
        <div className="p-4">
          {firstLoad ? (
            <p className="text-sm text-fog">Loading report…</p>
          ) : speciesBreakdown.length === 0 ? (
            <p className="text-sm text-fog">No completed jobs in the selected date range.</p>
          ) : (
            <div className="divide-y divide-charcoal/10">
              {speciesBreakdown.map((s) => {
                const pct = Math.min(Number(s.recoveryPercentage || 0), 100);
                return (
                  <div key={s.speciesName} className="flex flex-wrap items-center gap-3 py-2.5">
                    <span className="w-28 shrink-0 text-sm font-semibold text-charcoal">{s.speciesName}</span>
                    <span className="text-xs text-fog">
                      {s.completedJobCount} {s.completedJobCount === 1 ? 'job' : 'jobs'} ·{' '}
                      {Number(s.inputVolumeM3 || 0).toFixed(2)} m³ in
                    </span>
                    <div className="flex-1 min-w-[140px] h-2.5 rounded-full bg-sawdust overflow-hidden">
                      <div className="h-full rounded-full bg-heartwood" style={{ width: `${pct.toFixed(1)}%` }} />
                    </div>
                    <span className="w-12 text-right text-sm font-semibold text-charcoal">
                      {Number(s.recoveryPercentage || 0).toFixed(1)}%
                    </span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </Card>

      {/* ── Wastage & output by saw job ────────────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="p-4 border-b border-charcoal/10 bg-sawdust/60">
          <h2 className="text-base font-semibold text-charcoal">Wastage & Output by Saw Job</h2>
          <p className="text-sm text-fog mt-0.5">Every completed job in the selected date range.</p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
              <tr>
                <th className="py-3 px-4 font-semibold">Job</th>
                <th className="py-3 px-4 font-semibold">Species</th>
                <th className="py-3 px-4 font-semibold">Completed</th>
                <th className="py-3 px-4 font-semibold">Input (m³)</th>
                <th className="py-3 px-4 font-semibold">Yield (m³)</th>
                <th className="py-3 px-4 font-semibold">Wastage (m³)</th>
                <th className="py-3 px-4 font-semibold">Wastage %</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-charcoal/10">
              {firstLoad ? (
                <tr>
                  <td colSpan={7} className="py-6 px-4 text-center text-fog">Loading report…</td>
                </tr>
              ) : jobs.length === 0 ? (
                <tr>
                  <td colSpan={7} className="py-6 px-4 text-center text-fog">
                    No completed jobs in the selected date range.
                  </td>
                </tr>
              ) : (
                jobs.map((job) => (
                  <tr key={job.jobCode} className="hover:bg-sawdust/40 transition-colors">
                    <td className="py-3 px-4 font-mono font-semibold text-charcoal">{job.jobCode}</td>
                    <td className="py-3 px-4 text-charcoal">{job.speciesName}</td>
                    <td className="py-3 px-4 font-mono text-charcoal">{formatDate(job.completedAt)}</td>
                    <td className="py-3 px-4 font-mono text-charcoal">{Number(job.inputVolumeM3 || 0).toFixed(2)}</td>
                    <td className="py-3 px-4 font-mono text-charcoal">{Number(job.yieldVolumeM3 || 0).toFixed(2)}</td>
                    <td className="py-3 px-4 font-mono text-charcoal">{Number(job.wastageVolumeM3 || 0).toFixed(2)}</td>
                    <td className="py-3 px-4 font-mono text-charcoal">{Number(job.wastagePercentage || 0).toFixed(1)}%</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}

/** Small stat tile for the summary row (tokens from the codebase's palette). */
function SummaryCard({ label, value, unit, note, noteClassName = '' }) {
  return (
    <Card className="p-4">
      <p className="text-xs uppercase tracking-wider font-semibold text-fog">{label}</p>
      <p className="mt-1.5 font-display text-2xl font-semibold text-charcoal">
        {value}
        {unit && <span className="ml-1 text-sm font-medium text-fog font-sans">{unit}</span>}
      </p>
      {note && <p className={`mt-1.5 text-xs text-fog ${noteClassName}`}>{note}</p>}
    </Card>
  );
}

function formatDate(value) {
  if (!value) return '—';
  const d = new Date(value);
  if (isNaN(d.getTime())) return '—';
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}