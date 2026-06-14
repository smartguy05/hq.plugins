(function () {
  'use strict';

  const state = {
    token: null,
    orgId: null,
    apiBaseUrl: null,
    projects: [],
    tasks: [],
    currentProjectId: null,
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

  async function loadProjects() {
    state.projects = (await api('/projects')) || [];
  }

  async function loadTasks() {
    if (!state.currentProjectId) { state.tasks = []; return; }
    const qs = new URLSearchParams({ projectId: state.currentProjectId });
    if (state.statusFilter) qs.set('status', state.statusFilter);
    state.tasks = (await api(`/tasks?${qs.toString()}`)) || [];
  }

  // --- Rendering -------------------------------------------------------------

  function renderProjects() {
    const list = document.getElementById('project-list');
    list.innerHTML = '';
    for (const p of state.projects) {
      const li = document.createElement('li');
      li.textContent = p.name;
      li.dataset.id = p.id;
      if (p.id === state.currentProjectId) li.classList.add('active');
      li.addEventListener('click', () => selectProject(p.id));
      list.appendChild(li);
    }
  }

  function renderTasks() {
    const list = document.getElementById('task-list');
    list.innerHTML = '';
    const empty = document.getElementById('empty-state');

    if (!state.currentProjectId) {
      empty.hidden = false;
      empty.textContent = state.projects.length === 0
        ? 'Create a project to start tracking tasks.'
        : 'Select a project on the left.';
      document.getElementById('new-task-form').hidden = true;
      return;
    }

    empty.hidden = true;
    document.getElementById('new-task-form').hidden = false;
    const proj = state.projects.find((p) => p.id === state.currentProjectId);
    document.getElementById('current-project-name').textContent = proj ? proj.name : '';

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
    await loadProjects();
    if (!state.currentProjectId && state.projects[0]) {
      state.currentProjectId = state.projects[0].id;
    }
    await loadTasks();
    renderProjects();
    renderTasks();
  }

  // --- Actions ---------------------------------------------------------------

  async function selectProject(id) {
    state.currentProjectId = id;
    await loadTasks();
    renderProjects();
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
      state.currentProjectId = p.id;
      await refresh();
      toast('success', `Created "${p.name}"`);
    }
  }

  async function addTask(ev) {
    ev.preventDefault();
    const input = document.getElementById('new-task-title');
    const title = input.value.trim();
    if (!title || !state.currentProjectId) return;
    input.value = '';
    await api('/tasks', {
      method: 'POST',
      body: JSON.stringify({ projectId: state.currentProjectId, title, description: '', assignee: null, due: null }),
    });
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
