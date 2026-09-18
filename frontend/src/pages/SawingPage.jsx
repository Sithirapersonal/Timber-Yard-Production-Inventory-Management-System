import { useAuth } from '../context/AuthContext';
import { Link } from 'react-router-dom';
import Layout from '../components/Layout';

export default function SawingPage() {
  const { user } = useAuth();

  return (
    <Layout>
      <div className="max-w-4xl">
        <Link to="/" className="text-sm text-fog hover:text-charcoal mb-4 inline-block">&larr; Back to Dashboard</Link>
        <h1 className="font-display text-3xl font-semibold text-charcoal mb-2">Sawing Process</h1>
        <p className="text-sm text-fog mb-6">Your role: {user.role}</p>
        <div className="bg-white p-6 rounded-xl border border-black/10 shadow-sm text-fog text-sm">
          [Placeholder — this page belongs to the SawmillService team.
          Access scope for your role: {
            user.role === 'Admin' ? 'Full CRUD including Delete' : 'Create, Read, Update (no Delete)'
          }]
        </div>
      </div>
    </Layout>
  );
}