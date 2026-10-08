import { Link } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { fetchWithAuth } from '../utils/api';
import Layout from '../components/Layout';
import Card from '../components/ui/Card';

const API_BASE_URL = import.meta.env.VITE_TREATMENT_API_URL || 'http://localhost:5000/api';

export default function LandingPage() {
  const { user } = useAuth();

  const isAdmin = user.role === 'Admin';
  const canSeeAlerts = ['Admin', 'Manager', 'Supervisor'].includes(user?.role);
  const [alerts, setAlerts] = useState([]);

  useEffect(() => {
    if (!canSeeAlerts) return;
    (async () => {
      try {
        const res = await fetchWithAuth(`${API_BASE_URL}/TreatmentStock/treated/alerts`);
        if (res.ok) setAlerts(await res.json());
      } catch (err) {
        if (err.message !== 'SESSION_EXPIRED') console.error('Failed to fetch treated stock alerts', err);
      }
    })();
  }, [canSeeAlerts]);

  return (
    <Layout>
      <h1 className="font-display text-3xl font-semibold text-charcoal mb-1">Dashboard</h1>
      <p className="text-fog mb-8">Welcome back, {user.username}.</p>

      {canSeeAlerts && alerts.length > 0 && (
        <Card className="p-5 mb-8 border-rust/30">
          <h2 className="font-display text-lg font-semibold text-rust mb-2">Treated Stock Low-Level Alerts</h2>
          <ul className="space-y-1.5">
            {alerts.map((a, i) => (
              <li key={i} className="text-sm text-charcoal">
                • <span className="font-semibold">{a.species}</span> ({a.dimensions}, {a.chemicalType}) —{' '}
                <span className="font-mono">{Number(a.volumeM3).toFixed(4)} m³</span> remaining, min{' '}
                <span className="font-mono">{Number(a.thresholdM3).toFixed(4)} m³</span>.{' '}
                <Link to="/treatment" className="text-heartwood font-medium hover:underline">Open Treatment</Link>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        <NavCard to="/log-intake" title="Log Intake" desc="Record incoming timber deliveries" />
        <NavCard to="/sawing" title="Sawing Process" desc="Track raw logs through the sawmill" />
        <NavCard to="/treatment" title="Treatment Process" desc="Manage chemical treatment batches" />
        {isAdmin && (
          <NavCard to="/users" title="User Management" desc="Add, edit, and deactivate staff accounts" highlight />
        )}
      </div>
    </Layout>
  );
}

function NavCard({ to, title, desc, highlight }) {
  return (
    <Link to={to}>
      <Card className={`p-6 h-full hover:shadow-md transition-shadow ${highlight ? 'border-heartwood/40' : ''}`}>
        <h3 className="font-display text-lg font-semibold text-charcoal mb-1">{title}</h3>
        <p className="text-sm text-fog">{desc}</p>
      </Card>
    </Link>
  );
}