import { useState, useEffect, useCallback } from 'react';
import { useAuth } from '../context/AuthContext';
import { fetchWithAuth } from '../utils/api';
import Layout from '../components/Layout';
import Card from '../components/ui/Card';
import RoleBadge from '../components/ui/RoleBadge';
import StockOverviewTab from '../components/sawmill/StockOverviewTab';
import StartJobTab from '../components/sawmill/StartJobTab';

const API_BASE_URL = import.meta.env.VITE_SAWMILL_API_URL || 'http://localhost:5210/api/Sawmill';

export default function SawingPage() {
  const { user } = useAuth();
  const canWrite = ['Admin', 'Manager', 'Supervisor'].includes(user?.role);

  // ── Tab state ──────────────────────────────────────────────────────────────
  const [activeTab, setActiveTab] = useState('overview');

  // ── Shared data ───────────────────────────────────────────────────────────
  const [stockList, setStockList] = useState([]);
  const [stockLoading, setStockLoading] = useState(false);
  const [stockError, setStockError] = useState('');

  const [recentJobs, setRecentJobs] = useState([]);
  const [jobsLoading, setJobsLoading] = useState(false);
  const [jobsError, setJobsError] = useState('');

  // ── Inline notification banner (mirrors LogIntakePage pattern) ─────────────
  const [notification, setNotification] = useState({ type: '', text: '' });

  const showNotification = (type, text) => {
    setNotification({ type, text });
    setTimeout(() => setNotification({ type: '', text: '' }), 5000);
  };

  // ── Preselected stock from "Start Saw Job →" overview link ─────────────────
  const [preselectedStockId, setPreselectedStockId] = useState(null);

  // ─────────────────────────────────────────────────────────────────────────
  // Data fetch helpers
  // ─────────────────────────────────────────────────────────────────────────

  const fetchStock = useCallback(async () => {
    setStockLoading(true);
    setStockError('');
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/stock`);
      if (res.ok) {
        setStockList(await res.json());
      } else {
        const body = await res.json().catch(() => null);
        setStockError(body?.message || `Failed to load stock (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setStockError('Network error loading stock.');
    } finally {
      setStockLoading(false);
    }
  }, []);

  const fetchRecentJobs = useCallback(async () => {
    setJobsLoading(true);
    setJobsError('');
    try {
      const res = await fetchWithAuth(`${API_BASE_URL}/jobs`);
      if (res.ok) {
        setRecentJobs(await res.json());
      } else {
        const body = await res.json().catch(() => null);
        setJobsError(body?.message || `Failed to load recent jobs (${res.status}).`);
      }
    } catch (err) {
      if (err.message !== 'SESSION_EXPIRED') setJobsError('Network error loading recent jobs.');
    } finally {
      setJobsLoading(false);
    }
  }, []);

  // Initial load
  useEffect(() => {
    fetchStock();
    fetchRecentJobs();
  }, [fetchStock, fetchRecentJobs]);

  // ─────────────────────────────────────────────────────────────────────────
  // Handlers passed down to child tabs
  // ─────────────────────────────────────────────────────────────────────────

  const handleStartJobFromOverview = (stockId) => {
    setPreselectedStockId(stockId);
    setActiveTab('start');
  };

  const handleJobSubmitted = () => {
    showNotification('success', 'Saw job started successfully!');
    fetchStock();
    fetchRecentJobs();
    setPreselectedStockId(null);
    setActiveTab('overview');
  };

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Layout>
      <div className="space-y-6 max-w-7xl">

        {/* ── Page header ──────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-start justify-between gap-4 pb-4 border-b border-charcoal/10">
          <div>
            <h1 className="font-display text-3xl font-semibold text-charcoal">
              Sawing Process &amp; Stock Allocation
            </h1>
            <p className="text-sm text-fog mt-1">
              Start saw jobs, allocate raw logs, and track sawmill activity.
            </p>
          </div>
          <RoleBadge role={user?.role} />
        </div>

        {/* ── Inline notification banner ────────────────────────────────── */}
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

        {/* ── Tab navigation ────────────────────────────────────────────── */}
        <div className="flex flex-wrap gap-1 border-b border-charcoal/10">
          {[
            { id: 'overview', label: 'Stock Overview' },
            { id: 'start',    label: '+ Start Saw Job', hidden: !canWrite },
          ]
            .filter(t => !t.hidden)
            .map(tab => (
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

        {/* ── Tab panels ────────────────────────────────────────────────── */}
        {activeTab === 'overview' && (
          <StockOverviewTab
            stockList={stockList}
            stockLoading={stockLoading}
            stockError={stockError}
            onRefresh={fetchStock}
            recentJobs={recentJobs}
            jobsLoading={jobsLoading}
            jobsError={jobsError}
            onRefreshJobs={fetchRecentJobs}
            canWrite={canWrite}
            onStartJob={handleStartJobFromOverview}
          />
        )}

        {activeTab === 'start' && canWrite && (
          <StartJobTab
            stockList={stockList}
            preselectedStockId={preselectedStockId}
            apiBaseUrl={API_BASE_URL}
            onJobSubmitted={handleJobSubmitted}
            onNotification={showNotification}
          />
        )}

        {activeTab === 'start' && !canWrite && (
          <Card className="p-6">
            <p className="text-sm text-fog">
              Your role (<strong>{user?.role}</strong>) does not have permission to start saw jobs.
              Contact an Admin, Manager, or Supervisor.
            </p>
          </Card>
        )}

      </div>
    </Layout>
  );
}