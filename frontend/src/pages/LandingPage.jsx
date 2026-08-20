import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Layout from '../components/Layout';
import Card from '../components/ui/Card';

export default function LandingPage() {
  const { user } = useAuth();

  const isAdmin = user.role === 'Admin';

  return (
    <Layout>
      <h1 className="font-display text-3xl font-semibold text-charcoal mb-1">Dashboard</h1>
      <p className="text-fog mb-8">Welcome back, {user.username}.</p>

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