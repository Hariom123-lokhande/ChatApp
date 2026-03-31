import { state, API } from './state.js';
import { showModal } from './chat-ui.js';
import { logout } from './auth.js';

export async function apiFetch(url, opts = {}) {
   
    const tk = sessionStorage.getItem('chat_token') || localStorage.getItem('chat_token') || state.token;

    const headers = {
        'Content-Type': 'application/json',
        ...(tk ? { Authorization: `Bearer ${tk}` } : {}),
        ...opts.headers,
    };
    try {
        const res = await fetch(url, { 
            ...opts, 
            headers,
            credentials: 'include'
        });
        if (!res.ok) {
            if (res.status === 401 && !url.includes('/auth/login')) {
                showModal('Your session has expired. Please log in again.', false, 'Session Expired').then(logout);
            }
            if (url.includes('/auth/verify') && res.status === 401) return res;
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.message || errData.title || `HTTP ${res.status}`);
        }
        return res;
    } catch (err) {
        console.error('[API] Request failed:', url, err.message);
        throw err;
    }
}
