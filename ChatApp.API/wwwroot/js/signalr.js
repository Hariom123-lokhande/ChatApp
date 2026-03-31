import { state, cls, norm, parseDate, el } from './state.js';

import { appendMessage, updateMessageTick, patchUnreadBadges } from './chat-ui.js';

export async function startSignalR() {
    if (state.connection) await state.connection.stop();
    state.connection = new signalR.HubConnectionBuilder()
        .withUrl(`${window.location.origin}/chatHub`, {
            accessTokenFactory: () => sessionStorage.getItem('chat_token') || state.token,
        })
        .withAutomaticReconnect([0, 1000, 2000, 3000])
        .build();

    const showBanner = v => cls('offline-banner', 'hidden', !v);
    state.connection.onreconnecting(() => showBanner(true));
    state.connection.onreconnected(() => { showBanner(false); syncOffline(); });
    state.connection.onclose(() => {
        showBanner(true);
        if (sessionStorage.getItem('chat_token')) setTimeout(startSignalR, 5000);
    });

    state.connection.on('ReceivePrivateMessage', handleIncomingMsg);
    state.connection.on('ReceiveGroupMessage', handleIncomingMsg);
    state.connection.on('MessageSent', handleIncomingMsg);
    state.connection.on('UserOnline', d => updateUserStatus(d.userId ?? d.UserId, true));
    state.connection.on('UserOffline', d => updateUserStatus(d.userId ?? d.UserId, false));
    state.connection.on('OnlineUsers', uids => { if (Array.isArray(uids)) uids.forEach(id => updateUserStatus(id, true)); });
    state.connection.on('UserTyping', handleTyping);
    state.connection.on('UserStoppedTyping', d => hideTyping(norm(d.userId ?? d.UserId)));
    state.connection.on('AddedToGroup', async () => {
        const { loadGroups } = await import('./chat-core.js');
        loadGroups();
    });
    state.connection.on('MessageDelivered', d => updateMessageTick(d.messageId ?? d.MessageId, 1));
    state.connection.on('MessageRead', d => updateMessageTick(d.messageId ?? d.MessageId, 2));
    state.connection.on('MissedMessages', msgs => Array.isArray(msgs) && msgs.forEach(handleIncomingMsg));

    try { await state.connection.start(); showBanner(false); syncOffline(); }
    catch (err) { console.error('[SignalR] Failed:', err.message); showBanner(true); setTimeout(startSignalR, 5000); }
}

export function handleIncomingMsg(m) {
    if (!m) return;
    const sid = norm(m.senderId ?? m.SenderId);
    const rid = norm(m.receiverId ?? m.ReceiverId);
    const gid = norm(m.groupId ?? m.GroupId);
    const uid = norm(state.userId);
    const cid = norm(state.currentChat.id);
    const msgId = m.messageId ?? m.MessageId;
    const isOwn = sid === uid;

    const isTargetPrivate =
        state.currentChat.type === 'user' &&
        !gid &&
        (sid === cid || (isOwn && rid === cid));

    const isTargetGroup =
        state.currentChat.type === 'group' &&
        gid &&
        gid === cid;

    const store = gid ? state.chatCache.group : state.chatCache.user;
    const key = gid || (isOwn ? rid : sid);

    if (key) {
        const bucket = store[key] || [];
        if (msgId && !bucket.find(x => (x.messageId ?? x.MessageId) === msgId)) {
            bucket.push(m);
            store[key] = bucket;
        }
    }

    if (isTargetPrivate || isTargetGroup) {
        appendMessage(m);
        if (!isOwn && state.connection?.state === 'Connected' && msgId) {
            state.connection.invoke('MarkMessagesRead', [msgId]).catch(console.error);
        }
    }

    if (!isOwn) {
        if (gid) {
            const gk = norm(gid);
            const viewing = state.currentChat.type === 'group' && norm(state.currentChat.id) === gk;
            if (!viewing) {
                state.unread.group[gk] = (state.unread.group[gk] || 0) + 1;
                patchUnreadBadges();
            }
        } else {
            const other = norm(sid);
            const viewing = state.currentChat.type === 'user' && norm(state.currentChat.id) === other;
            if (!viewing) {
                state.unread.user[other] = (state.unread.user[other] || 0) + 1;
                patchUnreadBadges();
            }
        }
    }

    if (!isOwn && state.connection?.state === 'Connected' && msgId && !gid) {
        state.connection.invoke('MessageDelivered', msgId).catch(console.error);
    }

    const mt = m.createdAt ?? m.CreatedAt;
    if (mt && parseDate(mt) > parseDate(state.lastSyncTime)) {
        state.lastSyncTime = mt;
        sessionStorage.setItem('chat_last_sync', mt);
    }
}

export function handleTyping(d) {
    const cid = norm(state.currentChat.id);
    const uid = norm(d.userId ?? d.UserId);
    const gid = norm(d.groupId ?? d.GroupId);

    const relevant = state.currentChat.id && (
        (state.currentChat.type === 'user' && uid === cid) ||
        (state.currentChat.type === 'group' && gid === cid)
    );

    if (!relevant) return;

    const indicator = el('typing-indicator');
    if (!indicator) return;

    if (state.typingTimeouts[uid]) clearTimeout(state.typingTimeouts[uid]);

    indicator.textContent = `${d.username ?? d.Username ?? 'Someone'} is typing...`;

    cls('typing-indicator', 'hidden', false);

    state.typingTimeouts[uid] = setTimeout(() => hideTyping(uid), 2500);
}

export function hideTyping(uid) {
    if (uid && state.typingTimeouts[uid]) {
        clearTimeout(state.typingTimeouts[uid]);
        delete state.typingTimeouts[uid];
    }

    if (!Object.keys(state.typingTimeouts).length) {
        cls('typing-indicator', 'hidden', true);
    }
}

export async function markUnreadAsRead(msgs) {
    if (state.connection?.state !== 'Connected') return;
    const unread = msgs
        .filter(m =>
            norm(m.senderId ?? m.SenderId) !== norm(state.userId) &&
            (m.deliveryStatus ?? m.DeliveryStatus ?? 0) < 2 &&
            !(m.groupId ?? m.GroupId)
        )
        .map(m => m.messageId ?? m.MessageId)
        .filter(Boolean);
    if (unread.length) await state.connection.invoke('MarkMessagesRead', unread).catch(console.error);
}

export async function syncOffline() {
    if (state.connection?.state !== 'Connected') return;
    try { await state.connection.invoke('FetchMissedMessages', state.lastSyncTime); }
    catch (err) { console.error('[syncOffline]', err.message); }

    if (!state.pendingMsgs.length) return;
    const pending = [...state.pendingMsgs];
    state.pendingMsgs = [];
    for (const msg of pending) {
        try {
            await state.connection.invoke(msg.method, msg.arg);
        } catch (err) {
            console.error('[syncOffline] Pending send failed:', err.message);
            state.pendingMsgs.push(msg);
        }
    }
    sessionStorage.setItem('chat_pending', JSON.stringify(state.pendingMsgs));
}

function updateUserStatus(userId, online) {
    const uidNorm = norm(userId);
    
    // 1. Update Sidebar
    document.querySelectorAll('#users-list .list-item').forEach(row => {
        if (norm(row.dataset.id) !== uidNorm) return;
        const dot = row.querySelector('.status-dot');
        if (dot) {
            dot.classList.toggle('online', online);
            dot.classList.toggle('offline', !online);
        }
    });

    // 2. Update Active Chat Header/Details
    if (state.currentChat.type === 'user' && norm(state.currentChat.id) === uidNorm) {
        const headerStatus = el('chat-status-text');
        const detailsStatus = el('details-status');
        const statusStr = online ? '🟢 Online' : '⚫ Offline';
        
        if (headerStatus) headerStatus.textContent = online ? 'Active Now' : 'Offline';
        if (detailsStatus) detailsStatus.textContent = statusStr;
        
        const detailsAvatar = el('details-avatar');
        if (detailsAvatar) detailsAvatar.style.boxShadow = online ? '0 0 15px var(--primary-glow)' : 'none';
    } 
    else if (state.currentChat.type === 'group') {
        const memberRow = document.querySelector(`#members-list div[data-userid="${uidNorm}"]`);
        if (memberRow) {
            const dot = memberRow.querySelector('.status-dot');
            if (dot) {
                dot.classList.toggle('online', online);
                dot.classList.toggle('offline', !online);
            }
            
            // Recalculate header count
            const all = document.querySelectorAll('#members-list .status-dot');
            const total = all.length;
            const on = [...all].filter(d => d.classList.contains('online')).length;
            const text = `👥 ${on}/${total} Online`;
            if (el('chat-status-text')) el('chat-status-text').textContent = text;
            if (el('details-status')) el('details-status').textContent = text;
        }
    }
}
