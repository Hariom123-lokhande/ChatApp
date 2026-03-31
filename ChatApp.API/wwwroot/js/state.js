export const API = `${window.location.origin}/api`;

export const state = {
    token: '',
    userId: '',
    username: '',
    connection: null,
    modalResolve: null,
    currentChat: { id: null, type: null, name: null },
    /** Sidebar unread counts; keys = norm(guid string) */
    unread: { user: {}, group: {} },
    lastSyncTime: sessionStorage.getItem('chat_last_sync') || new Date(0).toISOString(),
    pendingMsgs: JSON.parse(sessionStorage.getItem('chat_pending') || '[]'),
    chatCache: { user: {}, group: {} },
    typingTimeouts: {},
    typingIndicatorTimeout: null,
    
};

// ─── UTILS ───────────────────────────────────────────────────
export const el = id => document.getElementById(id);
export const cls = (id, className, force) => el(id)?.classList.toggle(className, force);
export const esc = str => Object.assign(document.createElement('div'), { textContent: str ?? '' }).innerHTML;
export const norm = val => (val ?? '').toString().toLowerCase();

export const extractArray = data => {
    if (Array.isArray(data)) return data;
    if (data && typeof data === 'object') return data.items ?? data.Items ?? [];
    return [];
};

export const parseDate = iso => {
    if (!iso) return new Date(0);
    const s = iso.toString();
    return new Date(s.includes('Z') || s.includes('+') ? s : s + 'Z');
};

export const formatTime = iso => {
    try {
        const d = parseDate(iso);
        return isNaN(d) ? '' : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    } catch { return ''; }
};

export const scrollBottom = () => {
    const area = el('messages-area');
    if (area) {
        // Quick scroll first
        area.scrollTop = area.scrollHeight;
        // Then double-check after layout finishes
        requestAnimationFrame(() => {
            area.scrollTop = area.scrollHeight;
        });
    }
};
