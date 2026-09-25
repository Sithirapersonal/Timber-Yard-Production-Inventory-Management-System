/**
 * Centralized authenticated API client for the Timber Yard frontend.
 *
 * All authenticated HTTP requests must go through fetchWithAuth.
 * On a 401 Unauthorized response, the stored session is cleared and the user
 * is automatically redirected to /login with an explanatory message.
 */

/**
 * Module-level handler registered by AuthContext once on mount.
 * Holds a function that clears auth state and triggers the session-expired flow.
 */
let _onSessionExpired = null;

/**
 * Called once by AuthContext (via useEffect) to register the session-expired callback.
 * The callback clears sessionStorage + React auth state and navigates to /login.
 */
export function registerSessionExpiredHandler(handler) {
  _onSessionExpired = handler;
}

/**
 * Drop-in authenticated fetch wrapper.
 * Automatically attaches the Bearer token and handles 401 Unauthorized centrally.
 *
 * @param {string} url - Full URL to fetch.
 * @param {RequestInit} [options={}] - Standard fetch options. Do not include Authorization header.
 * @returns {Promise<Response>} Raw Response on success (any non-401 status).
 * @throws {Error} On network failure, or on 401 (throws 'SESSION_EXPIRED' to stop caller logic).
 */
export async function fetchWithAuth(url, options = {}) {
  const token = sessionStorage.getItem('token');

  const headers = {
    'Content-Type': 'application/json',
    ...(options.headers || {}),
    ...(token ? { Authorization: 'Bearer ' + token } : {}),
  };

  const response = await fetch(url, { ...options, headers });

  if (response.status === 401) {
    if (_onSessionExpired) {
      _onSessionExpired();
    }
    // Throw so the calling function aborts and its catch block fires,
    // preventing any further state updates on a dead session.
    throw new Error('SESSION_EXPIRED');
  }

  return response;
}
