/**
 * Small colored pill for a SawJob's Status ('InProgress' | 'Completed' | 'Cancelled').
 * Shared between StockOverviewTab (not used there anymore) and StartJobTab's
 * "Recently Started Jobs" table.
 */
export default function SawJobStatusPill({ status }) {
  const config = {
    InProgress: 'bg-heartwood/15 text-heartwood',
    Completed: 'bg-moss/15 text-moss',
    Cancelled: 'bg-rust/15 text-rust',
  };
  return (
    <span className={`inline-block px-2.5 py-1 rounded-full text-sm font-medium ${config[status] || 'bg-fog/15 text-fog'}`}>
      {status}
    </span>
  );
}
