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

  const [activeTab, setActiveTab] = useState('stock'); // 'stock' | 'create' | 'batches' | 'tanks'

  const [stock, setStock] = useState([]);
  const [batches, setBatches] = useState([]);
  const [tanks, setTanks] = useState([]);
  const [treatedStock, setTreatedStock] = useState([]);
  const [statusFilter, setStatusFilter] = useState('');
  const [batchesLoading, setBatchesLoading] = useState(false);
  const [notification, setNotification] = useState({ type: '', text: '' });

  const [selectedKey, setSelectedKey] = useState(''); // "Species|Dimensions"
  const [chemicalType, setChemicalType] = useState(CHEMICAL_TYPES[0]);
  const [quantity, setQuantity] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [detailBatch, setDetailBatch] = useState(null);
  const [startModal, setStartModal] = useState({ open: false, batch: null });
  const [startTankId, setStartTankId] = useState('');
  const [startSubmitting, setStartSubmitting] = useState(false);
  const [completeModal, setCompleteModal] = useState({ open: false, batch: null });
  const [treatedM3, setTreatedM3] = useState('');
  const [rejectedM3, setRejectedM3] = useState('');
  const [completeSubmitting, setCompleteSubmitting] = useState(false);
  const [thresholdDrafts, setThresholdDrafts] = useState({});
  const [reportRange, setReportRange] = useState({ from: '', to: '' });
  const [report, setReport] = useState(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [reportLoaded, setReportLoaded] = useState(false);

  const saveThreshold = async (species, dimensions, chemicalType) => {
    const key = `${species}|${dimensions}|${chemicalType}`;
    const value = parseFloat(thresholdDrafts[key]);
    if (isNaN(value) || value < 0) {
      showNotification('error', 'Enter a non-negative threshold value.');
      return;
    }
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentStock/treated/threshold`, {
        method: 'PUT',
        body: JSON.stringify({ species, dimensions, chemicalType, thresholdM3: value }),
      });
      if (res.ok) {
        showNotification('success', `Threshold for ${species} (${dimensions}, ${chemicalType}) saved to ${value} m³.`);
        setThresholdDrafts((prev) => ({ ...prev, [key]: '' }));
      } else {
        const body = await res.json().catch(() => null);
        showNotification('error', body?.message || 'Failed to save threshold (Manager only).');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error saving threshold.');
    }
  };
  const [cancelModal, setCancelModal] = useState({ open: false, batch: null });
  const [cancelReason, setCancelReason] = useState('');
  const [cancelSubmitting, setCancelSubmitting] = useState(false);
  const isAdmin = user?.role === 'Admin';

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

  const fetchTanks = useCallback(async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/Tanks/availability`);
      if (res.ok) setTanks(await res.json());
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch tanks', err);
    }
  }, []);

  const fetchTreatedStock = useCallback(async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentStock/treated`);
      if (res.ok) setTreatedStock(await res.json());
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch treated stock', err);
    }
  }, []);

  useEffect(() => {
    fetchTanks();
  }, [fetchTanks]);

  useEffect(() => {
    fetchTreatedStock();
  }, [fetchTreatedStock]);

  const fetchReport = async (range) => {
    const r = range ?? reportRange;
    setReportLoading(true);
    try {
      const params = new URLSearchParams();
      if (r.from) params.set('from', `${r.from}T00:00:00.000Z`);
      if (r.to) params.set('to', `${r.to}T23:59:59.999Z`);
      const qs = params.toString();
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches/reports/duration${qs ? `?${qs}` : ''}`);
      if (res.ok) {
        setReport(await res.json());
        setReportLoaded(true);
      } else {
        const body = await res.json().catch(() => null);
        showNotification('error', body?.message || 'Failed to load duration report (Manager/Admin only).');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error loading duration report.');
    } finally {
      setReportLoading(false);
    }
  };

  useEffect(() => {
    if (activeTab === 'reports' && !reportLoaded && !reportLoading) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      fetchReport(reportRange);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, reportLoaded]);

  const applyReportRange = () => {
    if (reportRange.from && reportRange.to && reportRange.from > reportRange.to) {
      showNotification('error', 'From date must be on or before To date.');
      return;
    }
    fetchReport(reportRange);
  };

  const handleCompleteBatch = async () => {
    const batch = completeModal.batch;
    if (!batch) return;
    const treated = parseFloat(treatedM3);
    const rejected = parseFloat(rejectedM3 || '0');
    if (isNaN(treated) || isNaN(rejected) || treated < 0 || rejected < 0) {
      showNotification('error', 'Enter valid treated and rejected quantities.');
      return;
    }
    if (treated + rejected > Number(batch.quantityM3)) {
      showNotification('error', `Treated (${treated}) + rejected (${rejected}) exceeds batch input (${batch.quantityM3}) m³.`);
      return;
    }

    setCompleteSubmitting(true);
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches/${batch.batchId}/complete`, {
        method: 'PUT',
        body: JSON.stringify({ treatedM3: treated, rejectedM3: rejected }),
      });
      if (res.ok) {
        showNotification('success', `Batch ${batch.batchCode} completed.`);
        setCompleteModal({ open: false, batch: null });
        setTreatedM3('');
        setRejectedM3('');
        fetchBatches();
        fetchTanks();
        fetchTreatedStock();
      } else {
        const body = await res.json().catch(() => null);
        showNotification('error', body?.message || 'Failed to complete batch.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error completing batch.');
    } finally {
      setCompleteSubmitting(false);
    }
  };

  const handleCancelBatch = async () => {
    const batch = cancelModal.batch;
    if (!batch) return;
    if (!cancelReason.trim()) {
      showNotification('error', 'A cancellation reason is required.');
      return;
    }

    setCancelSubmitting(true);
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches/${batch.batchId}/cancel`, {
        method: 'PUT',
        body: JSON.stringify({ reason: cancelReason.trim() }),
      });
      if (res.ok) {
        showNotification('success', `Batch ${batch.batchCode} cancelled. Sawn stock restored.`);
        setCancelModal({ open: false, batch: null });
        setCancelReason('');
        fetchBatches();
        fetchTanks();
        fetchStock();
      } else {
        const body = await res.json().catch(() => null);
        showNotification('error', body?.message || 'Failed to cancel batch.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error cancelling batch.');
    } finally {
      setCancelSubmitting(false);
    }
  };

  const handleStartBatch = async () => {
    const batch = startModal.batch;
    if (!batch || !startTankId) return;
    setStartSubmitting(true);
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentBatches/${batch.batchId}/start`, {
        method: 'PUT',
        body: JSON.stringify({ tankId: parseInt(startTankId) }),
      });
      if (res.ok) {
        showNotification('success', `Batch ${batch.batchCode} started (InTreatment).`);
        setStartModal({ open: false, batch: null });
        setStartTankId('');
        fetchBatches();
        fetchTanks();
        fetchStock();
      } else {
        const body = await res.json().catch(() => null);
        showNotification('error', body?.message || 'Failed to start batch.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error starting batch.');
    } finally {
      setStartSubmitting(false);
    }
  };

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
            { id: 'tanks', label: 'Tank Schedule' },
            { id: 'treated', label: 'Treated Stock' },
            ...(['Admin', 'Manager'].includes(user?.role) ? [{ id: 'reports', label: 'Reports' }] : []),
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
                        <td className="py-3 px-4">
                          <input
                            type="number"
                            step="0.0001"
                            min="0"
                            placeholder="e.g. 5.0"
                            className="w-28 px-2 py-1.5 border border-charcoal/20 rounded-md text-xs bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                            value={thresholdDrafts[`${s.species}|${s.dimensions}|${s.chemicalType}`] ?? ''}
                            onChange={(e) => setThresholdDrafts((prev) => ({ ...prev, [`${s.species}|${s.dimensions}|${s.chemicalType}`]: e.target.value }))}
                          />
                        </td>
                        <td className="py-3 px-4 text-right">
                          <button
                            type="button"
                            onClick={() => saveThreshold(s.species, s.dimensions, s.chemicalType)}
                            className="px-3 py-1.5 bg-heartwood hover:bg-heartwood-dark text-white rounded-md text-xs font-medium transition-colors"
                          >
                            Save
                          </button>
                        </td>
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
                    <th className="py-3 px-4 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-charcoal/10">
                  {batchesLoading ? (
                    <tr><td colSpan="8" className="py-6 px-4 text-center text-fog">Loading batches…</td></tr>
                  ) : batches.length === 0 ? (
                    <tr><td colSpan="8" className="py-6 px-4 text-center text-fog">No batches found.</td></tr>
                  ) : (
                    batches.map((b) => (
                      <tr key={b.batchId} onClick={() => openDetail(b.batchId)} className={`cursor-pointer transition-colors ${b.status === 'Cancelled' ? 'hover:bg-sawdust/40 bg-gray-50/80 opacity-70' : 'hover:bg-sawdust/40'}`}>
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
                        <td className="py-3 px-4 text-right whitespace-nowrap">
                          {b.status === 'Pending' && (
                            <Button
                              variant="primary"
                              onClick={(e) => { e.stopPropagation(); setStartModal({ open: true, batch: b }); }}
                            >
                              Start Treatment
                            </Button>
                          )}
                          {b.status === 'InTreatment' && (
                            <button
                              onClick={(e) => { e.stopPropagation(); setCompleteModal({ open: true, batch: b }); setTreatedM3(String(b.quantityM3)); setRejectedM3('0'); }}
                              className="px-4 py-2 rounded-md font-medium text-sm transition-colors bg-moss text-white hover:brightness-110 ml-2"
                            >
                              Mark Complete
                            </button>
                          )}
                          {isAdmin && b.status === 'Pending' && (
                            <button
                              onClick={(e) => { e.stopPropagation(); setCancelModal({ open: true, batch: b }); setCancelReason(''); }}
                              className="px-4 py-2 rounded-md font-medium text-sm transition-colors bg-transparent text-rust border border-rust/40 hover:bg-rust/10 ml-2"
                            >
                              Cancel
                            </button>
                          )}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        )}

        {activeTab === 'treated' && (
          <Card className="overflow-hidden">
            <div className="p-4 border-b border-charcoal/10 bg-sawdust/60 flex justify-between items-center">
              <h2 className="text-base font-semibold text-charcoal">Treated Stock Inventory</h2>
              <Button variant="ghost" onClick={fetchTreatedStock}>Refresh</Button>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
                  <tr>
                    <th className="py-3 px-4">Species</th>
                    <th className="py-3 px-4">Dimensions</th>
                    <th className="py-3 px-4">Chemical Type</th>
                    <th className="py-3 px-4">Volume (m³)</th>
                    <th className="py-3 px-4">Last Updated</th>
                    <th className="py-3 px-4">Threshold (m³)</th>
                    <th className="py-3 px-4 text-right">Threshold</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-charcoal/10">
                  {treatedStock.length === 0 ? (
                    <tr><td colSpan="7" className="py-6 px-4 text-center text-fog">No treated stock yet.</td></tr>
                  ) : (
                    treatedStock.map((s) => (
                      <tr key={s.stockId} className="hover:bg-sawdust/40 transition-colors">
                        <td className="py-3 px-4 font-semibold text-charcoal">{s.species}</td>
                        <td className="py-3 px-4 text-charcoal">{s.dimensions}</td>
                        <td className="py-3 px-4 text-charcoal">{s.chemicalType}</td>
                        <td className="py-3 px-4 font-mono text-charcoal">{Number(s.volumeM3).toFixed(4)}</td>
                        <td className="py-3 px-4 text-fog">{new Date(s.lastUpdated).toLocaleString()}</td>
                        <td className="py-3 px-4">
                          <input
                            type="number"
                            step="0.0001"
                            min="0"
                            placeholder="e.g. 5.0"
                            className="w-28 px-2 py-1.5 border border-charcoal/20 rounded-md text-xs bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                            value={thresholdDrafts[`${s.species}|${s.dimensions}|${s.chemicalType}`] ?? ''}
                            onChange={(e) => setThresholdDrafts((prev) => ({ ...prev, [`${s.species}|${s.dimensions}|${s.chemicalType}`]: e.target.value }))}
                          />
                        </td>
                        <td className="py-3 px-4 text-right">
                          <button
                            type="button"
                            onClick={() => saveThreshold(s.species, s.dimensions, s.chemicalType)}
                            className="px-3 py-1.5 bg-heartwood hover:bg-heartwood-dark text-white rounded-md text-xs font-medium transition-colors"
                          >
                            Save
                          </button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        )}

        {activeTab === 'tanks' && (
          <Card className="overflow-hidden">
            <div className="p-4 border-b border-charcoal/10 bg-sawdust/60 flex justify-between items-center">
              <h2 className="text-base font-semibold text-charcoal">Tank Schedule &amp; Utilization</h2>
              <Button variant="ghost" onClick={fetchTanks}>Refresh</Button>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
                  <tr>
                    <th className="py-3 px-4">Tank</th>
                    <th className="py-3 px-4">Capacity (m³)</th>
                    <th className="py-3 px-4">Status</th>
                    <th className="py-3 px-4">Current Batch</th>
                    <th className="py-3 px-4">Quantity (m³)</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-charcoal/10">
                  {tanks.length === 0 ? (
                    <tr><td colSpan="5" className="py-6 px-4 text-center text-fog">No tanks configured.</td></tr>
                  ) : (
                    tanks.map((t) => (
                      <tr key={t.tankId} className="hover:bg-sawdust/40 transition-colors">
                        <td className="py-3 px-4 font-mono text-xs font-bold text-charcoal">{t.tankCode}</td>
                        <td className="py-3 px-4 font-mono text-charcoal">{Number(t.capacityM3).toFixed(4)}</td>
                        <td className="py-3 px-4">
                          <span className={`px-2.5 py-1 rounded-full text-xs font-semibold whitespace-nowrap ${
                            t.status === 'Busy' ? 'bg-heartwood/15 text-heartwood' : 'bg-moss/15 text-moss'
                          }`}>
                            {t.status}
                          </span>
                        </td>
                        <td className="py-3 px-4 text-charcoal">{t.currentBatchCode || '—'}</td>
                        <td className="py-3 px-4 font-mono text-charcoal">{t.currentBatchQuantityM3 != null ? Number(t.currentBatchQuantityM3).toFixed(4) : '—'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        )}

        {activeTab === 'reports' && (
          <Card className="overflow-hidden">
            <div className="p-4 border-b border-charcoal/10 bg-sawdust/60 flex flex-wrap gap-3 justify-between items-center">
              <h2 className="text-base font-semibold text-charcoal">Treatment Turnaround &amp; Duration Report</h2>
              <div className="flex flex-wrap items-center gap-2">
                <label className="text-xs font-medium text-fog">From</label>
                <input
                  type="date"
                  value={reportRange.from}
                  onChange={(e) => setReportRange((prev) => ({ ...prev, from: e.target.value }))}
                  className="px-2 py-1.5 border border-charcoal/20 rounded-md text-xs bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                />
                <label className="text-xs font-medium text-fog">To</label>
                <input
                  type="date"
                  value={reportRange.to}
                  onChange={(e) => setReportRange((prev) => ({ ...prev, to: e.target.value }))}
                  className="px-2 py-1.5 border border-charcoal/20 rounded-md text-xs bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                />
                <Button variant="primary" onClick={applyReportRange} disabled={reportLoading}>
                  {reportLoading ? 'Loading…' : 'Apply Range'}
                </Button>
                <Button
                  variant="ghost"
                  onClick={() => {
                    const cleared = { from: '', to: '' };
                    setReportRange(cleared);
                    fetchReport(cleared);
                  }}
                  disabled={reportLoading}
                >
                  All Time
                </Button>
              </div>
            </div>

            {!report ? (
              <div className="py-10 px-4 text-center text-fog">
                {reportLoading ? 'Loading report…' : 'No report data yet. Click "All Time" or apply a date range.'}
              </div>
            ) : (
              <div className="p-5 space-y-6">
                {/* Summary cards */}
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                  <SummaryCard
                    label="Completed Batches"
                    value={report.completedBatches}
                    accent="text-heartwood"
                  />
                  <SummaryCard
                    label="Active Cycles"
                    value={report.activeCycles}
                    accent="text-moss"
                  />
                  <SummaryCard
                    label="Avg Turnaround (h)"
                    value={report.averageTurnaroundHours != null ? Number(report.averageTurnaroundHours).toFixed(2) : '—'}
                    accent="text-rust"
                  />
                </div>

                {report.completedBatches === 0 ? (
                  <div className="py-8 px-4 rounded-lg border border-dashed border-charcoal/20 bg-sawdust/40 text-center">
                    <p className="font-medium text-charcoal">No completed batches in the selected range.</p>
                    <p className="text-sm text-fog mt-1">
                      Adjust the date range to include completed treatment cycles.
                      {report.activeCycles > 0 && ` (${report.activeCycles} active cycle${report.activeCycles === 1 ? '' : 's'} currently in treatment.)`}
                    </p>
                  </div>
                ) : (
                  <>
                    {/* Comparison chart: average turnaround per chemical type */}
                    <div>
                      <h3 className="text-sm font-semibold text-charcoal mb-3">Average Turnaround by Chemical Type (hours)</h3>
                      <div className="space-y-2.5">
                        {report.byChemicalType.map((c) => {
                          const maxAvg = Math.max(...report.byChemicalType.map((x) => x.averageDurationHours), 0.0001);
                          const pct = Math.max(4, (c.averageDurationHours / maxAvg) * 100);
                          return (
                            <div key={c.chemicalType} className="flex items-center gap-3">
                              <span className="w-56 shrink-0 text-xs text-charcoal truncate" title={c.chemicalType}>
                                {c.chemicalType}
                              </span>
                              <div className="flex-1 bg-sawdust rounded-md h-6 overflow-hidden">
                                <div
                                  className="h-6 bg-heartwood/80 rounded-md flex items-center justify-end pr-2 transition-all"
                                  style={{ width: `${pct}%` }}
                                >
                                  <span className="text-[10px] font-mono font-semibold text-white whitespace-nowrap">
                                    {c.averageDurationHours.toFixed(2)}h
                                  </span>
                                </div>
                              </div>
                              <span className="w-20 text-right text-xs font-mono text-fog">{c.completedCount} batch{c.completedCount === 1 ? '' : 'es'}</span>
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    {/* Detail table */}
                    <div className="overflow-x-auto">
                      <table className="w-full text-left text-sm">
                        <thead className="bg-sawdust/60 text-fog border-b border-charcoal/10">
                          <tr>
                            <th className="py-3 px-4">Chemical Type</th>
                            <th className="py-3 px-4">Completed</th>
                            <th className="py-3 px-4">Avg Duration (h)</th>
                            <th className="py-3 px-4">Min (h)</th>
                            <th className="py-3 px-4">Max (h)</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-charcoal/10">
                          {report.byChemicalType.map((c) => (
                            <tr key={c.chemicalType} className="hover:bg-sawdust/40 transition-colors">
                              <td className="py-3 px-4 font-semibold text-charcoal">{c.chemicalType}</td>
                              <td className="py-3 px-4 font-mono text-charcoal">{c.completedCount}</td>
                              <td className="py-3 px-4 font-mono text-charcoal">{c.averageDurationHours.toFixed(2)}</td>
                              <td className="py-3 px-4 font-mono text-charcoal">{c.minDurationHours.toFixed(2)}</td>
                              <td className="py-3 px-4 font-mono text-charcoal">{c.maxDurationHours.toFixed(2)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </>
                )}
              </div>
            )}
          </Card>
        )}
      </div>

      {/* Cancel Batch Modal */}
      {cancelModal.open && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
            <h3 className="text-lg font-bold text-charcoal">Cancel Treatment Batch</h3>
            <p className="text-sm text-fog">
              Void batch <span className="font-semibold text-charcoal font-mono">{cancelModal.batch?.batchCode}</span>?
              The allocated sawn stock will be restored to inventory.
            </p>
            <div>
              <label className="block text-sm font-semibold text-charcoal mb-1.5">Cancellation Reason *</label>
              <input
                type="text"
                value={cancelReason}
                onChange={(e) => setCancelReason(e.target.value)}
                required
                className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-heartwood/40"
                placeholder="e.g. Schedule changed by supply chain"
              />
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => { setCancelModal({ open: false, batch: null }); setCancelReason(''); }}
                className="px-4 py-2 border rounded-lg text-sm text-fog hover:bg-gray-50"
              >
                Back
              </button>
              <button
                type="button"
                onClick={handleCancelBatch}
                disabled={cancelSubmitting || !cancelReason.trim()}
                className="px-4 py-2 bg-rust hover:brightness-110 text-white rounded-lg text-sm font-medium transition-colors disabled:opacity-50"
              >
                {cancelSubmitting ? 'Cancelling…' : 'Confirm Cancel'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Mark Complete Modal */}
      {completeModal.open && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
            <h3 className="text-lg font-bold text-charcoal">Mark Batch Complete</h3>
            <p className="text-sm text-fog">
              Finalize batch <span className="font-semibold text-charcoal font-mono">{completeModal.batch?.batchCode}</span>{' '}
              (input {Number(completeModal.batch?.quantityM3 ?? 0).toFixed(4)} m³).
            </p>
            <div>
              <label className="block text-sm font-semibold text-charcoal mb-1.5">Treated Quantity (m³) *</label>
              <input
                type="number"
                step="0.0001"
                min="0"
                value={treatedM3}
                onChange={(e) => setTreatedM3(e.target.value)}
                className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-heartwood/40"
              />
            </div>
            <div>
              <label className="block text-sm font-semibold text-charcoal mb-1.5">Rejected Quantity (m³)</label>
              <input
                type="number"
                step="0.0001"
                min="0"
                value={rejectedM3}
                onChange={(e) => setRejectedM3(e.target.value)}
                className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-heartwood/40"
              />
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => { setCompleteModal({ open: false, batch: null }); setTreatedM3(''); setRejectedM3(''); }}
                className="px-4 py-2 border rounded-lg text-sm text-fog hover:bg-gray-50"
              >
                Cancel
              </button>
              <Button onClick={handleCompleteBatch} disabled={completeSubmitting}>
                {completeSubmitting ? 'Completing…' : 'Complete Batch'}
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* Start Treatment Modal */}
      {startModal.open && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
            <h3 className="text-lg font-bold text-charcoal">Start Treatment</h3>
            <p className="text-sm text-fog">
              Assign batch <span className="font-semibold text-charcoal font-mono">{startModal.batch?.batchCode}</span>{' '}
              ({Number(startModal.batch?.quantityM3 ?? 0).toFixed(4)} m³) to a tank.
            </p>
            <div>
              <label className="block text-sm font-semibold text-charcoal mb-1.5">Treatment Tank *</label>
              <select
                value={startTankId}
                onChange={(e) => setStartTankId(e.target.value)}
                className="w-full px-3 py-2.5 border border-charcoal/20 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/40"
              >
                <option value="">— Select an idle tank —</option>
                {tanks.filter((t) => t.status === 'Idle').map((t) => (
                  <option key={t.tankId} value={t.tankId}>
                    {t.tankCode} (capacity {Number(t.capacityM3).toFixed(4)} m³)
                  </option>
                ))}
              </select>
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => { setStartModal({ open: false, batch: null }); setStartTankId(''); }}
                className="px-4 py-2 border rounded-lg text-sm text-fog hover:bg-gray-50"
              >
                Cancel
              </button>
              <Button onClick={handleStartBatch} disabled={startSubmitting || !startTankId}>
                {startSubmitting ? 'Starting…' : 'Start Treatment'}
              </Button>
            </div>
          </div>
        </div>
      )}

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

function SummaryCard({ label, value, accent }) {
  return (
    <Card className="p-4">
      <p className="text-xs font-medium text-fog uppercase tracking-wide">{label}</p>
      <p className={`font-display text-3xl font-semibold mt-1 ${accent}`}>{value}</p>
    </Card>
  );
}
