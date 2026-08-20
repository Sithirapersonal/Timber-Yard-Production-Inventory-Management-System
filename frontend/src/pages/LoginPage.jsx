import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Button from '../components/ui/Button';

const API_URL = import.meta.env.VITE_API_URL;

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const { login } = useAuth();
  const navigate = useNavigate();

  async function handleSubmit(e) {
    e.preventDefault();
    setError('');
    setSubmitting(true);

    try {
      const response = await fetch(`${API_URL}/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      });

      const data = await response.json();

      if (!response.ok) {
        setError(data.message || 'Login failed. Please try again.');
        return;
      }

      login(data.token);
      navigate('/');
    } catch {
      setError('Could not reach the server. Is the backend running?');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="min-h-screen bg-charcoal flex items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <div className="inline-block border-2 border-heartwood text-heartwood font-mono text-xs font-bold tracking-widest px-3 py-1 rounded-sm -rotate-2 mb-4">
            TIMBER YARD
          </div>
          <h1 className="font-display text-3xl font-semibold text-white">Staff Login</h1>
          <p className="text-white/50 text-sm mt-1">Production &amp; Inventory Management</p>
        </div>

        <form onSubmit={handleSubmit} className="bg-sawdust rounded-lg shadow-xl p-8">
          <div className="mb-4">
            <label htmlFor="username" className="block text-sm font-medium text-charcoal mb-1">
              Username
            </label>
            <input
              id="username"
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              required
              autoFocus
              className="w-full px-3 py-2 rounded-md border border-charcoal/20 bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/50 focus:border-heartwood"
            />
          </div>
          <div className="mb-6">
            <label htmlFor="password" className="block text-sm font-medium text-charcoal mb-1">
              Password
            </label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              className="w-full px-3 py-2 rounded-md border border-charcoal/20 bg-white focus:outline-none focus:ring-2 focus:ring-heartwood/50 focus:border-heartwood"
            />
          </div>

          {error && (
            <p className="text-rust text-sm mb-4 bg-rust/10 border border-rust/20 rounded-md px-3 py-2">
              {error}
            </p>
          )}

          <Button type="submit" disabled={submitting} className="w-full">
            {submitting ? 'Logging in…' : 'Log In'}
          </Button>
        </form>
      </div>
    </div>
  );
}