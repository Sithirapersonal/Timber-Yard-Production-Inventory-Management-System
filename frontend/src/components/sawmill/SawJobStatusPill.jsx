import { useState, useRef, useEffect } from 'react';

/**
 * Interactive colored pill for a SawJob's Status ('InProgress' | 'Completed' | 'Cancelled').
 * Clicking opens a dropdown menu to change status.
 */
const STATUS_CONFIG = {
  InProgress: {
    label: 'In Progress',
    pillClass: 'bg-heartwood/15 text-heartwood hover:bg-heartwood/25',
    dotClass: 'bg-heartwood',
  },
  Completed: {
    label: 'Complete',
    pillClass: 'bg-moss/15 text-moss hover:bg-moss/25',
    dotClass: 'bg-moss',
  },
  Cancelled: {
    label: 'Cancelled',
    pillClass: 'bg-rust/15 text-rust hover:bg-rust/25',
    dotClass: 'bg-rust',
  },
};

const ALL_STATUSES = [
  { key: 'InProgress', label: 'In Progress', dotClass: 'bg-heartwood' },
  { key: 'Completed', label: 'Complete', dotClass: 'bg-moss' },
  { key: 'Cancelled', label: 'Cancelled', dotClass: 'bg-rust' },
];

export default function SawJobStatusPill({ status, onSelectStatus, disabled = false }) {
  const [open, setOpen] = useState(false);
  const dropdownRef = useRef(null);

  const currentConfig = STATUS_CONFIG[status] || {
    label: status,
    pillClass: 'bg-fog/15 text-fog',
    dotClass: 'bg-fog',
  };

  useEffect(() => {
    function handleClickOutside(event) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
        setOpen(false);
      }
    }
    if (open) {
      document.addEventListener('mousedown', handleClickOutside);
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [open]);

  const handleSelect = (targetStatus) => {
    setOpen(false);
    if (targetStatus !== status && onSelectStatus) {
      onSelectStatus(targetStatus);
    }
  };

  return (
    <div className="relative inline-block text-left" ref={dropdownRef}>
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((prev) => !prev)}
        className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold cursor-pointer transition-colors ${currentConfig.pillClass} ${
          disabled ? 'opacity-60 cursor-default' : ''
        }`}
        title="Change job status"
      >
        <span>{currentConfig.label}</span>
        <span className="text-[9px] opacity-75">▼</span>
      </button>

      {open && (
        <div className="absolute left-0 mt-1 w-36 bg-white border border-charcoal/15 rounded-xl shadow-lg z-50 p-1.5 space-y-0.5">
          {ALL_STATUSES.map((item) => {
            const isCurrent = item.key === status;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => handleSelect(item.key)}
                className={`w-full flex items-center gap-2 text-left text-xs font-semibold px-2.5 py-1.5 rounded-lg text-charcoal hover:bg-sawdust/60 transition-colors ${
                  isCurrent ? 'bg-sawdust/90 font-bold' : ''
                }`}
              >
                <span className={`w-2 h-2 rounded-full flex-shrink-0 ${item.dotClass}`} />
                <span>{item.label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
