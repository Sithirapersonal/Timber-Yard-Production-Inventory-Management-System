import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Layout from '../components/Layout';
import Card from '../components/ui/Card';
import Button from '../components/ui/Button';
import RoleBadge from '../components/ui/RoleBadge';

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
    <Layout>
      <Link to="/" className="text-sm text-fog hover:text-heartwood transition-colors">
        &larr; Back to Dashboard
      </Link>
      <h1 className="font-display text-3xl font-semibold text-charcoal mt-2 mb-8">User Management</h1>

      {error && (
        <p className="text-rust text-sm mb-4 bg-rust/10 border border-rust/20 rounded-md px-3 py-2">{error}</p>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        {/* LEFT COLUMN: Add User + Directory */}
        <div className="space-y-6">
          <Card className="p-6">
            <h2 className="font-display text-lg font-semibold text-charcoal mb-4">Add New Staff Account</h2>
            <form onSubmit={handleAddUser}>
              <div className="mb-4">
                <label className="block text-sm font-medium text-charcoal mb-1">Username</label>
                <input
                  type="text"
                  value={newUsername}
                  onChange={(e) => setNewUsername(e.target.value)}
                  required
                  className="w-full px-3 py-2 rounded-md border border-charcoal/20 bg-sawdust/50 focus:outline-none focus:ring-2 focus:ring-heartwood/50 focus:border-heartwood text-sm"
                />
              </div>
              <div className="mb-4">
                <label className="block text-sm font-medium text-charcoal mb-1">Password</label>
                <input
                  type="password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                  className="w-full px-3 py-2 rounded-md border border-charcoal/20 bg-sawdust/50 focus:outline-none focus:ring-2 focus:ring-heartwood/50 focus:border-heartwood text-sm"
                />
              </div>
              <div className="mb-4">
                <label className="block text-sm font-medium text-charcoal mb-1">Role</label>
                <select
                  value={newRole}
                  onChange={(e) => setNewRole(e.target.value)}
                  className="w-full px-3 py-2 rounded-md border border-charcoal/20 bg-sawdust/50 focus:outline-none focus:ring-2 focus:ring-heartwood/50 focus:border-heartwood text-sm"
                >
                  <option value="Supervisor">Supervisor</option>
                  <option value="Manager">Manager</option>
                  <option value="Admin">Admin</option>
                </select>
              </div>
              {addError && <p className="text-rust text-sm mb-3">{addError}</p>}
              {addSuccess && <p className="text-moss text-sm mb-3">{addSuccess}</p>}
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Creating…' : 'Add User'}
              </Button>
            </form>
          </Card>

          <Card className="p-6">
            <h2 className="font-display text-lg font-semibold text-charcoal mb-4">User Directory</h2>
            {deleteError && <p className="text-rust text-sm mb-3">{deleteError}</p>}
            {loadingUsers ? (
              <p className="text-fog text-sm">Loading…</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs uppercase tracking-wide text-fog border-b border-charcoal/10">
                      <th className="py-2 pr-3 font-mono">ID</th>
                      <th className="py-2 pr-3">Username</th>
                      <th className="py-2 pr-3">Role</th>
                      <th className="py-2">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {users.map((u) => (
                      <tr key={u.userId} className="border-b border-charcoal/5 last:border-0">
                        <td className="py-3 pr-3 font-mono text-fog">{u.userId}</td>
                        <td className="py-3 pr-3 font-medium">{u.username}</td>
                        <td className="py-3 pr-3">
                          {editingUserId === u.userId ? (
                            <select
                              value={editRole}
                              onChange={(e) => setEditRole(e.target.value)}
                              className="border border-charcoal/20 rounded px-2 py-1 text-sm"
                            >
                              <option value="Supervisor">Supervisor</option>
                              <option value="Manager">Manager</option>
                              <option value="Admin">Admin</option>
                            </select>
                          ) : (
                            <RoleBadge role={u.role} />
                          )}
                        </td>
                        <td className="py-3">
                          {editingUserId === u.userId ? (
                            <div className="space-y-2 min-w-[160px]">
                              <input
                                type="password"
                                placeholder="New password (optional)"
                                value={editPassword}
                                onChange={(e) => setEditPassword(e.target.value)}
                                className="w-full border border-charcoal/20 rounded px-2 py-1 text-sm"
                              />
                              {editError && <p className="text-rust text-xs">{editError}</p>}
                              <div className="flex gap-2">
                                <Button variant="primary" onClick={() => handleSaveEdit(u.userId)} disabled={editSubmitting}>
                                  {editSubmitting ? 'Saving…' : 'Save'}
                                </Button>
                                <Button variant="ghost" onClick={cancelEdit}>Cancel</Button>
                              </div>
                            </div>
                          ) : (
                            <div className="flex gap-2">
                              <Button variant="ghost" onClick={() => startEdit(u)}>Edit</Button>
                              <Button
                                variant="danger"
                                onClick={() => handleDelete(u.userId, u.username)}
                                disabled={deletingUserId === u.userId}
                              >
                                {deletingUserId === u.userId ? 'Deleting…' : 'Deactivate'}
                              </Button>
                            </div>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </div>

        {/* RIGHT COLUMN: Login / Audit History */}
        <Card className="p-6">
          <h2 className="font-display text-lg font-semibold text-charcoal mb-4">Login / Audit History</h2>
          {loadingHistory ? (
            <p className="text-fog text-sm">Loading…</p>
          ) : (
            <div className="overflow-x-auto max-h-[640px] overflow-y-auto">
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-white">
                  <tr className="text-left text-xs uppercase tracking-wide text-fog border-b border-charcoal/10">
                    <th className="py-2 pr-3">Username</th>
                    <th className="py-2 pr-3">Result</th>
                    <th className="py-2 pr-3">Attempted At</th>
                    <th className="py-2 font-mono">IP</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((h) => (
                    <tr key={h.loginHistoryId} className="border-b border-charcoal/5 last:border-0">
                      <td className="py-2.5 pr-3 font-medium">{h.username}</td>
                      <td className="py-2.5 pr-3">
                        <span className={`text-xs font-semibold ${h.success ? 'text-moss' : 'text-rust'}`}>
                          {h.success ? 'Success' : 'Failed'}
                        </span>
                      </td>
                      <td className="py-2.5 pr-3 text-fog text-xs">
                        {new Date(h.attemptedAt).toLocaleString()}
                      </td>
                      <td className="py-2.5 font-mono text-xs text-fog">{h.ipAddress || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </div>
    </Layout>
  );
}