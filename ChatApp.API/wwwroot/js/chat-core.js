import { state, API, el, cls, norm, extractArray, parseDate, esc } from './state.js';
import { apiFetch } from './api.js';
import { renderList, renderMessages, appendMessage, showModal, unreadBadgeHtml, patchUnreadBadges } from './chat-ui.js';
import { startSignalR, markUnreadAsRead } from './signalr.js';

export async function enterChat() {
    cls('auth-screen', 'active', false);
    cls('chat-screen', 'active', true);
    el('display-name').textContent = state.username;
    await Promise.all([loadUsers(), loadGroups()]);
    startSignalR();
}

export function switchPanel(p) {
    // Top Tabs
    document.querySelectorAll('.stab').forEach((t, i) =>
        t.classList.toggle('active', p === 'users' ? i === 0 : i === 1)
    );
    // Activity Bar Icons
    document.querySelectorAll('.activity-icon').forEach((icon, i) => {
        if (p === 'users' && i === 0) icon.classList.add('active');
        else if (p === 'groups' && i === 1) icon.classList.add('active');
        else if (i < 2) icon.classList.remove('active'); 
    });
    cls('users-panel', 'active', p === 'users');
    cls('groups-panel', 'active', p === 'groups');
}


export async function loadUsers() {
    try {
        const res = await apiFetch(`${API}/users`);
        const users = extractArray(await res.json()).filter(u =>
            norm(u.userId ?? u.UserId) !== norm(state.userId)
        );
        renderList('users-list', users, u => {
            const uid = u.userId ?? u.UserId;
            const uname = u.username ?? u.Username;
            const online = u.isOnline ?? u.IsOnline;
            const initials = uname.substring(0, 2).toUpperCase();
            const isActive = state.currentChat.type === 'user' && state.currentChat.id === uid;
            const ur = state.unread.user[norm(uid)] || 0;
            return `<div class="list-item${isActive ? ' active' : ''}" data-id="${uid}" onclick="openPrivateChat('${uid}','${esc(uname)}')">
                <div class="avatar">${initials} <span class="status-dot ${online ? 'online' : 'offline'}"></span></div>
                <div class="item-info">
                    <div class="item-name"><span>${esc(uname)}</span> ${ur > 0 ? `<span class="unread-badge">${ur}</span>` : ''}</div>
                    <div class="item-preview">${online ? 'Active now' : 'Seen recently'}</div>
                </div>
            </div>`;
        }, 'No other users online');
    } catch (err) { console.error('[loadUsers]', err.message); }
}

export async function loadGroups() {
    try {
        const res = await apiFetch(`${API}/groups`);
        const groups = extractArray(await res.json());
        renderList('groups-list', groups, g => {
            const gid = g.groupId ?? g.GroupId;
            const gname = g.name ?? g.Name;
            const isCreator = norm(g.createdBy ?? g.CreatedBy) === norm(state.userId);
            const isActive = state.currentChat.type === 'group' && state.currentChat.id === gid;
            const gr = state.unread.group[norm(gid)] || 0;
            const memberCount = g.memberCount ?? g.MemberCount ?? 0;
            return `<div class="list-item${isActive ? ' active' : ''}" data-id="${gid}" onclick="openGroupChat('${gid}','${esc(gname)}')">
                <div class="avatar" style="background:#f1f5f9; color:var(--primary)">👥</div>
                <div class="item-info">
                    <div class="item-name">
                        <span>${esc(gname)} ${isCreator ? '👑' : ''}</span>
                        ${gr > 0 ? `<span class="unread-badge">${gr}</span>` : ''}
                    </div>
                    <div class="item-preview">${g.onlineCount ?? g.OnlineCount ?? 0}/${memberCount} online</div>
                </div>
                ${isCreator ? `<div title="Delete" onclick="event.stopPropagation();deleteGroup('${gid}')" style="cursor:pointer;color:var(--danger);font-size:14px;padding-left:8px;">🗑️</div>` : ''}
            </div>`;
        }, 'No groups yet');
    } catch (err) { console.error('[loadGroups]', err.message); }
}

export async function createGroup() {
    const name = el('new-group-name').value.trim();
    if (!name) return;
    try {
        await apiFetch(`${API}/groups`, { method: 'POST', body: JSON.stringify({ name }) });
        el('new-group-name').value = '';
        await loadGroups();
    } catch (err) { console.error('[createGroup]', err.message); }
}

export async function deleteGroup(groupId) {
    if (!confirm('Are you sure you want to delete this group permanently?')) return;
    try {
        const res = await apiFetch(`${API}/groups/${groupId}`, { method: 'DELETE' });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.message || `HTTP ${res.status}`);
        if (state.currentChat.id === groupId) {
            cls('active-chat', 'hidden', true);
            cls('no-chat', 'hidden', false);
            state.currentChat = { id: null, type: null, name: null };
        }
        delete state.unread.group[norm(groupId)];
        await loadGroups();
    } catch (err) { showModal(err.message || 'Failed to delete group', false, 'Error'); }
}

export function setupChat(id, type, name, statusStr) {
    state.currentChat = { id, type, name };
    cls('no-chat', 'hidden', true);
    cls('active-chat', 'hidden', false);
    el('chat-title').textContent = type === 'group' ? `👥 ${name}` : name;
    el('chat-status-text').textContent = statusStr;
    el('details-status').textContent = statusStr;
    cls('add-member-btn', 'hidden', type !== 'group');
    el('msg-input').focus();

    // Update Details Panel Header
    el('details-avatar').textContent = name.substring(0, 2).toUpperCase();
    el('details-title').textContent = name;
    el('details-status').textContent = statusStr;
    cls('group-members-section', 'hidden', type !== 'group');

    const statusEl = el('chat-status-text');
    statusEl.onclick = null; statusEl.title = ''; statusEl.style.cursor = 'default';

    document.querySelectorAll('.list-item').forEach(item =>
        item.classList.toggle('active', norm(item.dataset.id) === norm(id))
    );

    const cached = state.chatCache[type][id];
    if (cached?.length) renderMessages(cached);
    else el('messages').innerHTML = '<p style="text-align:center;color:#94a3b8;padding:20px;">Loading...</p>';
}

export function mergeMessages(fetched, cached) {
    const seen = new Map();
    for (const m of fetched) {
        const mid = m.messageId ?? m.MessageId;
        if (mid) seen.set(mid, m);
    }
    for (const m of cached) {
        const mid = m.messageId ?? m.MessageId;
        const rid = m.requestId ?? m.RequestId;
        if (mid && !seen.has(mid)) seen.set(mid, m);
        else if (!mid && rid) seen.set(`req_${rid}`, m);
    }
    return [...seen.values()].sort(
        (a, b) => parseDate(a.createdAt ?? a.CreatedAt) - parseDate(b.createdAt ?? b.CreatedAt)
    );
}

export async function openPrivateChat(otherId, otherName) {
    debugger;
    delete state.unread.user[norm(otherId)];
    patchUnreadBadges();

    const isOnline = [...document.querySelectorAll('#users-list .list-item')].some(
        item => norm(item.dataset.id) === norm(otherId) && item.querySelector('.status-dot.online')
    );
    setupChat(otherId, 'user', otherName, isOnline ? 'Active Now' : 'Offline');
    try {
        const res = await apiFetch(`${API}/chat/private/${otherId}?pageSize=100`);
        if (state.currentChat.type !== 'user' || state.currentChat.id !== otherId) return;
        const merged = mergeMessages(extractArray(await res.json()), state.chatCache.user[otherId] || []);
        state.chatCache.user[otherId] = merged;
        renderMessages(merged);
        markUnreadAsRead(merged);
    } catch (err) {
        console.error('[openPrivateChat]', err.message);
        el('messages').innerHTML = '<p style="text-align:center;color:#ef4444;padding:20px;">❌ Failed to load</p>';
    }
}

export async function openGroupChat(groupId, groupName) {
    delete state.unread.group[norm(groupId)];
    patchUnreadBadges();

    setupChat(groupId, 'group', groupName, 'Loading...');
    
    // Automatically load members in right panel
    showMembersPanel(groupId);

    try {
        const res = await apiFetch(`${API}/chat/group/${groupId}?pageSize=100`);
        if (state.currentChat.type !== 'group' || state.currentChat.id !== groupId) return;
        const merged = mergeMessages(extractArray(await res.json()), state.chatCache.group[groupId] || []);
        state.chatCache.group[groupId] = merged;
        renderMessages(merged);
        markUnreadAsRead(merged);

        const statusEl = el('chat-status-text');
        statusEl.title = 'Group info is on the right';
        // Member info is handled by showMembersPanel which is called above
    } catch (err) {
        console.error('[openGroupChat]', err.message);
        if (state.currentChat.id === groupId) {
            el('chat-status-text').textContent = 'Error';
            el('messages').innerHTML = `<div style="text-align:center;color:#ef4444;padding:20px;">❌ Failed to load.<br><small>${esc(err.message)}</small></div>`;
        }
    }
}

export async function showMembersPanel(groupId) {
    try {
        // Clear current list
        el('members-list').innerHTML = '<p style="color:var(--text-dim); padding:10px;">Refreshing...</p>';

        const r = await (await apiFetch(`${API}/groups/${groupId}/members`)).json();
        const members = extractArray(r);
        
        // Update Group Header if it's the current chat
        if (state.currentChat.type === 'group' && norm(state.currentChat.id) === norm(groupId)) {
            const onlineCount = members.filter(m => m.isOnline ?? m.IsOnline).length;
            el('details-status').textContent = `${onlineCount}/${members.length} Online`;
            el('chat-status-text').textContent = `👥 ${onlineCount}/${members.length} Online`;
        }

        const group = await (await apiFetch(`${API}/groups/${groupId}`)).json();
        const groupData = group.group ?? group.Group ?? group;
        const isAdmin = norm(groupData.createdBy ?? groupData.CreatedBy) === norm(state.userId);

        const memberHtml = members.map(m => {
            const uid = m.userId ?? m.UserId;
            const uname = esc(m.username ?? m.Username);
            const isOnline = m.isOnline ?? m.IsOnline ?? false;
            const role = m.role ?? m.Role ?? 'Member';
            const isMe = norm(uid) === norm(state.userId);
            const roleTag = role === 'Admin' ? ' <span style="color:var(--primary);font-size:11px;font-weight:700;">👑 Admin</span>' : '';
            const statusDot = `<span class="status-dot ${isOnline ? 'online' : 'offline'}" style="width:8px;height:8px;margin-right:8px;"></span>`;
            const removeBtn = (isAdmin && !isMe)
                ? `<span onclick="removeMember('${groupId}','${uid}','${uname}')" style="cursor:pointer;color:var(--danger);font-size:14px;margin-left:auto;padding:4px;" title="Remove ${uname}">❌</span>`
                : '';
            return `<div data-userid="${uid}" style="display:flex;align-items:center;padding:12px 0;border-bottom:1px solid var(--border);position:relative;">
                <span class="status-dot ${isOnline ? 'online' : 'offline'}" style="position:static;display:inline-block;width:10px;height:10px;margin-right:10px;flex-shrink:0;"></span>
                <span style="flex:1;font-size:14px;">${uname}${roleTag}${isMe ? ' <span style="color:var(--text-dim);font-size:11px;">(You)</span>' : ''}</span>
                ${removeBtn}
            </div>`;
        }).join('');

        el('members-list').innerHTML = memberHtml || '<p style="color:var(--text-dim); font-size:13px; padding:10px;">No members found</p>';
    } catch (err) {
        console.error('[showMembersPanel]', err);
        el('members-list').innerHTML = `<p style="color:var(--danger); font-size:13px;">Error loading members</p>`;
    }
}

export async function removeMember(groupId, memberId, memberName) {
    if (!confirm(`Remove ${memberName} from this group?`)) return;
    try {
        const res = await apiFetch(`${API}/groups/${groupId}/members/${memberId}`, { method: 'DELETE' });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.message || `HTTP ${res.status}`);
        // Refresh the members panel
        showMembersPanel(groupId);
    } catch (err) {
        showModal(err.message || 'Failed to remove member', false, 'Error');
    }
}


export async function promptAddMember() {
    const email = await showModal('Enter member email:', true, 'Add Member');
    if (!email || email === true) return;
    try {
        const res = await apiFetch(`${API}/groups/${state.currentChat.id}/members/add-by-email`, {
            method: 'POST', body: JSON.stringify({ email }),
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.message || 'Failed to add');
        showModal(data.message || 'Member added successfully!');
    } catch (err) { showModal(err.message, false, 'Error'); }
}

export async function sendMessage() {
    debugger;
    const inp = el('msg-input');
    const content = inp.value.trim();
    if (!content || !state.currentChat.id) return;
    inp.value = ''; inp.style.height = 'auto';

    if (state.connection?.state === 'Disconnected')
        return showModal('Disconnected. Please wait or refresh.');

    const isGroup = state.currentChat.type === 'group';
    const msgId = Date.now().toString();
    const method = isGroup ? 'SendGroupMessage' : 'SendPrivateMessage';
    const arg = { content, requestId: msgId, ...(isGroup ? { groupId: state.currentChat.id } : { receiverId: state.currentChat.id }) };
    const fakeMsg = {
        senderId: state.userId, senderName: state.username, content,
        createdAt: new Date().toISOString(), isPending: false,
        messageId: `pending_${msgId}`, requestId: msgId,
    };

    if (state.connection?.state === 'Connected') {
        try {
            appendMessage(fakeMsg);
            await state.connection.invoke(method, arg);
        } catch (err) {
            console.error('[sendMessage]', err.message);
            fakeMsg.isPending = true;
            renderMessages(state.chatCache[state.currentChat.type][state.currentChat.id] || []);
            queueMsg(method, arg);
        }
    } else {
        fakeMsg.isPending = true; appendMessage(fakeMsg); queueMsg(method, arg);
    }
}

export function queueMsg(method, arg) {
    state.pendingMsgs.push({ method, arg });
    sessionStorage.setItem('chat_pending', JSON.stringify(state.pendingMsgs));
}
