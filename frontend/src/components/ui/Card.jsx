export default function Card({ children, className = '' }) {
  return (
    <div className={`bg-white border border-charcoal/10 rounded-lg shadow-sm ${className}`}>
      {children}
    </div>
  );
}