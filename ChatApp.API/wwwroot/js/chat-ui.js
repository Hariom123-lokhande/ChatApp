import { state, el, cls, esc, formatTime, norm, scrollBottom } from './state.js';

// ─── MODAL ───────────────────────────────────────────────────
export function showModal(msg, isPrompt = false, title = 'Notification') {
    return new Promise(resolve => {
        if (state.modalResolve) state.modalResolve(null);
        state.modalResolve = resolve;
        el('modal-title').textContent = title;
        const msgEl = el('modal-msg');
        // Clear previous HTML if any
        msgEl.innerHTML = '';
        msgEl.textContent = msg; 
        
        cls('modal-overlay', 'hidden', false);
        cls('modal-prompt-div', 'hidden', !isPrompt);
        cls('modal-cancel', 'hidden', !isPrompt);
        if (isPrompt) { el('modal-input').value = ''; el('modal-input').focus(); }
    });
}

export function closeModal(isOk) {
    const val = el('modal-input').value;
    cls('modal-overlay', 'hidden', true);
    if (state.modalResolve) {
        state.modalResolve(isOk ? (val || true) : null);
        state.modalResolve = null;
    }
}

// ─── USERS & GROUPS ──────────────────────────────────────────
export function renderList(id, items, htmlFn, emptyMsg) {
    el(id).innerHTML = items.length
        ? items.map(htmlFn).join('')
        : `<p style="padding:20px;color:#64748b;text-align:center">${emptyMsg}</p>`;
}

// ─── MESSAGE RENDERING ───────────────────────────────────────
export const getTickHtml = (status, isSent) => {
    if (!isSent) return '';
    return `<span style="color:${status === 2 ? '#3b82f6' : '#94a3b8'};font-size:12px;margin-left:6px;">${status >= 1 ? '✓✓' : '✓'}</span>`;
};

export function buildBubble(m, myId, isGroup) {
    const mid = m.messageId ?? m.MessageId ?? '';
    const sid = norm(m.senderId ?? m.SenderId);
    const isSent = sid === myId && myId !== '';
    const status = m.deliveryStatus ?? m.DeliveryStatus ?? (isSent && (m.groupId ?? m.GroupId) ? 1 : 0);
    const senderLabel = !isSent && isGroup ? `<div class="msg-sender">${esc(m.senderName ?? m.SenderName ?? 'User')}</div>` : '';
    return `<div class="msg-row ${isSent ? 'sent' : 'recv'}" data-msgid="${mid}">
        <div class="msg-content">
            ${senderLabel}
            <div class="bubble">
                <div>${esc(m.content ?? m.Content ?? '')}</div>
                <div class="msg-meta">${formatTime(m.createdAt ?? m.CreatedAt)} ${m.isPending ? '⏳' : getTickHtml(status, isSent)}</div>
            </div>
        </div>
    </div>`;
}

export function renderMessages(msgs) {
    if (!Array.isArray(msgs)) msgs = [];
    const myId = norm(state.userId);
    const isGroup = state.currentChat.type === 'group';
    const seen = new Set();
    const unique = msgs.filter(m => {
        const mid = m?.messageId ?? m?.MessageId;
        if (mid && seen.has(mid)) return false;
        if (mid) seen.add(mid);
        return !!m;
    });
    try {
        el('messages').innerHTML = unique.map(m => buildBubble(m, myId, isGroup)).join('');
        scrollBottom();
    } catch (err) {
        console.error('[renderMessages]', err.message);
        el('messages').innerHTML = '<p style="text-align:center;color:#ef4444;padding:20px;">Error rendering messages</p>';
    }
}

export function appendMessage(m) {
    const msgId = m.messageId ?? m.MessageId;
    const requestId = m.requestId ?? m.RequestId;

    if (requestId) document.querySelector(`.msg-row[data-msgid="pending_${requestId}"]`)?.remove();
    if (msgId && document.querySelector(`.msg-row[data-msgid="${msgId}"]`)) return;

    if (state.currentChat.id) {
        const cache = state.chatCache[state.currentChat.type][state.currentChat.id] || [];
        if (requestId) {
            const pi = cache.findIndex(c => (c.messageId ?? c.MessageId ?? '').toString() === `pending_${requestId}`);
            if (pi > -1) cache.splice(pi, 1);
        }
        if (!msgId || !cache.find(c => (c.messageId ?? c.MessageId) === msgId)) {
            cache.push(m);
            state.chatCache[state.currentChat.type][state.currentChat.id] = cache;
        }
    }

    el('messages').insertAdjacentHTML('beforeend', buildBubble(m, norm(state.userId), state.currentChat.type === 'group'));
    scrollBottom();
}

export function updateMessageTick(msgId, status) {
    const tickEl = document.querySelector(`.msg-row[data-msgid="${msgId}"] .time span`);
    if (tickEl) tickEl.outerHTML = getTickHtml(status, true);
}
export function unreadBadgeHtml(count) {
    if (!count) return '';
    return `<span class="unread-badge">${count}</span>`;
}

export function patchUnreadBadges() {
    // Update all unread badges in sidebar using new .item-name structure
    ['users-list', 'groups-list'].forEach(listId => {
        const isUser = listId === 'users-list';
        document.querySelectorAll(`#${listId} .list-item`).forEach(item => {
            const id = norm(item.dataset.id);
            const count = isUser ? state.unread.user[id] : state.unread.group[id];
            const nameContainer = item.querySelector('.item-name');
            if (!nameContainer) return;

            let badge = nameContainer.querySelector('.unread-badge');
            if (count > 0) {
                if (!badge) {
                    nameContainer.insertAdjacentHTML('beforeend', `<span class="unread-badge">${count}</span>`);
                } else {
                    badge.textContent = count;
                }
            } else if (badge) {
                badge.remove();
            }
        });
    });
}