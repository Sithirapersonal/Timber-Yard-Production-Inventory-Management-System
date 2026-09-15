import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';

const API_BASE_URL = import.meta.env.VITE_LOG_INTAKE_API_URL || 'http://localhost:5000/api/LogIntake';

export default function LogIntakePage() {
  const { user } = useAuth();
  const [activeTab, setActiveTab] = useState('stock'); // 'stock', 'intake', 'history'
  
  // Data States
  const [stocks, setStocks] = useState([]);
  const [deliveries, setDeliveries] = useState([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState({ type: '', text: '' });

  // Delivery Form State (SCRUM-8)
  const [deliveryForm, setDeliveryForm] = useState({
    supplierName: '',
    species: 'Teak',
    logCount: 1,
    totalVolume: '',
    truckNumber: '',
    notes: ''
  });

  // Adjustment Modal State (SCRUM-10)
  const [adjustModal, setAdjustModal] = useState({
    isOpen: false,
    species: '',
    quantityDelta: 0,
    reason: ''
  });

  const authHeader = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${user?.token || sessionStorage.getItem('token')}`
  };

  useEffect(() => {
    fetchStock();
    fetchDeliveries();
  }, []);

  const showNotification = (type, text) => {
    setMessage({ type, text });
    setTimeout(() => setMessage({ type: '', text: '' }), 4000);
  };

  // 1. Fetch Raw Stock (SCRUM-9 & SCRUM-11)
  const fetchStock = async () => {
    setLoading(true);
    try {
      const res = await fetch(`${API_BASE_URL}/stock`, { headers: authHeader });
      if (res.ok) {
        const data = await res.json();
        setStocks(data);
      }
    } catch (err) {
      console.error('Failed to fetch stock', err);
    } finally {
      setLoading(false);
    }
  };

  // 2. Fetch Deliveries (SCRUM-9)
  const fetchDeliveries = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/deliveries`, { headers: authHeader });
      if (res.ok) {
        const data = await res.json();
        setDeliveries(data);
      }
    } catch (err) {
      console.error('Failed to fetch deliveries', err);
    }
  };

  // 3. Record Delivery Submit (SCRUM-8)
  const handleDeliverySubmit = async (e) => {
    e.preventDefault();
    try {
      const res = await fetch(`${API_BASE_URL}/deliveries`, {
        method: 'POST',
        headers: authHeader,
        body: JSON.stringify({
          supplierName: deliveryForm.supplierName,
          species: deliveryForm.species,
          logCount: parseInt(deliveryForm.logCount),
          totalVolume: parseFloat(deliveryForm.totalVolume),
          truckNumber: deliveryForm.truckNumber,
          notes: deliveryForm.notes
        })
      });

      if (res.ok) {
        showNotification('success', 'Raw Timber Delivery recorded successfully!');
        setDeliveryForm({
          supplierName: '',
          species: 'Teak',
          logCount: 1,
          totalVolume: '',
          truckNumber: '',
          notes: ''
        });
        fetchStock();
        fetchDeliveries();
        setActiveTab('stock');
      } else {
        showNotification('error', 'Failed to record delivery. Ensure required fields are valid.');
      }
    } catch (err) {
      showNotification('error', 'Network error connecting to LogIntakeService.');
    }
  };

  // 4. Stock Adjustment Submit (SCRUM-10)
  const handleAdjustSubmit = async (e) => {
    e.preventDefault();
    try {
      const res = await fetch(`${API_BASE_URL}/stock/adjust`, {
        method: 'POST',
        headers: authHeader,
        body: JSON.stringify({
          species: adjustModal.species,
          quantityDelta: parseInt(adjustModal.quantityDelta),
          reason: adjustModal.reason
        })
      });

      if (res.ok) {
        showNotification('success', `Stock adjusted successfully for ${adjustModal.species}!`);
        setAdjustModal({ isOpen: false, species: '', quantityDelta: 0, reason: '' });
        fetchStock();
      } else {
        showNotification('error', 'Failed to adjust stock. Check permissions.');
      }
    } catch (err) {
      showNotification('error', 'Network error adjusting stock.');
    }
  };

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex justify-between items-center border-b pb-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-800">Log Intake & Raw Timber Management</h1>
          <p className="text-sm text-gray-500">Manage deliveries, track live inventory, and monitor low-stock thresholds</p>
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
      <div className="flex gap-4 border-b border-gray-200">
        <button
          onClick={() => setActiveTab('stock')}
          className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
            activeTab === 'stock' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Raw Stock Overview
        </button>
        <button
          onClick={() => setActiveTab('intake')}
          className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
            activeTab === 'intake' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          + Record Delivery (Intake)
        </button>
        <button
          onClick={() => setActiveTab('history')}
          className={`py-2 px-4 text-sm font-medium border-b-2 transition-colors ${
            activeTab === 'history' ? 'border-amber-600 text-amber-600 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Delivery History
        </button>
      </div>

      {/* TAB 1: Live Stock View & Thresholds (SCRUM-9 & SCRUM-11) */}
      {activeTab === 'stock' && (
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
          <div className="p-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
            <h2 className="font-semibold text-gray-800">Current Log Inventory</h2>
            <button onClick={fetchStock} className="text-xs bg-white border border-gray-300 px-3 py-1.5 rounded-md hover:bg-gray-50">
              Refresh
            </button>
          </div>
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-600 border-b">
              <tr>
                <th className="py-3 px-4">Species</th>
                <th className="py-3 px-4">Available Logs</th>
                <th className="py-3 px-4">Total Volume (m³)</th>
                <th className="py-3 px-4">Status & Alert</th>
                <th className="py-3 px-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {stocks.length === 0 ? (
                <tr>
                  <td colSpan="5" className="py-6 text-center text-gray-400">
                    {loading ? 'Loading stock...' : 'No timber stock available.'}
                  </td>
                </tr>
              ) : (
                stocks.map((item) => {
                  const isLow = item.currentCount <= (item.threshold || 10);
                  return (
                    <tr key={item.id || item.species} className="hover:bg-gray-50">
                      <td className="py-3 px-4 font-semibold text-gray-800">{item.species}</td>
                      <td className="py-3 px-4 font-bold">{item.currentCount} logs</td>
                      <td className="py-3 px-4 text-gray-600">{item.totalVolume} m³</td>
                      <td className="py-3 px-4">
                        {isLow ? (
                          <span className="px-2.5 py-1 text-xs font-semibold rounded-full bg-rose-100 text-rose-700 border border-rose-200 animate-pulse">
                            ⚠️ Low Stock (≤ {item.threshold || 10})
                          </span>
                        ) : (
                          <span className="px-2.5 py-1 text-xs font-semibold rounded-full bg-emerald-100 text-emerald-700">
                            Adequate Stock
                          </span>
                        )}
                      </td>
                      <td className="py-3 px-4 text-right">
                        <button
                          onClick={() => setAdjustModal({ isOpen: true, species: item.species, quantityDelta: 0, reason: '' })}
                          className="text-xs bg-amber-50 text-amber-700 border border-amber-200 px-3 py-1 rounded hover:bg-amber-100"
                        >
                          Manual Adjust
                        </button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* TAB 2: Record Delivery Form (SCRUM-8) */}
      {activeTab === 'intake' && (
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200 max-w-2xl">
          <h2 className="text-lg font-semibold text-gray-800 mb-4">Record New Timber Delivery</h2>
          <form onSubmit={handleDeliverySubmit} className="space-y-4">
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Supplier Name *</label>
              <input
                type="text"
                required
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                placeholder="e.g. Silva Timber Plantation"
                value={deliveryForm.supplierName}
                onChange={(e) => setDeliveryForm({ ...deliveryForm, supplierName: e.target.value })}
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Timber Species *</label>
                <select
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                  value={deliveryForm.species}
                  onChange={(e) => setDeliveryForm({ ...deliveryForm, species: e.target.value })}
                >
                  <option value="Teak">Teak</option>
                  <option value="Mahogany">Mahogany</option>
                  <option value="Jak">Jak</option>
                  <option value="Pine">Pine</option>
                </select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Log Count *</label>
                <input
                  type="number"
                  min="1"
                  required
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                  value={deliveryForm.logCount}
                  onChange={(e) => setDeliveryForm({ ...deliveryForm, logCount: e.target.value })}
                />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Total Volume (m³) *</label>
                <input
                  type="number"
                  step="0.01"
                  required
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                  placeholder="e.g. 15.5"
                  value={deliveryForm.totalVolume}
                  onChange={(e) => setDeliveryForm({ ...deliveryForm, totalVolume: e.target.value })}
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Truck / Vehicle Plate</label>
                <input
                  type="text"
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                  placeholder="e.g. WP-CA-4521"
                  value={deliveryForm.truckNumber}
                  onChange={(e) => setDeliveryForm({ ...deliveryForm, truckNumber: e.target.value })}
                />
              </div>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Inspection Notes / Batch Info</label>
              <textarea
                rows="2"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-amber-500"
                placeholder="Grade A quality, verified moisture content..."
                value={deliveryForm.notes}
                onChange={(e) => setDeliveryForm({ ...deliveryForm, notes: e.target.value })}
              />
            </div>
            <button
              type="submit"
              className="w-full bg-amber-700 hover:bg-amber-800 text-white font-medium py-2.5 rounded-lg text-sm transition-colors shadow-sm"
            >
              Record Delivery
            </button>
          </form>
        </div>
      )}

      {/* TAB 3: Delivery History (SCRUM-9) */}
      {activeTab === 'history' && (
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-600 border-b">
              <tr>
                <th className="py-3 px-4">Date</th>
                <th className="py-3 px-4">Supplier</th>
                <th className="py-3 px-4">Species</th>
                <th className="py-3 px-4">Logs</th>
                <th className="py-3 px-4">Volume (m³)</th>
                <th className="py-3 px-4">Truck #</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {deliveries.length === 0 ? (
                <tr>
                  <td colSpan="6" className="py-6 text-center text-gray-400">No delivery records found.</td>
                </tr>
              ) : (
                deliveries.map((del) => (
                  <tr key={del.id} className="hover:bg-gray-50">
                    <td className="py-3 px-4 text-gray-500">{new Date(del.createdAt || Date.now()).toLocaleDateString()}</td>
                    <td className="py-3 px-4 font-medium text-gray-800">{del.supplierName}</td>
                    <td className="py-3 px-4">{del.species}</td>
                    <td className="py-3 px-4 font-semibold">{del.logCount}</td>
                    <td className="py-3 px-4 text-gray-600">{del.totalVolume} m³</td>
                    <td className="py-3 px-4 text-gray-500">{del.truckNumber || 'N/A'}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* MODAL: Manual Stock Adjustment (SCRUM-10) */}
      {adjustModal.isOpen && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl space-y-4">
            <h3 className="text-lg font-bold text-gray-800">Adjust Stock: {adjustModal.species}</h3>
            <form onSubmit={handleAdjustSubmit} className="space-y-3">
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Log Count Adjustment (+ / -) *</label>
                <input
                  type="number"
                  required
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                  placeholder="e.g. -2 for damage, 5 for recount"
                  value={adjustModal.quantityDelta}
                  onChange={(e) => setAdjustModal({ ...adjustModal, quantityDelta: e.target.value })}
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
                  onClick={() => setAdjustModal({ isOpen: false, species: '', quantityDelta: 0, reason: '' })}
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
  );
}