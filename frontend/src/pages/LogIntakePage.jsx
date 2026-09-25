import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import { fetchWithAuth } from '../utils/api';
import Layout from '../components/Layout';

const API_BASE_URL = import.meta.env.VITE_LOG_INTAKE_API_URL || 'http://localhost:5201/api/LogIntake';

export default function LogIntakePage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const isAuthorizedIntake = ['Admin', 'Manager', 'Supervisor'].includes(user?.role);

  const [activeTab, setActiveTab] = useState('stock'); // 'stock', 'logs', 'intake', 'history', 'suppliers'

  // Data States
  const [stocks, setStocks] = useState([]);
  const [logs, setLogs] = useState([]);
  const [deliveries, setDeliveries] = useState([]);
  const [suppliers, setSuppliers] = useState([]);
  const [allSuppliers, setAllSuppliers] = useState([]); // includes inactive for Suppliers tab
  const [speciesList, setSpeciesList] = useState([]);
  const [logLengths, setLogLengths] = useState([]);

  // Expanded sub-rows for Log Inventory (set of logIds)
  const [expandedLogIds, setExpandedLogIds] = useState(new Set());

  // Filter States for Log Inventory (SpeciesId and LengthId only)
  const [selectedSpeciesFilter, setSelectedSpeciesFilter] = useState('');
  const [selectedLengthFilter, setSelectedLengthFilter] = useState('');

  // Loading & Alerts
  const [loading, setLoading] = useState(false);
  const [logsLoading, setLogsLoading] = useState(false);
  const [suppliersLoading, setSuppliersLoading] = useState(false);
  const [message, setMessage] = useState({ type: '', text: '' });

  // Record Delivery Form State:
  // Delivery-level: Supplier, VehicleNumber, Notes
  // Dynamic rows: Species, Length, Girth (ft) - No Grade
  const [deliveryForm, setDeliveryForm] = useState({
    supplierId: '',
    vehicleNumber: '',
    notes: '',
    logRows: [
      { speciesId: '', lengthId: '', girthFt: '' }
    ]
  });

  // Modal for Soft Deleting a Log (Admin-only with reason)
  const [removeLogModal, setRemoveLogModal] = useState({
    isOpen: false,
    logId: null,
    reason: ''
  });

  // Add Supplier Form State
  const [supplierForm, setSupplierForm] = useState({
    name: '',
    phoneNumber: '',
    address: ''
  });

  // Modal for Deactivating Supplier (Admin-only)
  const [deactivateSupplierModal, setDeactivateSupplierModal] = useState({
    isOpen: false,
    supplierId: null,
    supplierName: ''
  });

  // Modal for Manual Stock Adjustment (legacy)
  const [adjustModal, setAdjustModal] = useState({
    isOpen: false,
    species: '',
    grade: '',
    volumeDelta: '',
    reason: ''
  });


  useEffect(() => {
    fetchStock();
    fetchDeliveries();
    fetchSuppliers();
    fetchSpecies();
    fetchLogLengths();
    fetchLogs('', '');
  }, []);

  const showNotification = (type, text) => {
    setMessage({ type, text });
    setTimeout(() => setMessage({ type: '', text: '' }), 4000);
  };

  const toggleExpandLog = (logId) => {
    setExpandedLogIds((prev) => {
      const next = new Set(prev);
      if (next.has(logId)) {
        next.delete(logId);
      } else {
        next.add(logId);
      }
      return next;
    });
  };

  // 1. Fetch Raw Stock Summary
  const fetchStock = async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/stock`);
      if (res.ok) {
        const data = await res.json();
        setStocks(data);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch stock', err);
    } finally {
      setLoading(false);
    }
  };

  // 2. Fetch Deliveries
  const fetchDeliveries = async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/deliveries`);
      if (res.ok) {
        const data = await res.json();
        setDeliveries(data);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch deliveries', err);
    }
  };

  // 3. Fetch Suppliers (Active only for dropdown, plus all for management)
  const fetchSuppliers = async () => {
    setSuppliersLoading(true);
    try {
      const [activeRes, allRes] = await Promise.all([
        fetchWithAuth(`${API_BASE_URL}/suppliers`),
        fetchWithAuth(`${API_BASE_URL}/suppliers?includeInactive=true`)
      ]);

      if (activeRes.ok) {
        const activeData = await activeRes.json();
        setSuppliers(activeData);
      }
      if (allRes.ok) {
        const allData = await allRes.json();
        setAllSuppliers(allData);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch suppliers', err);
    } finally {
      setSuppliersLoading(false);
    }
  };

  // 4. Fetch Species lookup
  const fetchSpecies = async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/species`);
      if (res.ok) {
        const data = await res.json();
        setSpeciesList(data);
        if (data.length > 0) {
          setDeliveryForm((prev) => ({
            ...prev,
            logRows: prev.logRows.map((r) => ({
              ...r,
              speciesId: r.speciesId || data[0].speciesId
            }))
          }));
        }
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch species', err);
    }
  };

  // 5. Fetch Log Lengths lookup
  const fetchLogLengths = async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/log-lengths`);
      if (res.ok) {
        const data = await res.json();
        setLogLengths(data);
        if (data.length > 0) {
          setDeliveryForm((prev) => ({
            ...prev,
            logRows: prev.logRows.map((r) => ({
              ...r,
              lengthId: r.lengthId || data[0].lengthId
            }))
          }));
        }
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch log lengths', err);
    }
  };

  // 6. Fetch Individual Logs (with optional speciesId and lengthId filters)
  const fetchLogs = async (
    speciesId = selectedSpeciesFilter,
    lengthId = selectedLengthFilter
  ) => {
    setLogsLoading(true);
    try {
      const params = new URLSearchParams();
      if (speciesId) params.append('speciesId', speciesId);
      if (lengthId) params.append('lengthId', lengthId);

      const qs = params.toString() ? `?${params.toString()}` : '';
      const res = await fetchWithAuth(`${API_BASE_URL}/logs${qs}`);
      if (res.ok) {
        const data = await res.json();
        setLogs(data);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch logs', err);
    } finally {
      setLogsLoading(false);
    }
  };

  // Filter change handlers for Log Inventory
  const handleFilterChange = (speciesId, lengthId) => {
    setSelectedSpeciesFilter(speciesId);
    setSelectedLengthFilter(lengthId);
    fetchLogs(speciesId, lengthId);
  };

  const clearFilters = () => {
    setSelectedSpeciesFilter('');
    setSelectedLengthFilter('');
    fetchLogs('', '');
  };

  // Dynamic Log Rows Handlers (Each row has its own Species, Length, Girth)
  const addLogRow = () => {
    setDeliveryForm((prev) => ({
      ...prev,
      logRows: [
        ...prev.logRows,
        {
          speciesId: speciesList[0]?.speciesId || '',
          lengthId: logLengths[0]?.lengthId || '',
          girthFt: ''
        }
      ]
    }));
  };

  const removeLogRow = (index) => {
    if (deliveryForm.logRows.length <= 1) return;
    setDeliveryForm((prev) => ({
      ...prev,
      logRows: prev.logRows.filter((_, i) => i !== index)
    }));
  };

  const updateLogRowField = (index, field, value) => {
    setDeliveryForm((prev) => {
      const updated = [...prev.logRows];
      updated[index] = { ...updated[index], [field]: value };
      return { ...prev, logRows: updated };
    });
  };

  // 7. Record Delivery Submit
  const handleDeliverySubmit = async (e) => {
    e.preventDefault();

    if (!deliveryForm.supplierId) {
      showNotification('error', 'Please select a supplier.');
      return;
    }

    // Validate each row
    for (let i = 0; i < deliveryForm.logRows.length; i++) {
      const row = deliveryForm.logRows[i];
      if (!row.speciesId) {
        showNotification('error', `Log #${i + 1}: Please select a timber species.`);
        return;
      }
      if (!row.lengthId) {
        showNotification('error', `Log #${i + 1}: Please select a standard length.`);
        return;
      }
      const g = parseFloat(row.girthFt);
      if (isNaN(g) || g <= 0) {
        showNotification('error', `Log #${i + 1}: Please provide a valid positive girth.`);
        return;
      }
    }

    const payload = {
      supplierId: parseInt(deliveryForm.supplierId),
      vehicleNumber: deliveryForm.vehicleNumber.trim() || null,
      receivedBy: user?.userId ? parseInt(user.userId) : 1,
      logCount: deliveryForm.logRows.length,
      notes: deliveryForm.notes.trim() || null,
      logs: deliveryForm.logRows.map((row) => ({
        speciesId: parseInt(row.speciesId),
        lengthId: parseInt(row.lengthId),
        girthFt: parseFloat(row.girthFt)
      }))
    };

    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/deliveries`, {
        method: 'POST',
        body: JSON.stringify(payload)
      });

      if (res.ok) {
        showNotification('success', `Delivery recorded successfully with ${payload.logs.length} logs!`);
        setDeliveryForm({
          supplierId: '',
          vehicleNumber: '',
          notes: '',
          logRows: [
            {
              speciesId: speciesList[0]?.speciesId || '',
              lengthId: logLengths[0]?.lengthId || '',
              girthFt: ''
            }
          ]
        });
        fetchStock();
        fetchDeliveries();
        fetchLogs();
        setActiveTab('logs');
      } else {
        const errData = await res.json().catch(() => null);
        showNotification('error', errData?.message || 'Failed to record delivery. Ensure all fields are valid.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error connecting to LogIntakeService.');
    }
  };

  // 8. Admin-Only Soft Delete Log with Reason
  const handleRemoveLogSubmit = async (e) => {
    e.preventDefault();
    if (!removeLogModal.reason.trim()) {
      showNotification('error', 'A removal reason is required.');
      return;
    }

    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/logs/${removeLogModal.logId}/remove`, {
        method: 'PUT',
        body: JSON.stringify({ reason: removeLogModal.reason.trim() })
      });

      if (res.ok) {
        showNotification('success', `Log #${removeLogModal.logId} removed from inventory.`);
        setRemoveLogModal({ isOpen: false, logId: null, reason: '' });
        fetchLogs();
        fetchStock(); // Live stock view automatically reflects reduced count/volume
      } else {
        const errData = await res.json().catch(() => null);
        showNotification('error', errData?.message || 'Failed to remove log.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error while removing log.');
    }
  };

  // 9. Create New Supplier Submit (Admin, Manager, Supervisor)
  const handleCreateSupplierSubmit = async (e) => {
    e.preventDefault();
    if (!supplierForm.name.trim() || !supplierForm.phoneNumber.trim()) {
      showNotification('error', 'Supplier name and phone number are required.');
      return;
    }

    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/suppliers`, {
        method: 'POST',
        body: JSON.stringify({
          name: supplierForm.name.trim(),
          phoneNumber: supplierForm.phoneNumber.trim(),
          address: supplierForm.address.trim() || null
        })
      });

      if (res.ok) {
        showNotification('success', `Supplier "${supplierForm.name}" created successfully!`);
        setSupplierForm({ name: '', phoneNumber: '', address: '' });
        fetchSuppliers();
      } else {
        const errData = await res.json().catch(() => null);
        showNotification('error', errData?.message || 'Failed to create supplier.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error creating supplier.');
    }
  };

  // 10. Admin-Only Deactivate Supplier
  const handleDeactivateSupplierConfirm = async () => {
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/suppliers/${deactivateSupplierModal.supplierId}/deactivate`, {
        method: 'PUT',
      });

      if (res.ok) {
        showNotification('success', `Supplier "${deactivateSupplierModal.supplierName}" deactivated.`);
        setDeactivateSupplierModal({ isOpen: false, supplierId: null, supplierName: '' });
        fetchSuppliers();
      } else {
        const errData = await res.json().catch(() => null);
        showNotification('error', errData?.message || 'Failed to deactivate supplier.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error deactivating supplier.');
    }
  };

  // 11. Legacy Stock Adjustment Submit
  const handleAdjustSubmit = async (e) => {
    e.preventDefault();
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/stock/adjust`, {
        method: 'POST',
        body: JSON.stringify({
          species: adjustModal.species,
          grade: adjustModal.grade,
          adjustedVolumeM3: parseFloat(adjustModal.volumeDelta),
          reason: adjustModal.reason,
          adjustedBy: user?.userId ? parseInt(user.userId) : 1
        })
      });

      if (res.ok) {
        showNotification('success', `Adjustment saved for ${adjustModal.species} (${adjustModal.grade})!`);
        setAdjustModal({ isOpen: false, species: '', grade: '', volumeDelta: '', reason: '' });
        fetchStock();
      } else {
        showNotification('error', 'Failed to adjust stock. Check permissions.');
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') showNotification('error', 'Network error adjusting stock.');
    }
  };

  return (
    <Layout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex justify-between items-center border-b pb-4">
          <div>
            <h1 className="text-2xl font-bold text-gray-800">Log Intake &amp; Inventory Management</h1>
            <p className="text-sm text-gray-500">
              Record multi-log timber deliveries, manage log inventory with soft removal, and maintain supplier master data
            </p>
          </div>
          <span className="px-3 py-1 bg-amber-100 text-amber-800 rounded-full text-xs font-semibold">
            Role: {user?.role || 'Guest'}
          </span>
        </div>

        {/* Alert Notification */}
        {message.text && (
          <div className={`p-4 rounded-lg text-sm font-medium ${
            message.type === 'success' ? 'bg-emerald-100 text-emerald-800 border border-emerald-300' : 'bg-rose-100 text-rose-800 border border-rose-300'
          }`}>
            {message.text}
          </div>
        )}

        {/* Tabs */}
        <div className="flex flex-wrap gap-2 border-b border-gray-200">
          <button
            onClick={() => setActiveTab('stock')}
            className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
              activeTab === 'stock' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            Raw Stock Overview
          </button>
          <button
            onClick={() => setActiveTab('logs')}
            className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
              activeTab === 'logs' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            Log Inventory
          </button>
          <button
            onClick={() => setActiveTab('intake')}
            className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
              activeTab === 'intake' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            + Record Delivery
          </button>
          <button
            onClick={() => setActiveTab('history')}
            className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
              activeTab === 'history' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            Delivery History
          </button>
          <button
            onClick={() => setActiveTab('suppliers')}
            className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
              activeTab === 'suppliers' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            Suppliers
          </button>
        </div>

        {/* TAB 1: Raw Stock Overview */}
        {activeTab === 'stock' && (
          <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
            <div className="p-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
              <div>
                <h2 className="font-semibold text-gray-800">Current Stock by Species &amp; Length</h2>
                <p className="text-xs text-gray-500">Live rollup dynamically derived from all in-stock logs</p>
              </div>
              <button onClick={fetchStock} className="text-xs bg-white border border-gray-300 px-3 py-1.5 rounded-md hover:bg-gray-50">
                Refresh
              </button>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-gray-50 text-gray-600 border-b">
                  <tr>
                    <th className="py-3 px-4">Stock</th>
                    <th className="py-3 px-4">Species</th>
                    <th className="py-3 px-4">Length (ft)</th>
                    <th className="py-3 px-4">Log Count</th>
                    <th className="py-3 px-4">Total Volume (m³)</th>
                    <th className="py-3 px-4">Status &amp; Alert</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {stocks.length === 0 ? (
                    <tr>
                      <td colSpan="6" className="py-6 text-center text-gray-400">
                        {loading ? 'Loading stock...' : 'No in-stock logs available.'}
                      </td>
                    </tr>
                  ) : (
                    stocks.map((item, idx) => {
                      const isLow = item.isLowStock;
                      return (
                        <tr key={item.stockId || `${item.speciesId}-${item.lengthId}-${idx}`} className="hover:bg-gray-50">
                          <td className="py-3 px-4 font-mono text-xs font-bold text-gray-700">STK-{item.stockId}</td>
                          <td className="py-3 px-4 font-semibold text-gray-800">{item.species}</td>
                          <td className="py-3 px-4 text-gray-600">{item.lengthFt} ft</td>
                          <td className="py-3 px-4 font-semibold text-gray-700">{item.logCount}</td>
                          <td className="py-3 px-4 font-bold">{Number(item.totalVolumeM3).toFixed(4)} m³</td>
                          <td className="py-3 px-4">
                            {isLow ? (
                              <span className="px-2.5 py-1 text-xs font-semibold rounded-full bg-rose-100 text-rose-700 border border-rose-200">
                                ⚠️ Low Stock (≤ {item.lowStockThreshold} m³)
                              </span>
                            ) : (
                              <span className="px-2.5 py-1 text-xs font-semibold rounded-full bg-emerald-100 text-emerald-700">
                                Adequate Stock
                              </span>
                            )}
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* TAB 2: Log Inventory (Individual Logs with Species & Length Filters, Expandable Delivery/Supplier Row & Admin Soft-Delete) */}
        {activeTab === 'logs' && (
          <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden space-y-4">
            <div className="p-4 border-b border-gray-100 flex flex-wrap justify-between items-center gap-4 bg-gray-50">
              <div className="flex items-center gap-3">
                <h2 className="font-semibold text-gray-800">In-Stock Individual Logs</h2>
                <span className="text-xs bg-amber-100 text-amber-800 px-2.5 py-0.5 rounded-full font-medium">
                  {logs.length} {logs.length === 1 ? 'log' : 'logs'}
                </span>
              </div>

              {/* Combinable Filters: SpeciesId, LengthId */}
              <div className="flex flex-wrap items-center gap-2">
                {/* Species filter */}
                <select
                  value={selectedSpeciesFilter}
                  onChange={(e) => handleFilterChange(e.target.value, selectedLengthFilter)}
                  className="px-2.5 py-1.5 border border-gray-300 rounded-md text-xs bg-white focus:ring-1 focus:ring-amber-500"
                >
                  <option value="">All Species</option>
                  {speciesList.map((s) => (
                    <option key={s.speciesId} value={s.speciesId}>
                      {s.name}
                    </option>
                  ))}
                </select>

                {/* Length filter */}
                <select
                  value={selectedLengthFilter}
                  onChange={(e) => handleFilterChange(selectedSpeciesFilter, e.target.value)}
                  className="px-2.5 py-1.5 border border-gray-300 rounded-md text-xs bg-white focus:ring-1 focus:ring-amber-500"
                >
                  <option value="">All Lengths</option>
                  {logLengths.map((l) => (
                    <option key={l.lengthId} value={l.lengthId}>
                      {l.lengthFt} ft
                    </option>
                  ))}
                </select>

                {(selectedSpeciesFilter || selectedLengthFilter) && (
                  <button
                    onClick={clearFilters}
                    className="text-xs text-amber-700 hover:text-amber-900 font-medium px-2 py-1"
                  >
                    Clear Filters
                  </button>
                )}

                <button
                  onClick={() => fetchLogs()}
                  className="text-xs bg-white border border-gray-300 px-3 py-1.5 rounded-md hover:bg-gray-50"
                >
                  Refresh
                </button>
              </div>
            </div>

            {/* Horizontally scrolling container with bumped font size */}
            <div className="overflow-x-auto">
              <table className="w-full text-left text-base">
                <thead className="bg-gray-50 text-gray-600 border-b">
                  <tr>
                    <th className="py-3 px-4 w-10"></th>
                    <th className="py-3 px-4">Log ID</th>
                    <th className="py-3 px-4">Species</th>
                    <th className="py-3 px-4">Length (ft)</th>
                    <th className="py-3 px-4">Girth (ft)</th>
                    <th className="py-3 px-4">Volume (m³)</th>
                    {isAdmin && <th className="py-3 px-4 text-right">Actions</th>}
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {logs.length === 0 ? (
                    <tr>
                      <td colSpan={isAdmin ? 7 : 6} className="py-6 text-center text-gray-400">
                        {logsLoading ? 'Loading logs...' : 'No in-stock logs match the selected criteria.'}
                      </td>
                    </tr>
                  ) : (
                    logs.map((lg) => {
                      const isExpanded = expandedLogIds.has(lg.logId);
                      return (
                        <React.Fragment key={lg.logId}>
                          <tr className="hover:bg-gray-50 transition-colors">
                            <td className="py-3 px-4 w-10 text-center">
                              <button
                                type="button"
                                onClick={() => toggleExpandLog(lg.logId)}
                                className="text-gray-400 hover:text-gray-700 p-1 rounded transition-transform duration-150 inline-flex items-center justify-center"
                                title={isExpanded ? 'Collapse details' : 'Expand details'}
                              >
                                <svg
                                  className={`w-4 h-4 transform transition-transform ${isExpanded ? 'rotate-90 text-amber-700' : ''}`}
                                  fill="none"
                                  stroke="currentColor"
                                  viewBox="0 0 24 24"
                                >
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                                </svg>
                              </button>
                            </td>
                            <td className="py-3 px-4 font-mono font-bold text-gray-700">LOG-{lg.logId}</td>
                            <td className="py-3 px-4 font-semibold text-gray-800">{lg.speciesName}</td>
                            <td className="py-3 px-4 text-gray-600">{lg.lengthFt} ft</td>
                            <td className="py-3 px-4 text-gray-600">{lg.girthFt} ft</td>
                            <td className="py-3 px-4 font-bold text-gray-800">{Number(lg.volumeM3).toFixed(4)} m³</td>
                            {isAdmin && (
                              <td className="py-3 px-4 text-right">
                                <button
                                  onClick={() => setRemoveLogModal({ isOpen: true, logId: lg.logId, reason: '' })}
                                  className="text-xs px-2.5 py-1 text-rose-600 hover:text-rose-700 hover:bg-rose-50 border border-rose-200 rounded font-medium transition-colors"
                                >
                                  Delete
                                </button>
                              </td>
                            )}
                          </tr>
                          {isExpanded && (
                            <tr className="bg-amber-50/50 border-b border-gray-100">
                              <td></td>
                              <td colSpan={isAdmin ? 6 : 5} className="py-2.5 px-4 text-sm text-gray-600">
                                <span className="inline-flex items-center gap-2">
                                  <span className="font-semibold text-gray-700">Delivery #{lg.deliveryId}</span>
                                  <span>·</span>
                                  <span className="text-gray-800">{lg.supplierName || 'Unknown Supplier'}</span>
                                </span>
                              </td>
                            </tr>
                          )}
                        </React.Fragment>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* TAB 3: Record Delivery Form (No Grade Input) */}
        {activeTab === 'intake' && (
          <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200 max-w-4xl">
            <h2 className="text-lg font-semibold text-gray-800 mb-4">Record New Timber Delivery</h2>
            <form onSubmit={handleDeliverySubmit} className="space-y-6">
              
              {/* Delivery Header Level Details */}
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Supplier *</label>
                  <select
                    required
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                    value={deliveryForm.supplierId}
                    onChange={(e) => setDeliveryForm({ ...deliveryForm, supplierId: e.target.value })}
                  >
                    <option value="">— Select a supplier —</option>
                    {suppliers.map((s) => (
                      <option key={s.supplierId} value={s.supplierId}>
                        {s.supplierName}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Vehicle Plate</label>
                  <input
                    type="text"
                    maxLength={20}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                    placeholder="e.g. WP-CA-4521"
                    value={deliveryForm.vehicleNumber}
                    onChange={(e) => setDeliveryForm({ ...deliveryForm, vehicleNumber: e.target.value })}
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Inspection Notes</label>
                  <input
                    type="text"
                    maxLength={500}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                    placeholder="e.g. Inspected upon arrival"
                    value={deliveryForm.notes}
                    onChange={(e) => setDeliveryForm({ ...deliveryForm, notes: e.target.value })}
                  />
                </div>
              </div>

              {/* Physical Log Entries: Each row has independent Species, Length, Girth */}
              <div className="border border-gray-200 rounded-lg p-4 bg-gray-50/50 space-y-3">
                <div className="flex justify-between items-center">
                  <div>
                    <h3 className="text-sm font-semibold text-gray-800">Physical Log Entries</h3>
                    <p className="text-xs text-gray-500">
                      Configure species, length, and girth individually for each physical log in this delivery. Total logs: <span className="font-bold text-amber-700">{deliveryForm.logRows.length}</span>
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={addLogRow}
                    className="px-3 py-1.5 bg-amber-600 hover:bg-amber-700 text-white rounded-md text-xs font-medium transition-colors"
                  >
                    + Add Another Log
                  </button>
                </div>

                <div className="space-y-3 max-h-96 overflow-y-auto pr-1">
                  {deliveryForm.logRows.map((row, idx) => (
                    <div key={idx} className="bg-white p-3 rounded-lg border border-gray-200 flex flex-wrap items-center gap-3">
                      <span className="text-xs font-bold text-gray-500 w-14">Log #{idx + 1}</span>

                      {/* Species Dropdown */}
                      <div className="flex-1 min-w-[140px]">
                        <label className="block text-[11px] font-medium text-gray-600 mb-0.5">Species *</label>
                        <select
                          required
                          value={row.speciesId}
                          onChange={(e) => updateLogRowField(idx, 'speciesId', e.target.value)}
                          className="w-full px-2.5 py-1.5 border border-gray-300 rounded-md text-xs focus:ring-1 focus:ring-amber-500 bg-white"
                        >
                          <option value="">— Species —</option>
                          {speciesList.map((s) => (
                            <option key={s.speciesId} value={s.speciesId}>
                              {s.name}
                            </option>
                          ))}
                        </select>
                      </div>

                      {/* Length Dropdown */}
                      <div className="w-32">
                        <label className="block text-[11px] font-medium text-gray-600 mb-0.5">Length (ft) *</label>
                        <select
                          required
                          value={row.lengthId}
                          onChange={(e) => updateLogRowField(idx, 'lengthId', e.target.value)}
                          className="w-full px-2.5 py-1.5 border border-gray-300 rounded-md text-xs focus:ring-1 focus:ring-amber-500 bg-white"
                        >
                          <option value="">— Length —</option>
                          {logLengths.map((l) => (
                            <option key={l.lengthId} value={l.lengthId}>
                              {l.lengthFt} ft
                            </option>
                          ))}
                        </select>
                      </div>

                      {/* Girth Input */}
                      <div className="w-32">
                        <label className="block text-[11px] font-medium text-gray-600 mb-0.5">Girth (ft) *</label>
                        <input
                          type="number"
                          step="0.01"
                          min="0.01"
                          required
                          placeholder="e.g. 3.50"
                          value={row.girthFt}
                          onChange={(e) => updateLogRowField(idx, 'girthFt', e.target.value)}
                          className="w-full px-2.5 py-1.5 border border-gray-300 rounded-md text-xs focus:ring-1 focus:ring-amber-500"
                        />
                      </div>

                      {/* Remove Row Button */}
                      <div className="pt-4">
                        <button
                          type="button"
                          disabled={deliveryForm.logRows.length <= 1}
                          onClick={() => removeLogRow(idx)}
                          className={`text-xs px-2.5 py-1.5 rounded transition-colors ${
                            deliveryForm.logRows.length <= 1
                              ? 'text-gray-300 cursor-not-allowed'
                              : 'text-rose-600 hover:bg-rose-50 border border-rose-200'
                          }`}
                          title={deliveryForm.logRows.length <= 1 ? 'At least one log is required' : 'Remove row'}
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <button
                type="submit"
                className="w-full bg-amber-700 hover:bg-amber-800 text-white font-medium py-2.5 rounded-lg text-sm transition-colors shadow-sm"
              >
                Record Delivery ({deliveryForm.logRows.length} {deliveryForm.logRows.length === 1 ? 'log' : 'logs'})
              </button>
            </form>
          </div>
        )}

        {/* TAB 4: Delivery History */}
        {activeTab === 'history' && (
          <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
            <div className="p-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
              <h2 className="font-semibold text-gray-800">Timber Delivery History</h2>
              <button onClick={fetchDeliveries} className="text-xs bg-white border border-gray-300 px-3 py-1.5 rounded-md hover:bg-gray-50">
                Refresh
              </button>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-gray-50 text-gray-600 border-b">
                  <tr>
                    <th className="py-3 px-4">Delivery #</th>
                    <th className="py-3 px-4">Date</th>
                    <th className="py-3 px-4">Supplier</th>
                    <th className="py-3 px-4">Log Count</th>
                    <th className="py-3 px-4">Vehicle #</th>
                    <th className="py-3 px-4">Notes</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {deliveries.length === 0 ? (
                    <tr>
                      <td colSpan="6" className="py-6 text-center text-gray-400">No delivery records found.</td>
                    </tr>
                  ) : (
                    deliveries.map((del) => (
                      <tr key={del.deliveryId} className="hover:bg-gray-50">
                        <td className="py-3 px-4 font-mono text-xs font-bold text-gray-700">#{del.deliveryId}</td>
                        <td className="py-3 px-4 text-gray-500">{new Date(del.receivedAt).toLocaleDateString()}</td>
                        <td className="py-3 px-4 font-medium text-gray-800">
                          {del.supplierName || `Supplier #${del.supplierId}`}
                        </td>
                        <td className="py-3 px-4 font-semibold text-gray-700">{del.logCount ?? '—'}</td>
                        <td className="py-3 px-4 text-gray-500">{del.vehicleNumber || 'N/A'}</td>
                        <td className="py-3 px-4 text-gray-500 text-xs truncate max-w-xs">{del.notes || '—'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* TAB 5: Suppliers Management */}
        {activeTab === 'suppliers' && (
          <div className="space-y-6">
            {/* Add New Supplier Form */}
            {isAuthorizedIntake && (
              <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200 max-w-3xl">
                <h2 className="text-lg font-semibold text-gray-800 mb-1">Add New Supplier</h2>
                <p className="text-xs text-gray-500 mb-4">Register a new timber supplier to enable logging their deliveries</p>
                <form onSubmit={handleCreateSupplierSubmit} className="space-y-4">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-medium text-gray-700 mb-1">Supplier Name *</label>
                      <input
                        type="text"
                        required
                        maxLength={100}
                        placeholder="e.g. Apex Foresters Ltd"
                        value={supplierForm.name}
                        onChange={(e) => setSupplierForm({ ...supplierForm, name: e.target.value })}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-700 mb-1">Phone Number *</label>
                      <input
                        type="text"
                        required
                        maxLength={20}
                        placeholder="e.g. +94771234567"
                        value={supplierForm.phoneNumber}
                        onChange={(e) => setSupplierForm({ ...supplierForm, phoneNumber: e.target.value })}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                      />
                    </div>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">Address (optional)</label>
                    <input
                      type="text"
                      maxLength={255}
                      placeholder="e.g. 45 Sawmill Way, Ratnapura"
                      value={supplierForm.address}
                      onChange={(e) => setSupplierForm({ ...supplierForm, address: e.target.value })}
                      className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                    />
                  </div>
                  <button
                    type="submit"
                    className="px-4 py-2 bg-amber-700 hover:bg-amber-800 text-white rounded-lg text-sm font-medium transition-colors"
                  >
                    Add Supplier
                  </button>
                </form>
              </div>
            )}

            {/* Suppliers List Table */}
            <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
              <div className="p-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
                <h2 className="font-semibold text-gray-800">All Suppliers</h2>
                <button onClick={fetchSuppliers} className="text-xs bg-white border border-gray-300 px-3 py-1.5 rounded-md hover:bg-gray-50">
                  Refresh
                </button>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-gray-50 text-gray-600 border-b">
                    <tr>
                      <th className="py-3 px-4">Supplier ID</th>
                      <th className="py-3 px-4">Name</th>
                      <th className="py-3 px-4">Phone Number</th>
                      <th className="py-3 px-4">Address</th>
                      <th className="py-3 px-4">Status</th>
                      {isAdmin && <th className="py-3 px-4 text-right">Actions</th>}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {allSuppliers.length === 0 ? (
                      <tr>
                        <td colSpan={isAdmin ? 6 : 5} className="py-6 text-center text-gray-400">
                          {suppliersLoading ? 'Loading suppliers...' : 'No suppliers registered.'}
                        </td>
                      </tr>
                    ) : (
                      allSuppliers.map((s) => (
                        <tr key={s.supplierId} className="hover:bg-gray-50">
                          <td className="py-3 px-4 font-mono text-xs text-gray-500">#{s.supplierId}</td>
                          <td className="py-3 px-4 font-semibold text-gray-800">{s.supplierName}</td>
                          <td className="py-3 px-4 text-gray-600">{s.contactNumber || '—'}</td>
                          <td className="py-3 px-4 text-gray-600">{s.address || '—'}</td>
                          <td className="py-3 px-4">
                            {s.isActive ? (
                              <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-emerald-100 text-emerald-800 border border-emerald-200">
                                Active
                              </span>
                            ) : (
                              <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-gray-100 text-gray-600 border border-gray-200">
                                Deactivated
                              </span>
                            )}
                          </td>
                          {isAdmin && (
                            <td className="py-3 px-4 text-right">
                              {s.isActive ? (
                                <button
                                  onClick={() => setDeactivateSupplierModal({
                                    isOpen: true,
                                    supplierId: s.supplierId,
                                    supplierName: s.supplierName
                                  })}
                                  className="text-xs px-2.5 py-1 text-rose-600 hover:text-rose-700 hover:bg-rose-50 border border-rose-200 rounded font-medium transition-colors"
                                >
                                  Deactivate
                                </button>
                              ) : (
                                <span className="text-xs text-gray-400 italic">Inactive</span>
                              )}
                            </td>
                          )}
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}

        {/* MODAL: Soft Delete Log (Admin-Only with Required Reason) */}
        {removeLogModal.isOpen && (
          <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
            <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
              <h3 className="text-lg font-bold text-gray-800">Remove Log from Inventory</h3>
              <p className="text-sm text-gray-600">
                This will mark <span className="font-semibold text-gray-800">Log #{removeLogModal.logId}</span> as removed. Are you sure?
              </p>
              <form onSubmit={handleRemoveLogSubmit} className="space-y-4">
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Reason for Removal *</label>
                  <textarea
                    required
                    maxLength={255}
                    rows="3"
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-rose-500"
                    placeholder="e.g. Damaged due to rot, splintered during transit, audit mismatch"
                    value={removeLogModal.reason}
                    onChange={(e) => setRemoveLogModal({ ...removeLogModal, reason: e.target.value })}
                  />
                </div>
                <div className="flex justify-end gap-2 pt-2">
                  <button
                    type="button"
                    onClick={() => setRemoveLogModal({ isOpen: false, logId: null, reason: '' })}
                    className="px-4 py-2 border rounded-lg text-sm text-gray-600 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="px-4 py-2 bg-rose-600 hover:bg-rose-700 text-white rounded-lg text-sm font-medium transition-colors"
                  >
                    Confirm Removal
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* MODAL: Deactivate Supplier Confirmation (Admin-Only) */}
        {deactivateSupplierModal.isOpen && (
          <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
            <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
              <h3 className="text-lg font-bold text-gray-800">Deactivate Supplier</h3>
              <p className="text-sm text-gray-600">
                Are you sure you want to deactivate <span className="font-semibold text-gray-800">{deactivateSupplierModal.supplierName}</span>?
                Historical deliveries linked to this supplier will be preserved, but new deliveries cannot select this supplier.
              </p>
              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setDeactivateSupplierModal({ isOpen: false, supplierId: null, supplierName: '' })}
                  className="px-4 py-2 border rounded-lg text-sm text-gray-600 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleDeactivateSupplierConfirm}
                  className="px-4 py-2 bg-rose-600 hover:bg-rose-700 text-white rounded-lg text-sm font-medium transition-colors"
                >
                  Confirm Deactivation
                </button>
              </div>
            </div>
          </div>
        )}

        {/* MODAL: Manual Stock Adjustment (legacy modal) */}
        {adjustModal.isOpen && (
          <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
            <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
              <h3 className="text-lg font-bold text-gray-800">Adjust Stock: {adjustModal.species} — {adjustModal.grade}</h3>
              <form onSubmit={handleAdjustSubmit} className="space-y-3">
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Grade</label>
                  <input
                    type="text"
                    readOnly
                    className="w-full px-3 py-2 border border-gray-200 bg-gray-50 rounded-lg text-sm text-gray-600 cursor-not-allowed"
                    value={adjustModal.grade}
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Volume Adjustment (m³) *</label>
                  <input
                    type="number"
                    step="0.01"
                    required
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                    placeholder="e.g. -2.5 or 5.0"
                    value={adjustModal.volumeDelta}
                    onChange={(e) => setAdjustModal({ ...adjustModal, volumeDelta: e.target.value })}
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Reason for Adjustment *</label>
                  <textarea
                    required
                    rows="2"
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                    placeholder="e.g. Damaged during unloading, recount audit"
                    value={adjustModal.reason}
                    onChange={(e) => setAdjustModal({ ...adjustModal, reason: e.target.value })}
                  />
                </div>

                <div className="flex justify-end gap-2 pt-2">
                  <button
                    type="button"
                    onClick={() => setAdjustModal({ isOpen: false, species: '', grade: '', volumeDelta: '', reason: '' })}
                    className="px-4 py-2 border rounded-lg text-sm text-gray-600 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="px-4 py-2 bg-amber-700 hover:bg-amber-800 text-white rounded-lg text-sm font-medium"
                  >
                    Save Adjustment
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}
      </div>
    </Layout>
  );
}