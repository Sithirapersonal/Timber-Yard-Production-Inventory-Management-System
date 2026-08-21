import { createContext, useContext, useState, useEffect } from 'react';
import { jwtDecode } from 'jwt-decode';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

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

  function login(token) {
    const decoded = jwtDecode(token);
    sessionStorage.setItem('token', token);
    setUser({
      username: decoded.sub,
      role: decoded.role,
      userId: decoded.userId,
      token,
    });
  }

  function logout() {
    sessionStorage.removeItem('token');
    setUser(null);
  }

  return (
    <AuthContext.Provider value={{ user, login, logout, loading }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}