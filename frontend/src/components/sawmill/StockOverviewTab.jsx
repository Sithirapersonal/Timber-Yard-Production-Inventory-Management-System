import Card from '../ui/Card';
import Button from '../ui/Button';

/**
 * "Stock Overview" tab of the Sawing Process page.
 *
 * Shows the raw stock batches available for sawing (proxied live from
 * LogIntakeService via SawmillService's GET /stock) and the sawmill's own
 * "Recently Started Jobs" list (GET /jobs), which survives a page reload
 * because it is fetched from SawmillDB rather than held only in local state.
 */
export default function StockOverviewTab({
  stockList,
  stockLoading,
  stockError,
  onRefresh,
  recentJobs,
  jobsLoading,
  jobsError,
  onRefreshJobs,
  canWrite,
  onStartJob,
}) {
  return (
    <div className="space-y-6">
      {/* ── Raw stock batches ─────────────────────────────────────────── */}
      <Card className="overflow-hidden">
        <div className="p-4 border-b border-charcoal/10 flex flex-wrap items-center justify-between gap-2 bg-sawdust/60">
          <div>
            <h2 className="text-base font-semibold text-charcoal">Raw Stock Available for Sawing</h2>
            <p className="text-sm text-fog mt-0.5">
              Live rollup of in-stock logs by species &amp; length, from Log Intake.
            </p>
          </div>
          <Button variant="ghost" onClick={onRefresh} disabled={stockLoading}>
            {stockLoading ? 'Refreshing…' : 'Refresh'}
          </Button>
        </div>

        {stockError && (
          <div className="px-4 py-3 text-sm font-medium text-rust bg-rust/10 border-b border-rust/20">
            {stockError}
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
              <tr>
                <th className="py-3 px-4 font-semibold">Species</th>
                <th className="py-3 px-4 font-semibold">Length (ft)</th>
                <th className="py-3 px-4 font-semibold">Log Count</th>
                <th className="py-3 px-4 font-semibold">Total Volume (m³)</th>
                <th className="py-3 px-4 font-semibold">Status</th>
                <th className="py-3 px-4 font-semibold text-right">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-charcoal/10">
              {stockList.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-6 px-4 text-center text-fog">
                    {stockLoading ? 'Loading stock…' : 'No raw stock batches found.'}
                  </td>
                </tr>
              ) : (
                stockList.map((batch) => {
                  const disabled = !canWrite || batch.logCount === 0;
                  return (
                    <tr key={batch.stockId} className="hover:bg-sawdust/40 transition-colors">
                      <td className="py-3 px-4 font-semibold text-charcoal">{batch.species}</td>
                      <td className="py-3 px-4 text-charcoal">{batch.lengthFt} ft</td>
                      <td className="py-3 px-4 text-charcoal">{batch.logCount}</td>
                      <td className="py-3 px-4 font-semibold text-charcoal">
                        {Number(batch.totalVolumeM3).toFixed(4)} m³
                      </td>
                      <td className="py-3 px-4">
                        {batch.isLowStock ? (
                          <span className="inline-block px-2.5 py-1 rounded-full text-sm font-medium bg-rust/15 text-rust">
                            Low Stock
                          </span>
                        ) : (
                          <span className="inline-block px-2.5 py-1 rounded-full text-sm font-medium bg-moss/15 text-moss">
                            Adequate
                          </span>
                        )}
                      </td>
                      <td className="py-3 px-4 text-right">
                        <Button
                          variant="primary"
                          disabled={disabled}
                          onClick={() => onStartJob(batch.stockId)}
                          title={
                            batch.logCount === 0
                              ? 'No in-stock logs available for this batch.'
                              : !canWrite
                              ? 'Your role cannot start saw jobs.'
                              : undefined
                          }
                        >
                          Start Saw Job →
                        </Button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
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
                <th className="py-3 px-4 font-semibold">Job Code</th>
                <th className="py-3 px-4 font-semibold">Species</th>
                <th className="py-3 px-4 font-semibold">Length (ft)</th>
                <th className="py-3 px-4 font-semibold">Volume (m³)</th>
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
                recentJobs.map((job) => (
                  <tr key={job.sawJobId} className="hover:bg-sawdust/40 transition-colors">
                    <td className="py-3 px-4 font-mono font-semibold text-charcoal">{job.jobCode}</td>
                    <td className="py-3 px-4 text-charcoal">{job.speciesName}</td>
                    <td className="py-3 px-4 text-charcoal">{job.lengthFt} ft</td>
                    <td className="py-3 px-4 font-semibold text-charcoal">
                      {Number(job.totalVolumeM3).toFixed(4)} m³
                    </td>
                    <td className="py-3 px-4 text-charcoal">
                      {job.assignedWorkerNames?.length ? job.assignedWorkerNames.join(', ') : '—'}
                    </td>
                    <td className="py-3 px-4">
                      <StatusPill status={job.status} />
                    </td>
                    <td className="py-3 px-4 text-fog">
                      {job.startedAt ? new Date(job.startedAt).toLocaleString() : '—'}
                    </td>
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

function StatusPill({ status }) {
  const config = {
    InProgress: 'bg-heartwood/15 text-heartwood',
    Completed: 'bg-moss/15 text-moss',
    Cancelled: 'bg-rust/15 text-rust',
  };
  return (
    <span className={`inline-block px-2.5 py-1 rounded-full text-sm font-medium ${config[status] || 'bg-fog/15 text-fog'}`}>
      {status}
    </span>
  );
}
