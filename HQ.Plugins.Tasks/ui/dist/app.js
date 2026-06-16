(function () {
  'use strict';

  const state = {
    token: null,
    orgId: null,
    apiBaseUrl: null,
    agents: [],
    projects: [],
    tasks: [],
    // Current selection: { kind: 'agent' | 'project', id, name } or null.
    selection: null,
    statusFilter: '',
  };

  // --- Host postMessage bootstrap --------------------------------------------

  function sendReady() {
    window.parent.postMessage({ type: 'hq:ready' }, '*');
  }

  window.addEventListener('message', (ev) => {
    const data = ev.data;
    if (!data || data.type !== 'hq:bootstrap') return;
    state.token = data.accessToken;
    state.orgId = data.orgId;
    state.apiBaseUrl = data.apiBaseUrl;
    if (data.theme === 'dark') document.body.classList.replace('theme-light', 'theme-dark');
    else document.body.classList.replace('theme-dark', 'theme-light');
    refresh();
  });

  function toast(level, message) {
    window.parent.postMessage({ type: 'hq:toast', level, message }, '*');
  }

  // --- API client ------------------------------------------------------------

  async function api(path, options = {}) {
    const headers = {
      'Content-Type': 'application/json',
      ...(options.headers || {}),
    };
    if (state.token) headers['Authorization'] = `Bearer ${state.token}`;
    if (state.orgId) headers['X-Organization-Id'] = state.orgId;
    const res = await fetch(`${state.apiBaseUrl}${path}`, { ...options, headers });
    if (!res.ok) {
      const msg = `${res.status} ${res.statusText}`;
      toast('error', msg);
      throw new Error(msg);
    }
    if (res.status === 204) return null;
    return res.json();
  }

  async function loadAgents() {
    state.agents = (await api('/agents')) || [];
  }

  async function loadProjects() {
    state.projects = (await api('/projects')) || [];
  }

  async function loadTasks() {
    if (!state.selection) { state.tasks = []; return; }
    const qs = new URLSearchParams();
    if (state.selection.kind === 'project') qs.set('projectId', state.selection.id);
    else qs.set('agentId', state.selection.id);
    if (state.statusFilter) qs.set('status', state.statusFilter);
    state.tasks = (await api(`/tasks?${qs.toString()}`)) || [];
  }

  // --- Rendering -------------------------------------------------------------

  function renderNavItem(list, kind, id, name) {
    const li = document.createElement('li');
    li.dataset.id = id;
    if (state.selection && state.selection.kind === kind && state.selection.id === id) {
      li.classList.add('active');
    }
    li.addEventListener('click', () => selectNode(kind, id, name));

    const label = document.createElement('span');
    label.className = 'nav-label';
    label.textContent = name || '(unnamed)';
    li.appendChild(label);

    // Projects can be deleted (agents are derived from task data, so they aren't).
    if (kind === 'project') {
      const del = document.createElement('button');
      del.className = 'btn-ghost nav-del';
      del.title = 'Delete project';
      del.textContent = '×';
      del.addEventListener('click', (ev) => {
        ev.stopPropagation();
        deleteProject(id, name);
      });
      li.appendChild(del);
    }

    list.appendChild(li);
  }

  function renderSidebar() {
    const agentList = document.getElementById('agent-list');
    agentList.innerHTML = '';
    for (const a of state.agents) renderNavItem(agentList, 'agent', a.agentId, a.agentName);

    const projectList = document.getElementById('project-list');
    projectList.innerHTML = '';
    for (const p of state.projects) renderNavItem(projectList, 'project', p.id, p.name);
  }

  function renderTasks() {
    const list = document.getElementById('task-list');
    list.innerHTML = '';
    const empty = document.getElementById('empty-state');
    const heading = document.getElementById('current-scope-name');

    if (!state.selection) {
      empty.hidden = false;
      empty.textContent = (state.agents.length === 0 && state.projects.length === 0)
        ? 'No agents or projects yet. Create a project to start tracking tasks.'
        : 'Select an agent or project on the left.';
      heading.textContent = 'Select an agent or project';
      document.getElementById('new-task-form').hidden = true;
      return;
    }

    empty.hidden = true;
    document.getElementById('new-task-form').hidden = false;
    heading.textContent = state.selection.name || '';

    for (const t of state.tasks) {
      const li = document.createElement('li');
      li.className = `task-row${t.status === 'done' ? ' done' : ''}`;

      const cb = document.createElement('input');
      cb.type = 'checkbox';
      cb.checked = t.status === 'done';
      cb.addEventListener('change', () => toggleTask(t));
      li.appendChild(cb);

      const title = document.createElement('span');
      title.className = 'task-title';
      title.textContent = t.title;
      li.appendChild(title);

      const pill = document.createElement('span');
      pill.className = `status-pill ${t.status}`;
      pill.textContent = t.status;
      li.appendChild(pill);

      const del = document.createElement('button');
      del.className = 'btn-ghost';
      del.title = 'Delete';
      del.textContent = '×';
      del.addEventListener('click', () => deleteTask(t));
      li.appendChild(del);

      list.appendChild(li);
    }
  }

  async function refresh() {
    await Promise.all([loadAgents(), loadProjects()]);
    // Keep the current selection if it still exists; otherwise pick the first available node.
    if (!selectionStillValid()) {
      if (state.projects[0]) state.selection = { kind: 'project', id: state.projects[0].id, name: state.projects[0].name };
      else if (state.agents[0]) state.selection = { kind: 'agent', id: state.agents[0].agentId, name: state.agents[0].agentName };
      else state.selection = null;
    }
    await loadTasks();
    renderSidebar();
    renderTasks();
  }

  function selectionStillValid() {
    if (!state.selection) return false;
    if (state.selection.kind === 'project') return state.projects.some((p) => p.id === state.selection.id);
    return state.agents.some((a) => a.agentId === state.selection.id);
  }

  // --- Actions ---------------------------------------------------------------

  async function selectNode(kind, id, name) {
    state.selection = { kind, id, name };
    await loadTasks();
    renderSidebar();
    renderTasks();
  }

  async function createProject() {
    const name = window.prompt('Project name');
    if (!name) return;
    const p = await api('/projects', {
      method: 'POST',
      body: JSON.stringify({ name, description: '', color: null }),
    });
    if (p) {
      state.selection = { kind: 'project', id: p.id, name: p.name };
      await refresh();
      toast('success', `Created "${p.name}"`);
    }
  }

  async function deleteProject(id, name) {
    if (!window.confirm(`Delete project "${name}" and all its tasks?`)) return;
    await api(`/projects/${id}`, { method: 'DELETE' });
    if (state.selection && state.selection.kind === 'project' && state.selection.id === id) {
      state.selection = null;
    }
    await refresh();
    toast('success', `Deleted "${name}"`);
  }

  async function addTask(ev) {
    ev.preventDefault();
    const input = document.getElementById('new-task-title');
    const title = input.value.trim();
    if (!title || !state.selection) return;
    input.value = '';
    const body = { title, description: '', assignee: null, due: null };
    if (state.selection.kind === 'project') body.projectId = state.selection.id;
    else { body.agentId = state.selection.id; body.agentName = state.selection.name; }
    await api('/tasks', { method: 'POST', body: JSON.stringify(body) });
    await loadTasks();
    renderTasks();
  }

  async function toggleTask(t) {
    const nextStatus = t.status === 'done' ? 'todo' : 'done';
    await api(`/tasks/${t.id}`, {
      method: 'PATCH',
      body: JSON.stringify({ status: nextStatus }),
    });
    await loadTasks();
    renderTasks();
  }

  async function deleteTask(t) {
    if (!window.confirm(`Delete "${t.title}"?`)) return;
    await api(`/tasks/${t.id}`, { method: 'DELETE' });
    await loadTasks();
    renderTasks();
  }

  function setStatusFilter(value) {
    state.statusFilter = value;
    for (const btn of document.querySelectorAll('.status-btn')) {
      btn.classList.toggle('active', btn.dataset.status === value);
    }
    loadTasks().then(renderTasks);
  }

  // --- Wire up ---------------------------------------------------------------

  document.getElementById('new-project').addEventListener('click', createProject);
  document.getElementById('new-task-form').addEventListener('submit', addTask);
  for (const btn of document.querySelectorAll('.status-btn')) {
    btn.addEventListener('click', () => setStatusFilter(btn.dataset.status));
  }

  sendReady();
})();
