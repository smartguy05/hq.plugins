'use strict';

const state = {
  token: null,
  orgId: null,
  apiBaseUrl: null,
  agents: [],
  sel: { agentId: null, account: null, folder: null, messageId: null },
  current: null, // currently open full message
  messages: [], // summaries currently shown in the list
  selected: new Set(), // messageIds checked for bulk actions
};

// --- host bridge -----------------------------------------------------------

window.addEventListener('message', (ev) => {
  const data = ev.data;
  if (!data || data.type !== 'hq:bootstrap') return;
  state.token = data.accessToken;
  state.orgId = data.orgId;
  state.apiBaseUrl = data.apiBaseUrl;
  document.body.classList.toggle('theme-dark', data.theme === 'dark');
  document.body.classList.toggle('theme-light', data.theme !== 'dark');
  loadAgents();
});

function sendReady() { window.parent.postMessage({ type: 'hq:ready' }, '*'); }

// Inline confirmation (the plugin iframe is sandboxed, so window.confirm is unavailable).
function confirmDialog(message, okLabel = 'Delete') {
  return new Promise((resolve) => {
    const overlay = document.getElementById('confirm');
    const ok = document.getElementById('confirm-ok');
    const cancel = document.getElementById('confirm-cancel');
    document.getElementById('confirm-msg').textContent = message;
    ok.textContent = okLabel;
    overlay.classList.remove('hidden');
    const done = (val) => { overlay.classList.add('hidden'); ok.onclick = null; cancel.onclick = null; resolve(val); };
    ok.onclick = () => done(true);
    cancel.onclick = () => done(false);
  });
}

function toast(kind, msg) {
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.className = 'toast ' + (kind || '');
  setTimeout(() => el.classList.add('hidden'), 3200);
  if (kind === 'error') window.parent.postMessage({ type: 'hq:toast', kind, message: msg }, '*');
}

async function api(path, options = {}) {
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  if (state.token) headers['Authorization'] = `Bearer ${state.token}`;
  if (state.orgId) headers['X-Organization-Id'] = state.orgId;
  const res = await fetch(`${state.apiBaseUrl}${path}`, { ...options, headers });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`${res.status} ${res.statusText}${text ? ': ' + text : ''}`);
  }
  if (res.status === 204) return null;
  return res.json();
}

// --- data ------------------------------------------------------------------

async function loadAgents() {
  try {
    state.agents = await api('/agents');
    renderAgents();
  } catch (e) { toast('error', 'Failed to load agents: ' + e.message); }
}

async function loadFolders(agentId, account) {
  const q = new URLSearchParams({ agentId, account: account || '' });
  return api(`/folders?${q}`);
}

async function loadMessages(agentId, account, folder, search) {
  const q = new URLSearchParams({ agentId, account: account || '', folder: folder || '', max: '150' });
  if (search) q.set('search', search);
  return api(`/messages?${q}`);
}

async function loadMessage(agentId, messageId) {
  const q = new URLSearchParams({ agentId, messageId });
  return api(`/message?${q}`);
}

async function postAction(path, body) {
  return api(path, { method: 'POST', body: JSON.stringify(body) });
}

// --- rendering: agents / folders ------------------------------------------

function renderAgents() {
  const root = document.getElementById('agents');
  root.innerHTML = '';
  if (!state.agents.length) {
    root.innerHTML = '<div class="empty">No agents with a synced inbox yet.</div>';
    return;
  }
  for (const agent of state.agents) {
    const wrap = document.createElement('div');
    wrap.className = 'agent';

    const name = document.createElement('div');
    name.className = 'agent-name';
    const label = document.createElement('span');
    label.textContent = shortAgent(agent.agentId);
    const sync = document.createElement('button');
    sync.className = 'btn mini';
    sync.textContent = 'Sync';
    sync.title = 'Sync this agent now';
    sync.onclick = () => syncAgent(agent.agentId, sync);
    name.append(label, sync);
    wrap.appendChild(name);

    const accounts = agent.accounts && agent.accounts.length
      ? agent.accounts
      : [{ name: null, email: '(default)' }];
    for (const acct of accounts) {
      const accEl = document.createElement('div');
      accEl.className = 'account';
      accEl.textContent = acct.email || acct.name || '(account)';
      wrap.appendChild(accEl);

      const folders = document.createElement('div');
      folders.dataset.agent = agent.agentId;
      wrap.appendChild(folders);
      renderFolders(folders, agent.agentId, acct.name);
    }
    root.appendChild(wrap);
  }
}

async function renderFolders(container, agentId, account) {
  try {
    const folders = await loadFolders(agentId, account);
    container.innerHTML = '';
    if (!folders.length) {
      const e = document.createElement('div');
      e.className = 'folder'; e.style.cursor = 'default';
      e.innerHTML = '<span style="color:var(--muted)">no folders synced</span>';
      container.appendChild(e);
      return;
    }
    for (const f of folders) {
      const el = document.createElement('div');
      el.className = 'folder';
      el.dataset.agent = agentId;
      el.dataset.account = account || '';
      el.dataset.folder = f.name;
      el.innerHTML = `<span>${escapeHtml(f.name)}</span><span class="count"></span>`;
      setFolderCount(el, f.messages, f.unread);
      el.onclick = () => selectFolder(agentId, account, f.name, el);
      container.appendChild(el);
    }
  } catch (e) { /* leave empty on error */ }
}

function setFolderCount(el, messages, unread) {
  messages = Math.max(0, parseInt(messages, 10) || 0);
  unread = Math.max(0, parseInt(unread, 10) || 0);
  el.dataset.messages = String(messages);
  el.dataset.unread = String(unread);
  const span = el.querySelector('.count');
  if (!span) return;
  span.className = 'count' + (unread ? ' unread' : '');
  span.textContent = (unread ? unread + ' / ' : '') + messages;
}

// Adjust the currently selected folder's counts in place (e.g. after delete / mark-read).
function adjustSelectedFolder(dMessages, dUnread) {
  const el = document.querySelector('.folder.sel');
  if (!el) return;
  setFolderCount(el, (parseInt(el.dataset.messages, 10) || 0) + dMessages,
    (parseInt(el.dataset.unread, 10) || 0) + dUnread);
}

function selectFolder(agentId, account, folder, el) {
  state.sel = { agentId, account, folder, messageId: null };
  document.querySelectorAll('.folder.sel').forEach(n => n.classList.remove('sel'));
  if (el) el.classList.add('sel');
  document.getElementById('search').value = '';
  document.getElementById('list-title').textContent = folder;
  refreshMessages();
}

// --- rendering: message list ----------------------------------------------

async function refreshMessages() {
  const { agentId, account, folder } = state.sel;
  const box = document.getElementById('messages');
  state.messages = [];
  clearSelection();
  if (!agentId) { box.innerHTML = '<div class="empty">Pick a folder.</div>'; return; }
  const search = document.getElementById('search').value.trim();
  box.innerHTML = '<div class="empty">Loading…</div>';
  try {
    const msgs = await loadMessages(agentId, account, folder, search);
    state.messages = msgs;
    if (!msgs.length) { box.innerHTML = '<div class="empty">No messages.</div>'; return; }
    box.innerHTML = '';
    for (const m of msgs) box.appendChild(buildMessageRow(m));
  } catch (e) { box.innerHTML = `<div class="empty">Failed: ${escapeHtml(e.message)}</div>`; }
}

function buildMessageRow(m) {
  const el = document.createElement('div');
  el.className = 'msg' + (m.isRead ? '' : ' unread');
  el.dataset.id = m.messageId;

  const check = document.createElement('input');
  check.type = 'checkbox';
  check.className = 'msg-check';
  check.setAttribute('aria-label', 'Select email');
  check.checked = state.selected.has(m.messageId);
  // Don't open the message when toggling its checkbox.
  check.onclick = (e) => e.stopPropagation();
  check.onchange = () => toggleSelect(m.messageId, check.checked, el);

  const main = document.createElement('div');
  main.className = 'msg-main';
  main.innerHTML =
    `<div class="msg-row"><span class="msg-from">${escapeHtml(m.from || m.fromAddress || '')}</span>` +
    `<span class="msg-date">${fmtDate(m.date)}</span></div>` +
    `<div class="msg-row"><span class="msg-subject">${escapeHtml(m.subject || '(no subject)')}</span>` +
    `<span class="msg-flags">${m.isFlagged ? '★' : ''}${m.hasAttachments ? ' 📎' : ''}</span></div>`;
  main.onclick = () => openMessage(m.messageId, el);

  el.append(check, main);
  if (check.checked) el.classList.add('checked');
  return el;
}

// --- selection / bulk delete ----------------------------------------------

function toggleSelect(messageId, checked, row) {
  if (checked) state.selected.add(messageId);
  else state.selected.delete(messageId);
  if (row) row.classList.toggle('checked', checked);
  updateSelBar();
}

function clearSelection() {
  state.selected.clear();
  document.querySelectorAll('.msg.checked').forEach(n => n.classList.remove('checked'));
  document.querySelectorAll('.msg-check:checked').forEach(c => { c.checked = false; });
  updateSelBar();
}

function updateSelBar() {
  const bar = document.getElementById('selbar');
  const count = state.selected.size;
  document.getElementById('sel-count').textContent = `${count} selected`;
  bar.classList.toggle('hidden', count === 0);
}

async function bulkDelete() {
  const ids = [...state.selected];
  if (!ids.length) return;
  const ok = await confirmDialog(
    `Delete ${ids.length} selected email${ids.length === 1 ? '' : 's'}? This permanently removes ${ids.length === 1 ? 'it' : 'them'} from the mailbox.`);
  if (!ok) return;

  const items = ids.map((id) => {
    const m = state.messages.find((x) => x.messageId === id);
    return { messageId: id, folder: (m && m.folder) || state.sel.folder };
  });

  const busy = document.getElementById('list-busy');
  busy.classList.remove('hidden');
  try {
    const res = await postAction('/actions/delete-bulk', {
      agentId: state.sel.agentId, account: state.sel.account, items,
    });
    if (res && res.success === false) throw new Error(res.message || 'failed');

    let removed = 0, unreadRemoved = 0;
    for (const id of ids) {
      const m = state.messages.find((x) => x.messageId === id);
      const row = document.querySelector(`.msg[data-id="${cssEscape(id)}"]`);
      if (row) { row.remove(); removed++; if (m && !m.isRead) unreadRemoved++; }
      if (state.current && state.current.messageId === id) {
        state.current = null;
        document.getElementById('reader').classList.add('hidden');
        document.getElementById('reader-empty').classList.remove('hidden');
      }
    }
    adjustSelectedFolder(-removed, -unreadRemoved);
    state.messages = state.messages.filter((x) => !state.selected.has(x.messageId));
    clearSelection();
    toast('ok', `Deleted ${res && typeof res.deleted === 'number' ? res.deleted : removed}`);
  } catch (e) { toast('error', `Delete failed: ${e.message}`); }
  finally { busy.classList.add('hidden'); }
}

// --- rendering: reader -----------------------------------------------------

async function openMessage(messageId, el) {
  state.sel.messageId = messageId;
  document.querySelectorAll('.msg.sel').forEach(n => n.classList.remove('sel'));
  if (el) el.classList.add('sel');
  try {
    const m = await loadMessage(state.sel.agentId, messageId);
    state.current = m;
    renderReader(m);
    // Opening an unread message marks it read (best-effort, like a mail client).
    if (!m.isRead) doAction('mark-read', true, true);
  } catch (e) { toast('error', 'Failed to open: ' + e.message); }
}

function renderReader(m) {
  document.getElementById('reader-empty').classList.add('hidden');
  document.getElementById('reader').classList.remove('hidden');
  document.getElementById('r-subject').textContent = m.subject || '(no subject)';
  document.getElementById('r-from').textContent = `${m.from || ''} <${m.fromAddress || ''}>`;
  document.getElementById('r-date').textContent = fmtDate(m.date, true);

  const att = document.getElementById('r-attachments');
  att.innerHTML = '';
  if (m.hasAttachments && m.attachmentNames) {
    for (const n of String(m.attachmentNames).split(',').map(s => s.trim()).filter(Boolean))
      att.insertAdjacentHTML('beforeend', `<span class="att">📎 ${escapeHtml(n)}</span>`);
  }

  // Render in a sandboxed iframe (sandbox="" blocks scripts/forms/navigation).
  const frame = document.getElementById('r-body');
  if (m.bodyHtml) frame.srcdoc = m.bodyHtml;
  else frame.srcdoc = `<pre style="white-space:pre-wrap;font:14px/1.5 sans-serif;padding:16px;margin:0">${escapeHtml(m.bodyText || '')}</pre>`;

  syncActionButtons(m);
}

function syncActionButtons(m) {
  const read = document.getElementById('a-read');
  read.textContent = m.isRead ? 'Mark unread' : 'Mark read';
  read.onclick = () => doAction('mark-read', !m.isRead);

  const flag = document.getElementById('a-flag');
  flag.textContent = m.isFlagged ? 'Unflag' : 'Flag';
  flag.onclick = () => doAction('flag', !m.isFlagged);

  document.getElementById('a-delete').onclick = () => doDelete();
}

// --- actions ---------------------------------------------------------------

async function doAction(kind, value, silent) {
  const m = state.current;
  if (!m) return;
  const body = {
    agentId: state.sel.agentId, account: state.sel.account,
    folder: m.folder || state.sel.folder, messageId: m.messageId, value,
  };
  try {
    const res = await postAction(`/actions/${kind}`, body);
    if (res && res.success === false) throw new Error(res.message || 'failed');
    if (kind === 'mark-read' && m.isRead !== value) {
      m.isRead = value;
      adjustSelectedFolder(0, value ? -1 : 1);
    }
    if (kind === 'flag') m.isFlagged = value;
    syncActionButtons(m);
    updateListRow(m);
    if (!silent) toast('ok', 'Done');
  } catch (e) { toast('error', `Action failed: ${e.message}`); }
}

async function doDelete() {
  const m = state.current;
  if (!m) return;
  const ok = await confirmDialog(`Delete "${m.subject || '(no subject)'}"? This permanently removes it from the mailbox.`);
  if (!ok) return;
  const body = {
    agentId: state.sel.agentId, account: state.sel.account,
    folder: m.folder || state.sel.folder, messageId: m.messageId,
  };
  const busy = document.getElementById('reader-busy');
  busy.classList.remove('hidden');
  try {
    const res = await postAction('/actions/delete', body);
    if (res && res.success === false) throw new Error(res.message || 'failed');
    toast('ok', 'Deleted');
    adjustSelectedFolder(-1, m.isRead ? 0 : -1);
    state.current = null;
    document.getElementById('reader').classList.add('hidden');
    document.getElementById('reader-empty').classList.remove('hidden');
    const row = document.querySelector(`.msg[data-id="${cssEscape(m.messageId)}"]`);
    if (row) row.remove();
    state.messages = state.messages.filter((x) => x.messageId !== m.messageId);
    if (state.selected.delete(m.messageId)) updateSelBar();
  } catch (e) { toast('error', `Delete failed: ${e.message}`); }
  finally { busy.classList.add('hidden'); }
}

function updateListRow(m) {
  const row = document.querySelector(`.msg[data-id="${cssEscape(m.messageId)}"]`);
  if (!row) return;
  row.classList.toggle('unread', !m.isRead);
  const flags = row.querySelector('.msg-flags');
  if (flags) flags.textContent = `${m.isFlagged ? '★' : ''}${m.hasAttachments ? ' 📎' : ''}`;
}

async function syncAgent(agentId, btn) {
  btn.disabled = true; const old = btn.textContent; btn.textContent = '…';
  try {
    const res = await postAction(`/sync?agentId=${encodeURIComponent(agentId)}`, {});
    if (res && res.success === false) throw new Error(res.message || 'failed');
    toast('ok', 'Sync complete');
    if (state.sel.agentId === agentId) refreshMessages();
  } catch (e) { toast('error', `Sync failed: ${e.message}`); }
  finally { btn.disabled = false; btn.textContent = old; }
}

// --- utils -----------------------------------------------------------------

function shortAgent(id) { return id ? `Agent ${String(id).slice(0, 8)}` : 'Agent'; }
function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function cssEscape(s) { return String(s).replace(/["\\]/g, '\\$&'); }
function fmtDate(d, full) {
  if (!d) return '';
  const dt = new Date(d);
  if (isNaN(dt)) return '';
  return full ? dt.toLocaleString() : dt.toLocaleDateString();
}

document.getElementById('refresh-agents').onclick = loadAgents;
document.getElementById('sel-delete').onclick = bulkDelete;
document.getElementById('sel-clear').onclick = clearSelection;
let searchTimer = null;
document.getElementById('search').addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(refreshMessages, 300);
});

sendReady();
