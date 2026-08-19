import { Link } from 'react-router-dom';

export default function UserManagementPage() {
  return (
    <div style={{ padding: 40, fontFamily: 'sans-serif' }}>
      <Link to="/">&larr; Back to Dashboard</Link>
      <h2>User Management</h2>
      <p style={{ color: '#888' }}>[Placeholder — full Add/Edit/Deactivate User screens coming in Commit 3-4]</p>
    </div>
  );
}