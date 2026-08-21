import { useAuth } from '../context/AuthContext';
import { Link } from 'react-router-dom';

export default function LogIntakePage() {
  const { user } = useAuth();

  return (
    <div style={{ padding: 40, fontFamily: 'sans-serif' }}>
      <Link to="/">&larr; Back to Dashboard</Link>
      <h2>Log Intake</h2>
      <p>Your role: {user.role}</p>
      <p style={{ color: '#888' }}>
        [Placeholder — this page belongs to the LogIntakeService team.
        Access scope for your role: {
          user.role === 'Supervisor' ? 'Create-only (Record Delivery form)' :
          'Full access (Create, stock view, history view)'
        }]
      </p>
    </div>
  );
}