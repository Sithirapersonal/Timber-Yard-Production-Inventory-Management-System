import { Routes, Route } from 'react-router-dom';
import LoginPage from './pages/LoginPage';
import LandingPage from './pages/LandingPage';
import LogIntakePage from './pages/LogIntakePage';
import SawingPage from './pages/SawingPage';
import TreatmentPage from './pages/TreatmentPage';
import UserManagementPage from './pages/UserManagementPage';
import ProtectedRoute from './components/ProtectedRoute';

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <LandingPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/log-intake"
        element={
          <ProtectedRoute>
            <LogIntakePage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/sawing"
        element={
          <ProtectedRoute>
            <SawingPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/treatment"
        element={
          <ProtectedRoute>
            <TreatmentPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/users"
        element={
          <ProtectedRoute allowedRoles={['Admin']}>
            <UserManagementPage />
          </ProtectedRoute>
        }
      />
    </Routes>
  );
}

export default App;