import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

export default function LandingPage() {
  const { user, logout } = useAuth();

  const isAdmin = user.role === 'Admin';
  const isManager = user.role === 'Manager';
  const isSupervisor = user.role === 'Supervisor';

  return (
    <div style={{ padding: 40, fontFamily: 'sans-serif' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h2>Timber Yard — Dashboard</h2>
          <p>Welcome, {user.username} ({user.role})</p>
        </div>
        <button onClick={logout}>Log Out</button>
      </div>

      <div style={{ marginTop: 32, display: 'flex', gap: 16, flexWrap: 'wrap' }}>
        {/* Log Intake — every role sees this, scope differs by role inside the page itself */}
        <NavButton to="/log-intake" label="Log Intake" />

        {/* Sawing and Treatment — every role sees these */}
        <NavButton to="/sawing" label="Sawing Process" />
        <NavButton to="/treatment" label="Treatment Process" />

        {/* Admin-only */}
        {isAdmin && <NavButton to="/users" label="User Management" highlight />}
      </div>
    </div>
  );
}

function NavButton({ to, label, highlight }) {
  return (
    <Link
      to={to}
      style={{
        display: 'block',
        padding: '20px 32px',
        border: '1px solid #ccc',
        borderRadius: 8,
        textDecoration: 'none',
        color: '#222',
        backgroundColor: highlight ? '#eef' : '#fafafa',
        minWidth: 160,
        textAlign: 'center',
        fontWeight: 500,
      }}
    >
      {label}
    </Link>
  );
}