import { useAuth } from '../context/AuthContext';
import { Link } from 'react-router-dom';

export default function TreatmentPage() {
  const { user } = useAuth();

  return (
    <div style={{ padding: 40, fontFamily: 'sans-serif' }}>
      <Link to="/">&larr; Back to Dashboard</Link>
      <h2>Treatment Process</h2>
      <p>Your role: {user.role}</p>
      <p style={{ color: '#888' }}>
        [Placeholder — this page belongs to the TreatmentService team.
        Access scope for your role: {
          user.role === 'Admin' ? 'Full CRUD including Delete' : 'Create, Read, Update (no Delete)'
        }]
      </p>
    </div>
  );
}