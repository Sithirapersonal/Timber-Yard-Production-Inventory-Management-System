export default function Button({ children, variant = 'primary', ...props }) {
  const base = 'px-4 py-2 rounded-md font-medium text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed';

  const variants = {
    primary: 'bg-heartwood text-white hover:bg-heartwood-dark',
    danger: 'bg-transparent text-rust border border-rust/40 hover:bg-rust/10',
    ghost: 'bg-transparent text-charcoal border border-charcoal/20 hover:bg-charcoal/5',
  };

  return (
    <button className={`${base} ${variants[variant]}`} {...props}>
      {children}
    </button>
  );
}