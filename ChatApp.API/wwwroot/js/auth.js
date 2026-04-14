import { state, API, el, cls } from './state.js';
import { apiFetch } from './api.js';
import { enterChat } from './chat-core.js';

export function switchTab(tab) {
    document.querySelectorAll('.tab').forEach((t, i) =>
        t.classList.toggle('active', tab === 'login' ? i === 0 : i === 1)
    );
    cls('login-form', 'active', tab === 'login');
    cls('register-form', 'active', tab === 'register');
    cls('auth-msg', 'hidden', true);
}
export function showAuthMsg(text, type)

{
    const msgEl = el('auth-msg');
    msgEl.className = `msg ${type}`;
    msgEl.textContent = text;
    cls('auth-msg', 'hidden', false);
}

async function handleAuth(endpoint, body, isLogin) {
    try {
        const res = await fetch(`${API}/auth/${endpoint}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
            credentials: 'include' 
        });
        console.log(`[auth] ${endpoint} response:`, res.status);

        let data = {};
        try {
            const t = await res.text();
            if (t) data = JSON.parse(t);
        } catch (err) {
            console.error('[auth] JSON parse error:', err);
            throw new Error(res.ok ? 'Invalid server response' : `Request failed (${res.status})`);
        }

        if (!res.ok) {
            console.error(`[auth] ${endpoint} failed:`, data);
            throw new Error(data.message || data.Message || `${endpoint} failed`);
        }

        if (isLogin) {
            console.log('[auth] Login successful');
            const token = data.token ?? data.Token;
            const userId = data.userId ?? data.UserId;
            const username = data.username ?? data.Username;
            if (!token) throw new Error(data.message || 'No token from server');
            state.token = token;
            state.userId = userId;
            state.username = username;

           
            localStorage.setItem('chat_token', token);
            localStorage.setItem('chat_userId', String(userId));
            localStorage.setItem('chat_username', username);
            await enterChat();
        } else {
            showAuthMsg('Account created! Please sign in.', 'success');
            switchTab('login');
        }
    } catch (err) {
        showAuthMsg(err.message, 'error');
    }
}

export function handleLogin(e) {
    debugger;
    e.preventDefault();
    handleAuth('login', { email: el('login-email').value, password: el('login-password').value }, true);
}

export function handleRegister(e) {
    e.preventDefault();
    handleAuth('register', { username: el('reg-username').value, email: el('reg-email').value, password: el('reg-password').value }, false);
}

export function togglePassword(inputId, btn) {
    const input = el(inputId);
    if (!input) return;

    const isPassword = input.type === 'password';
    input.type = isPassword ? 'text' : 'password';

    // Professional SVG Toggle
    const open = btn.querySelector('.eye-open');
    const closed = btn.querySelector('.eye-closed');

    if (open && closed) {
        if (isPassword) {
            open.classList.add('hidden');
            closed.classList.remove('hidden');
        } else {
            open.classList.remove('hidden');
            closed.classList.add('hidden');
        }
    }
}

export async function logout() {
    try { await apiFetch(`${API}/auth/logout`, { method: 'POST' }); } catch { }
    localStorage.clear(); // Clear everything
    sessionStorage.clear();
    if (state.connection) await state.connection.stop();
    state.token = ''; state.userId = ''; state.username = '';
    state.chatCache = { user: {}, group: {} };
    state.unread = { user: {}, group: {} };
    state.currentChat = { id: null, type: null, name: null };
    cls('chat-screen', 'active', false);
    cls('auth-screen', 'active', true);
}
