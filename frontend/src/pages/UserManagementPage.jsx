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

  // Edit User state
  const [editingUserId, setEditingUserId] = useState(null);
  const [editRole, setEditRole] = useState('');
  const [editPassword, setEditPassword] = useState('');
  const [editError, setEditError] = useState('');
  const [editSubmitting, setEditSubmitting] = useState(false);

  // Delete state
  const [deletingUserId, setDeletingUserId] = useState(null);
  const [deleteError, setDeleteError] = useState('');

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
      fetchUsers();
    } catch {
      setAddError('Could not reach the server.');
    } finally {
      setSubmitting(false);
    }
  }

  function startEdit(u) {
    setEditingUserId(u.userId);
    setEditRole(u.role);
    setEditPassword('');
    setEditError('');
  }

  function cancelEdit() {
    setEditingUserId(null);
    setEditRole('');
    setEditPassword('');
    setEditError('');
  }

  async function handleSaveEdit(userId) {
    setEditError('');
    setEditSubmitting(true);

    const body = {};
    if (editRole) body.role = editRole;
    if (editPassword) body.password = editPassword;

    try {
      const res = await fetch(`${API_URL}/auth/users/${userId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${user.token}`,
        },
        body: JSON.stringify(body),
      });

      const data = await res.json();

      if (!res.ok) {
        setEditError(data.message || 'Failed to update user.');
        return;
      }

      cancelEdit();
      fetchUsers();
    } catch {
      setEditError('Could not reach the server.');
    } finally {
      setEditSubmitting(false);
    }
  }

  async function handleDelete(userId, username) {
    const confirmed = window.confirm(
      `Are you sure you want to deactivate (permanently remove) "${username}"? This cannot be undone.`
    );
    if (!confirmed) return;

    setDeleteError('');
    setDeletingUserId(userId);

    try {
      const res = await fetch(`${API_URL}/auth/users/${userId}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${user.token}` },
      });

      const data = await res.json();

      if (!res.ok) {
        setDeleteError(data.message || 'Failed to delete user.');
        return;
      }

      fetchUsers();
    } catch {
      setDeleteError('Could not reach the server.');
    } finally {
      setDeletingUserId(null);
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
        {deleteError && <p style={{ color: 'red' }}>{deleteError}</p>}
        {loadingUsers ? (
          <p>Loading...</p>
        ) : (
          <table border="1" cellPadding="8" style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th>User ID</th>
                <th>Username</th>
                <th>Role</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.userId}>
                  <td>{u.userId}</td>
                  <td>{u.username}</td>
                  <td>
                    {editingUserId === u.userId ? (
                      <select value={editRole} onChange={(e) => setEditRole(e.target.value)}>
                        <option value="Supervisor">Supervisor</option>
                        <option value="Manager">Manager</option>
                        <option value="Admin">Admin</option>
                      </select>
                    ) : (
                      u.role
                    )}
                  </td>
                  <td>
                    {editingUserId === u.userId ? (
                      <div>
                        <input
                          type="password"
                          placeholder="New password (optional)"
                          value={editPassword}
                          onChange={(e) => setEditPassword(e.target.value)}
                          style={{ marginBottom: 4, padding: 4, width: '100%' }}
                        />
                        {editError && <p style={{ color: 'red', margin: '4px 0' }}>{editError}</p>}
                        <button
                          onClick={() => handleSaveEdit(u.userId)}
                          disabled={editSubmitting}
                          style={{ marginRight: 6 }}
                        >
                          {editSubmitting ? 'Saving...' : 'Save'}
                        </button>
                        <button onClick={cancelEdit}>Cancel</button>
                      </div>
                    ) : (
                      <>
                        <button onClick={() => startEdit(u)} style={{ marginRight: 6 }}>
                          Edit
                        </button>
                        <button
                          onClick={() => handleDelete(u.userId, u.username)}
                          disabled={deletingUserId === u.userId}
                          style={{ color: 'red' }}
                        >
                          {deletingUserId === u.userId ? 'Deleting...' : 'Deactivate'}
                        </button>
                      </>
                    )}
                  </td>
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