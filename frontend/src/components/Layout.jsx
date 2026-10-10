import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import RoleBadge from './ui/RoleBadge';

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard' },
  { to: '/log-intake', label: 'Log Intake' },
  { to: '/sawing', label: 'Sawing Process' },
  { to: '/treatment', label: 'Treatment Process' },
];

export default function Layout({ children }) {
  const { user, logout } = useAuth();
  const location = useLocation();

  return (
    <div className="min-h-screen flex bg-sawdust">
      {/* Sidebar */}
      <aside className="w-32 bg-charcoal text-white flex flex-col shrink-0">
        <div className="px-2.5 py-4 border-b border-white/10 text-center">
          <div className="inline-block border-2 border-heartwood text-heartwood font-mono text-[10px] font-bold tracking-wider px-2 py-0.5 rounded-sm -rotate-2 whitespace-nowrap">
            TIMBER YARD
          </div>
        </div>

        <nav className="flex-1 px-2 py-3 space-y-1">
          {NAV_ITEMS.map((item) => {
            const active = location.pathname === item.to;
            return (
              <Link
                key={item.to}
                to={item.to}
                className={`block px-2.5 py-2 rounded-md text-xs font-medium leading-snug transition-colors ${
                  active
                    ? 'bg-heartwood text-white'
                    : 'text-white/70 hover:bg-white/10 hover:text-white'
                }`}
              >
                {item.label}
              </Link>
            );
          })}

          {user.role === 'Admin' && (
            <Link
              to="/users"
              className={`block px-2.5 py-2 rounded-md text-xs font-medium leading-snug transition-colors ${
                location.pathname === '/users'
                  ? 'bg-heartwood text-white'
                  : 'text-white/70 hover:bg-white/10 hover:text-white'
              }`}
            >
              User Management
            </Link>
          )}
        </nav>
      </aside>

      {/* Main area */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Topbar */}
        <header className="h-16 bg-white border-b border-charcoal/10 flex items-center justify-between px-6 shrink-0">
          <div />
          <div className="flex items-center gap-3">
            <RoleBadge role={user.role} />
            <span className="text-sm font-medium text-charcoal">{user.username}</span>
            <button
              onClick={logout}
              className="text-sm text-fog hover:text-rust transition-colors ml-2"
            >
              Log Out
            </button>
          </div>
        </header>

        {/* Page content */}
        <main className="flex-1 p-8 overflow-auto">{children}</main>
      </div>
    </div>
  );
}