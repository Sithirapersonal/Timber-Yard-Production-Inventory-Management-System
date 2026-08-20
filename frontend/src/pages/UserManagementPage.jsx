import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

const API_URL = import.meta.env.VITE_API_URL;

export default function UserManagementPage() {
  const { user } = useAuth();
  const [users, setUsers] = useState([]);
  const [history, setHistory] = useState([]);
  const [loadingUsers, setLoadingUsers] = useState(true);
  const [loadingHistory, setLoadingHistory] = useState(true);
  const [error, setError] = useState('');

  // Add User form state
  const [newUsername, setNewUsername] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRole, setNewRole] = useState('Supervisor');
  const [addError, setAddError] = useState('');
  const [addSuccess, setAddSuccess] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function fetchUsers() {
    setLoadingUsers(true);
    try {
      const res = await fetch(`${API_URL}/auth/users`, {
        headers: { Authorization: `Bearer ${user.token}` },
      });
      if (!res.ok) throw new Error('Failed to load users');
      setUsers(await res.json());
    } catch (err) {
      setError(err.message);
    } finally {
      setLoadingUsers(false);
    }
  }

  async function fetchHistory() {
    setLoadingHistory(true);
    try {
      const res = await fetch(`${API_URL}/auth/login-history`, {
        headers: { Authorization: `Bearer ${user.token}` },
      });
      if (!res.ok) throw new Error('Failed to load login history');
      setHistory(await res.json());
    } catch (err) {
      setError(err.message);
    } finally {
      setLoadingHistory(false);
    }
  }

  useEffect(() => {
    fetchUsers();
    fetchHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleAddUser(e) {
    e.preventDefault();
    setAddError('');
    setAddSuccess('');
    setSubmitting(true);

    try {
      const res = await fetch(`${API_URL}/auth/users`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${user.token}`,
        },
        body: JSON.stringify({ username: newUsername, password: newPassword, role: newRole }),
      });

      const data = await res.json();

      if (!res.ok) {
        setAddError(data.message || 'Failed to create user.');
        return;
      }

      setAddSuccess(`User "${data.username}" created successfully.`);
      setNewUsername('');
      setNewPassword('');
      setNewRole('Supervisor');
      fetchUsers(); // refresh the directory
    } catch {
      setAddError('Could not reach the server.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={{ padding: 40, fontFamily: 'sans-serif' }}>
      <Link to="/">&larr; Back to Dashboard</Link>
      <h2>User Management</h2>

      {error && <p style={{ color: 'red' }}>{error}</p>}

      {/* Add User Form */}
      <section style={{ marginTop: 24, marginBottom: 40, maxWidth: 360 }}>
        <h3>Add New Staff Account</h3>
        <form onSubmit={handleAddUser}>
          <div style={{ marginBottom: 10 }}>
            <label>Username</label><br />
            <input
              type="text"
              value={newUsername}
              onChange={(e) => setNewUsername(e.target.value)}
              required
              style={{ width: '100%', padding: 6 }}
            />
          </div>
          <div style={{ marginBottom: 10 }}>
            <label>Password</label><br />
            <input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              required
              style={{ width: '100%', padding: 6 }}
            />
          </div>
          <div style={{ marginBottom: 10 }}>
            <label>Role</label><br />
            <select
              value={newRole}
              onChange={(e) => setNewRole(e.target.value)}
              style={{ width: '100%', padding: 6 }}
            >
              <option value="Supervisor">Supervisor</option>
              <option value="Manager">Manager</option>
              <option value="Admin">Admin</option>
            </select>
          </div>
          {addError && <p style={{ color: 'red' }}>{addError}</p>}
          {addSuccess && <p style={{ color: 'green' }}>{addSuccess}</p>}
          <button type="submit" disabled={submitting} style={{ padding: '6px 14px' }}>
            {submitting ? 'Creating...' : 'Add User'}
          </button>
        </form>
      </section>

      {/* User Directory */}
      <section style={{ marginBottom: 40 }}>
        <h3>User Directory</h3>
        {loadingUsers ? (
          <p>Loading...</p>
        ) : (
          <table border="1" cellPadding="8" style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th>User ID</th>
                <th>Username</th>
                <th>Role</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.userId}>
                  <td>{u.userId}</td>
                  <td>{u.username}</td>
                  <td>{u.role}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {/* Login History */}
      <section>
        <h3>Login / Audit History</h3>
        {loadingHistory ? (
          <p>Loading...</p>
        ) : (
          <table border="1" cellPadding="8" style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th>Username</th>
                <th>Result</th>
                <th>Attempted At</th>
                <th>IP Address</th>
              </tr>
            </thead>
            <tbody>
              {history.map((h) => (
                <tr key={h.loginHistoryId}>
                  <td>{h.username}</td>
                  <td style={{ color: h.success ? 'green' : 'red' }}>
                    {h.success ? 'Success' : 'Failed'}
                  </td>
                  <td>{new Date(h.attemptedAt).toLocaleString()}</td>
                  <td>{h.ipAddress || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}