import { useState, useEffect, useCallback } from 'react';
import { useAuth } from '../context/AuthContext';
import { fetchWithAuth } from '../utils/api';
import Layout from '../components/Layout';
import Card from '../components/ui/Card';
import Button from '../components/ui/Button';
import RoleBadge from '../components/ui/RoleBadge';

const API_BASE_URL = import.meta.env.VITE_TREATMENT_API_URL || 'http://localhost:5000/api';

const CHEMICAL_TYPES = ['CCA (Copper Chrome Arsenate)', 'ACQ (Alkaline Copper Quaternary)', 'Creosote', 'Boron Salts', 'Tanalith E'];

const STATUS_OPTIONS = ['Pending', 'InTreatment', 'Completed', 'Cancelled'];

export default function TreatmentPage() {
  const { user } = useAuth();

  const [activeTab, setActiveTab] = useState('stock'); // 'stock' | 'create' | 'batches'

  const [stock, setStock] = useState([]);
  const [batches, setBatches] = useState([]);
  const [statusFilter, setStatusFilter] = useState('');
  const [batchesLoading, setBatchesLoading] = useState(false);
  const [notification, setNotification] = useState({ type: '', text: '' });

  const [selectedKey, setSelectedKey] = useState(''); // "Species|Dimensions"
  const [chemicalType, setChemicalType] = useState(CHEMICAL_TYPES[0]);
  const [quantity, setQuantity] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [detailBatch, setDetailBatch] = useState(null);

  const showNotification = (type, text) => {
    setNotification({ type, text });
    setTimeout(() => setNotification({ type: '', text: '' }), 5000);
  };

  const fetchStock = useCallback(async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentStock`);
      if (res.ok) setStock(await res.json());
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch sawn stock', err);
    }
  }, []);

  const fetchBatches = useCallback(async () => {
    setBatchesLoading(true);
    try {
      const qs = statusFilter ? `?status=${encodeURIComponent(statusFilter)}` : '';
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches${qs}`);
      if (res.ok) setBatches(await res.json());
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch batches', err);
    } finally {
      setBatchesLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    fetchStock();
  }, [fetchStock]);

  useEffect(() => {
    fetchBatches();
  }, [fetchBatches]);

  const selected = stock.find((s) => `${s.species}|${s.dimensions}` === selectedKey);
  const available = selected ? Number(selected.volumeM3) : null;

  const handleCreate = async (e) => {
    e.preventDefault();
    if (!selected) {
      showNotification('error', 'Please select a sawn timber species/dimension.');
      return;
    }
    const qty = parseFloat(quantity);
    if (isNaN(qty) || qty <= 0) {
      showNotification('error', 'Quantity must be a positive number.');
      return;
    }
    if (available !== null && qty > available) {
      showNotification('error', `Insufficient sawn stock. Available: ${available} m³.`);
      return;
    }

    setSubmitting(true);
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches`, {
        method: 'POST',
        body: JSON.stringify({
          species: selected.species,
          dimensions: selected.dimensions,
          chemicalType,
          quantityM3: qty,
        }),
      });
      if (res.ok) {
        showNotification('success', 'Treatment batch created with status Pending.');
        setQuantity('');
        fetchStock();
        fetchBatches();
        setActiveTab('batches');
      } else {
        const body = await res.json().catch(() => null);
        showNotification('error', body?.message || 'Failed to create batch.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error creating batch.');
    } finally {
      setSubmitting(false);
    }
  };

  const openDetail = async (batchId) => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches/${batchId}`);
      if (res.ok) setDetailBatch(await res.json());
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Failed to load batch detail.');
    }
  };

  return (
    <Layout>
      <div className="space-y-6 max-w-7xl">

        {/* Page header */}
        <div className="flex flex-wrap items-start justify-between gap-4 pb-4 border-b border-charcoal/10">
          <div>
            <h1 className="font-display text-3xl font-semibold text-charcoal">
              Treatment &amp; Chemical Batching
            </h1>
            <p className="text-sm text-fog mt-1">
              Create chemical treatment batches from sawn stock and track their status.
            </p>
          </div>
          <RoleBadge role={user?.role} />
        </div>

        {/* Inline notification banner */}
        {notification.text && (
          <div
            className={`px-4 py-3 rounded-lg text-sm font-medium border ${
              notification.type === 'success'
                ? 'bg-moss/15 text-moss border-moss/30'
                : 'bg-rust/15 text-rust border-rust/30'
            }`}
          >
            {notification.text}
          </div>
        )}

        {/* Tab navigation */}
        <div className="flex flex-wrap gap-1 border-b border-charcoal/10">
          {[
            { id: 'stock', label: 'Sawn Stock' },
            { id: 'create', label: '+ Create Batch' },
            { id: 'batches', label: 'Batch History' },
          ].map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2.5 px-5 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? 'border-heartwood text-heartwood font-semibold'
                  : 'border-transparent text-fog hover:text-charcoal'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {/* Tab panels */}
        {activeTab === 'stock' && (
          <Card className="overflow-hidden">
            <div className="p-4 border-b border-charcoal/10 bg-sawdust/60 flex justify-between items-center">
              <h2 className="text-base font-semibold text-charcoal">Sawn Stock Available for Treatment</h2>
              <Button variant="ghost" onClick={fetchStock}>Refresh</Button>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
                  <tr>
                    <th className="py-3 px-4">Species</th>
                    <th className="py-3 px-4">Dimensions</th>
                    <th className="py-3 px-4">Available Volume (m³)</th>
                    <th className="py-3 px-4">Last Updated</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-charcoal/10">
                  {stock.length === 0 ? (
                    <tr><td colSpan="4" className="py-6 px-4 text-center text-fog">No sawn stock available.</td></tr>
                  ) : (
                    stock.map((s) => (
                      <tr key={`${s.species}|${s.dimensions}`} className="hover:bg-sawdust/40 transition-colors">
                        <td className="py-3 px-4 font-semibold text-charcoal">{s.species}</td>
                        <td className="py-3 px-4 text-charcoal">{s.dimensions}</td>
                        <td className="py-3 px-4 font-mono text-charcoal">{Number(s.volumeM3).toFixed(4)}</td>
                        <td className="py-3 px-4 text-fog">{new Date(s.lastUpdated).toLocaleString()}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        )}

        {activeTab === 'create' && (
          <Card className="overflow-hidden">
            <div className="p-4 border-b border-charcoal/10 bg-sawdust/60">
              <h2 className="text-base font-semibold text-charcoal">New Treatment Batch</h2>
              <p className="text-sm text-fog mt-0.5">New batches start with status Pending. Stock is deducted immediately.</p>
            </div>
            <form onSubmit={handleCreate} className="p-4 space-y-4 max-w-3xl">
              <div>
                <label className="block text-sm font-semibold text-charcoal mb-1.5">Sawn Timber *</label>
                <select
                  required
                  value={selectedKey}
                  onChange={(e) => setSelectedKey(e.target.value)}
                  className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                >
                  <option value="">Select a stock batch...</option>
                  {stock.map((s) => (
                    <option key={`${s.species}|${s.dimensions}`} value={`${s.species}|${s.dimensions}`}>
                      {s.species} — {s.dimensions} ({Number(s.volumeM3).toFixed(2)} m³)
                    </option>
                  ))}
                </select>
                {available !== null && (
                  <p className={`text-xs mt-1.5 font-medium ${available > 0 ? 'text-moss' : 'text-rust'}`}>
                    Available: {available.toFixed(4)} m³
                  </p>
                )}
              </div>

              <div>
                <label className="block text-sm font-semibold text-charcoal mb-1.5">Chemical Type *</label>
                <select
                  value={chemicalType}
                  onChange={(e) => setChemicalType(e.target.value)}
                  className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                >
                  {CHEMICAL_TYPES.map((c) => (
                    <option key={c} value={c}>{c}</option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-sm font-semibold text-charcoal mb-1.5">Quantity (m³) *</label>
                <input
                  type="number"
                  step="0.0001"
                  min="0.0001"
                  required
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                  placeholder="e.g. 1.2500"
                />
              </div>

              <Button type="submit" disabled={submitting}>
                {submitting ? 'Creating…' : 'Create Batch'}
              </Button>
            </form>
          </Card>
        )}

        {activeTab === 'batches' && (
          <Card className="overflow-hidden">
            <div className="p-4 border-b border-charcoal/10 bg-sawdust/60 flex flex-wrap justify-between items-center gap-3">
              <h2 className="text-base font-semibold text-charcoal">Treatment Batch History</h2>
              <div className="flex items-center gap-2">
                <label className="text-sm font-semibold text-charcoal">Status:</label>
                <select
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value)}
                  className="px-2.5 py-2 border border-charcoal/20 rounded-md text-xs bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                >
                  <option value="">All</option>
                  {STATUS_OPTIONS.map((s) => (
                    <option key={s} value={s}>{s}</option>
                  ))}
                </select>
                <Button variant="ghost" onClick={fetchBatches}>Refresh</Button>
              </div>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
                  <tr>
                    <th className="py-3 px-4">Batch</th>
                    <th className="py-3 px-4">Species</th>
                    <th className="py-3 px-4">Dimensions</th>
                    <th className="py-3 px-4">Chemical</th>
                    <th className="py-3 px-4">Quantity (m³)</th>
                    <th className="py-3 px-4">Status</th>
                    <th className="py-3 px-4">Created</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-charcoal/10">
                  {batchesLoading ? (
                    <tr><td colSpan="7" className="py-6 px-4 text-center text-fog">Loading batches…</td></tr>
                  ) : batches.length === 0 ? (
                    <tr><td colSpan="7" className="py-6 px-4 text-center text-fog">No batches found.</td></tr>
                  ) : (
                    batches.map((b) => (
                      <tr key={b.batchId} onClick={() => openDetail(b.batchId)} className="hover:bg-sawdust/40 cursor-pointer transition-colors">
                        <td className="py-3 px-4 font-mono text-xs font-bold text-charcoal">{b.batchCode}</td>
                        <td className="py-3 px-4 text-charcoal">{b.species}</td>
                        <td className="py-3 px-4 text-charcoal">{b.dimensions}</td>
                        <td className="py-3 px-4 text-charcoal">{b.chemicalType}</td>
                        <td className="py-3 px-4 font-mono text-charcoal">{Number(b.quantityM3).toFixed(4)}</td>
                        <td className="py-3 px-4">
                          <span className={`px-2.5 py-1 rounded-full text-xs font-semibold whitespace-nowrap ${
                            b.status === 'Completed' ? 'bg-moss/15 text-moss' :
                            b.status === 'Cancelled' ? 'bg-rust/15 text-rust' :
                            b.status === 'InTreatment' ? 'bg-heartwood/15 text-heartwood' :
                            'bg-fog/15 text-fog'
                          }`}>
                            {b.status}
                          </span>
                        </td>
                        <td className="py-3 px-4 text-fog">{new Date(b.createdAt).toLocaleString()}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        )}
      </div>

      {/* Batch Detail Modal */}
      {detailBatch && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
            <h3 className="text-lg font-bold text-charcoal">Batch Detail</h3>
            <dl className="text-sm space-y-2">
              <div className="flex justify-between"><dt className="text-fog">Batch Code</dt><dd className="font-mono font-semibold">{detailBatch.batchCode}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Species</dt><dd>{detailBatch.species}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Dimensions</dt><dd>{detailBatch.dimensions}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Chemical Type</dt><dd>{detailBatch.chemicalType}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Quantity</dt><dd className="font-mono">{Number(detailBatch.quantityM3).toFixed(4)} m³</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Status</dt><dd>{detailBatch.status}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Tank</dt><dd>{detailBatch.tank || '—'}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Cancellation Reason</dt><dd>{detailBatch.cancellationReason || '—'}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Created</dt><dd>{new Date(detailBatch.createdAt).toLocaleString()}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Started</dt><dd>{detailBatch.startedAt ? new Date(detailBatch.startedAt).toLocaleString() : '—'}</dd></div>
              <div className="flex justify-between"><dt className="text-fog">Completed</dt><dd>{detailBatch.completedAt ? new Date(detailBatch.completedAt).toLocaleString() : '—'}</dd></div>
            </dl>
            <div className="flex justify-end pt-2">
              <button onClick={() => setDetailBatch(null)} className="px-4 py-2 border rounded-lg text-sm text-fog hover:bg-gray-50">
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </Layout>
  );
}
