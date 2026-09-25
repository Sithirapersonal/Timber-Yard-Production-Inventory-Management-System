import { useState, Fragment } from 'react';
import { fetchWithAuth } from '../../utils/api';
import Card from '../ui/Card';
import Button from '../ui/Button';

/**
 * "Stock Overview" tab of the Sawing Process page.
 *
 * Shows the raw stock batches available for sawing (proxied live from
 * LogIntakeService via SawmillService's GET /stock). Each row can be
 * expanded — mirroring the Log Intake page's per-log expand pattern — to
 * show which delivery/supplier(s) the logs behind that stock batch came
 * from. Since a stock batch is a rollup by species & length, it can be made
 * up of logs from more than one delivery/supplier, so the expanded detail
 * lists every distinct delivery contributing to it.
 */
export default function StockOverviewTab({
  stockList,
  stockLoading,
  stockError,
  onRefresh,
  canWrite,
  onStartJob,
  apiBaseUrl,
}) {
  // ── Expand/collapse per-row delivery & supplier breakdown ──────────────
  const [expandedStockIds, setExpandedStockIds] = useState(new Set());
  const [detailsByStockId, setDetailsByStockId] = useState(new Map());

  const fetchStockDetails = async (stockId) => {
    setDetailsByStockId((prev) => {
      const next = new Map(prev);
      next.set(stockId, { loading: true, error: '', breakdown: [] });
      return next;
    });

    try {
      const res = await fetchWithAuth(`${apiBaseUrl}/stock/${stockId}/logs`);
      if (res.ok) {
        const logs = await res.json();
        const breakdown = groupByDelivery(logs);
        setDetailsByStockId((prev) => {
          const next = new Map(prev);
          next.set(stockId, { loading: false, error: '', breakdown });
          return next;
        });
      } else {
        const body = await res.json().catch(() => null);
        setDetailsByStockId((prev) => {
          const next = new Map(prev);
          next.set(stockId, {
            loading: false,
            error: body?.message || `Failed to load delivery details (${res.status}).`,
            breakdown: [],
          });
          return next;
        });
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') {
        setDetailsByStockId((prev) => {
          const next = new Map(prev);
          next.set(stockId, { loading: false, error: 'Network error loading delivery details.', breakdown: [] });
          return next;
        });
      }
    }
  };

  const toggleExpand = (stockId) => {
    setExpandedStockIds((prev) => {
      const next = new Set(prev);
      if (next.has(stockId)) {
        next.delete(stockId);
      } else {
        next.add(stockId);
        if (!detailsByStockId.has(stockId)) fetchStockDetails(stockId);
      }
      return next;
    });
  };

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
                <th className="py-3 px-4 w-10"></th>
                <th className="py-3 px-4 font-semibold">Stock</th>
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
                  <td colSpan={8} className="py-6 px-4 text-center text-fog">
                    {stockLoading ? 'Loading stock…' : 'No raw stock batches found.'}
                  </td>
                </tr>
              ) : (
                stockList.map((batch) => {
                  const disabled = !canWrite || batch.logCount === 0;
                  const isExpanded = expandedStockIds.has(batch.stockId);
                  const detail = detailsByStockId.get(batch.stockId);
                  return (
                    <Fragment key={batch.stockId}>
                      <tr className="hover:bg-sawdust/40 transition-colors">
                        <td className="py-3 px-4 w-10 text-center">
                          <button
                            type="button"
                            onClick={() => toggleExpand(batch.stockId)}
                            className="text-fog hover:text-charcoal p-1 rounded inline-flex items-center justify-center"
                            title={isExpanded ? 'Collapse delivery details' : 'Show delivery & supplier details'}
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
                        <td className="py-3 px-4 font-mono font-semibold text-charcoal">STK-{batch.stockId}</td>
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
                      {isExpanded && (
                        <tr className="bg-sawdust/40 border-b border-charcoal/10">
                          <td></td>
                          <td colSpan={7} className="py-3 px-4 text-sm text-charcoal">
                            {detail?.loading ? (
                              <span className="text-fog">Loading delivery details…</span>
                            ) : detail?.error ? (
                              <span className="text-rust font-medium">{detail.error}</span>
                            ) : detail?.breakdown?.length ? (
                              <div className="flex flex-col gap-1.5">
                                {detail.breakdown.map((d) => (
                                  <span key={d.deliveryId} className="inline-flex items-center gap-2">
                                    <span className="font-semibold text-charcoal">Delivery #{d.deliveryId}</span>
                                    <span className="text-fog">·</span>
                                    <span>{d.supplierName}</span>
                                    <span className="text-fog">·</span>
                                    <span className="text-fog">
                                      {d.logCount} {d.logCount === 1 ? 'log' : 'logs'}, {d.volumeM3.toFixed(4)} m³
                                    </span>
                                  </span>
                                ))}
                              </div>
                            ) : (
                              <span className="text-fog">No delivery details available.</span>
                            )}
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
    </div>
  );
}

/**
 * Groups a stock batch's individual logs by (deliveryId, supplierName) so
 * the expanded row can show every delivery/supplier contributing to that
 * rolled-up batch, each with its own log count and volume subtotal.
 */
function groupByDelivery(logs) {
  const map = new Map();
  for (const log of logs) {
    const key = log.deliveryId;
    if (!map.has(key)) {
      map.set(key, {
        deliveryId: log.deliveryId,
        supplierName: log.supplierName || 'Unknown Supplier',
        logCount: 0,
        volumeM3: 0,
      });
    }
    const entry = map.get(key);
    entry.logCount += 1;
    entry.volumeM3 += Number(log.volumeM3 || 0);
  }
  return Array.from(map.values()).sort((a, b) => a.deliveryId - b.deliveryId);
}
