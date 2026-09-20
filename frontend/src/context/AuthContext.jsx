import { createContext, useContext, useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { jwtDecode } from 'jwt-decode';
import { registerSessionExpiredHandler } from '../utils/api';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  // Message shown on the login page after an automatic session expiry redirect.
  const [sessionExpiredMessage, setSessionExpiredMessage] = useState('');
  const navigate = useNavigate();

  // On app load, check if a token is already stored and still valid
  useEffect(() => {
    const token = sessionStorage.getItem('token');
    if (token) {
      try {
        const decoded = jwtDecode(token);
        const isExpired = decoded.exp * 1000 < Date.now();
        if (isExpired) {
          sessionStorage.removeItem('token');
        } else {
          setUser({
            username: decoded.sub,
            role: decoded.role,
            userId: decoded.userId,
            token,
          });
        }
      } catch {
        sessionStorage.removeItem('token');
      }
    }
    setLoading(false);
  }, []);

  // Use a ref so the handler closure always sees the latest navigate reference
  // without needing to re-register every render.
  const navigateRef = useRef(navigate);
  useEffect(() => {
    navigateRef.current = navigate;
  }, [navigate]);

  // Register the global 401 handler with the centralized api module once on mount.
  // When any fetchWithAuth call receives a 401, this fires automatically regardless
  // of which page or endpoint triggered it.
  useEffect(() => {
    registerSessionExpiredHandler(() => {
      // 1. Clear all stored auth data
      sessionStorage.removeItem('token');
      setUser(null);
      // 2. Set the human-readable message LoginPage will display
      setSessionExpiredMessage('Your session has expired. Please log in again.');
      // 3. Redirect to login immediately
      navigateRef.current('/login', { replace: true });
    });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  function login(token) {
    const decoded = jwtDecode(token);
    sessionStorage.setItem('token', token);
    setUser({
      username: decoded.sub,
      role: decoded.role,
      userId: decoded.userId,
      token,
    });
    // Clear any previous session-expired message when a fresh login succeeds
    setSessionExpiredMessage('');
  }

  function logout() {
    sessionStorage.removeItem('token');
    setUser(null);
    setSessionExpiredMessage('');
  }

  return (
    <AuthContext.Provider value={{ user, login, logout, loading, sessionExpiredMessage }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}