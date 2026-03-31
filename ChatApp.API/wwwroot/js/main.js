import { state, API, el } from './state.js';
import { apiFetch } from './api.js';
import { switchTab, handleLogin, handleRegister, togglePassword, logout } from './auth.js';
import { closeModal } from './chat-ui.js';
import {
    enterChat, switchPanel, createGroup, deleteGroup,
    openPrivateChat, openGroupChat, promptAddMember, sendMessage,
    showMembersPanel, removeMember
} from './chat-core.js';

// ─── UI HELPERS ─────────────────────────────
function filterList() {
    const q = el('chat-search').value.toLowerCase();
    document.querySelectorAll('.list-item').forEach(item => {
        const name = item.querySelector('.item-name span')?.textContent.toLowerCase() || '';
        item.style.display = name.includes(q) ? 'flex' : 'none';
    });
}
function toggleDetails() {
    const p = el('details-panel');
    const isActive = p.classList.toggle('active');
}

function toggleTheme() {
    const isLight = document.body.classList.toggle('light-mode');
    localStorage.setItem('chat_theme', isLight ? 'light' : 'dark');
    const btn = el('theme-toggle');
    if (btn) btn.textContent = isLight ? '☀️' : '🌙';
}

// Expose functions globally so inline HTML onclick/onsubmit handlers can access them
window.toggleDetails = toggleDetails;
window.toggleTheme = toggleTheme;
window.filterList = filterList;
window.switchTab = switchTab;
window.handleLogin = handleLogin;
window.handleRegister = handleRegister;
window.logout = logout;
window.togglePassword = togglePassword;
window.closeModal = closeModal;
window.switchPanel = switchPanel;
window.createGroup = createGroup;
window.deleteGroup = deleteGroup;
window.openPrivateChat = openPrivateChat;
window.openGroupChat = openGroupChat;
window.promptAddMember = promptAddMember;
window.sendMessage = sendMessage;
window.showMembersPanel = showMembersPanel;
window.removeMember = removeMember;

// ─── UI EVENTS & SESSION RESTORE ─────────────────────────────
document.addEventListener('DOMContentLoaded', () => {

    // --- Restore Theme ---
    const savedTheme = localStorage.getItem('chat_theme') || 'light';
    if (savedTheme === 'light') {
        document.body.classList.add('light-mode');
        const btn = el('theme-toggle');
        if (btn) btn.textContent = '☀️';
    }

    const msgInput = el('msg-input');

    if (msgInput) {
        msgInput.addEventListener('input', function () {
            this.style.height = 'auto';
            this.style.height = `${Math.min(this.scrollHeight, 100)}px`;

            const { id, type } = state.currentChat;

            if (!id || state.connection?.state !== 'Connected') return;

            state.connection.invoke('SendTyping', id, type === 'group')
                .catch(console.error);

            clearTimeout(state.typingIndicatorTimeout);

            state.typingIndicatorTimeout = setTimeout(() => {
                state.connection.invoke('StopTyping', id, type === 'group')
                    .catch(console.error);
            }, 2000);
        });

        msgInput.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });
    }

    const savedId = localStorage.getItem('chat_userId');
    const token = localStorage.getItem('chat_token');

    if (savedId && token && savedId !== 'null') {

        state.userId = savedId;
        state.username = localStorage.getItem('chat_username') || '';
        state.token = token;



        //  FIXED (no logout on refresh)
        (async () => {
            try {
                const res = await apiFetch(`${API}/auth/verify`);

                if (res.ok) {
                    enterChat();
                } else {
                    console.warn("Verify failed → restoring session");
                    enterChat();
                }

            } catch (err) {
                console.warn("Network issue → restoring session");
                enterChat();
            }
        })();
    }

});
function changeLanguage(lang) {
    const url = new URL(window.location.href);
    url.searchParams.set('culture', lang);
    url.searchParams.set('ui-culture', lang);
    window.location.href = url.href;
}

// global banana IMPORTANT hai
window.changeLanguage = changeLanguage;
