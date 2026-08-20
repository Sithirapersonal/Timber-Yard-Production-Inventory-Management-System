const ROLE_CONFIG = {
  Admin: { label: 'ADM', color: 'text-rust border-rust' },
  Manager: { label: 'MGR', color: 'text-heartwood border-heartwood' },
  Supervisor: { label: 'SUP', color: 'text-moss border-moss' },
};

export default function RoleBadge({ role }) {
  const config = ROLE_CONFIG[role] || { label: role?.slice(0, 3).toUpperCase(), color: 'text-fog border-fog' };

  return (
    <span
      className={`inline-block font-mono text-xs font-bold tracking-widest border-2 px-2 py-0.5 rounded-sm -rotate-2 ${config.color}`}
    >
      {config.label}
    </span>
  );
}