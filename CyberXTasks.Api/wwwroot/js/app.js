const tg = window.Telegram?.WebApp;
const screen = document.getElementById('screen');
const nav = document.getElementById('nav');
const toast = document.getElementById('toast');

let state = {
  user: null,
  dashboard: null,
  currentScreen: 'home',
  selectedTask: null,
  workers: [],
  createDraft: { priority: 'Normal', reminderMinutes: 15 },
  schedule: { weekOffset: 0, selectedDate: null, workerFilter: null }
};

const PRIORITY_LABELS = {
  Low: 'Низкий', Normal: 'Обычный', High: 'Высокий', Urgent: 'Срочный'
};

const STATUS_LABELS = {
  Pending: 'Ожидает', Accepted: 'В работе', Completed: 'Выполнено', Deleted: 'Удалено'
};

const ROLE_LABELS = {
  Worker: 'Сотрудник', Manager: 'Менеджер', Admin: 'Администратор'
};

function isManagerOrAdmin() {
  return state.user?.role === 'Manager' || state.user?.role === 'Admin';
}

function isAdmin() {
  return state.user?.role === 'Admin';
}

function isOwner() {
  return state.user?.isOwner === true;
}

function displayName(u) {
  if (!u) return '';
  return u.displayName || u.firstName || u.name || 'Пользователь';
}

function checklistProgressLabel(t) {
  if (!t.checklistProgress) return '';
  const p = t.checklistProgress;
  if (p.hasMain && p.subTotal > 0)
    return ` · доп. ${p.subDone}/${p.subTotal}${p.mainCompleted ? ' · главная ✓' : ''}`;
  return ` · ${p.done}/${p.total}`;
}

function checklistItemHtml(t, item, canToggle) {
  const mainClass = item.isMain ? ' checklist-item-main' : '';
  const label = item.isMain ? '<span class="checklist-main-badge">Главная</span>' : '';
  return `
    <button type="button" class="checklist-item ${item.isCompleted ? 'done' : ''}${mainClass}"
      ${canToggle ? `onclick="toggleChecklistItem(${t.id}, ${item.id}, ${!!item.isMain}, ${!!item.isCompleted})"` : 'disabled'}>
      <span class="checklist-box" aria-hidden="true">${item.isCompleted ? '✓' : ''}</span>
      <span class="checklist-text">${label}${esc(item.title)}</span>
    </button>
  `;
}

function checklistHtml(t, canToggle) {
  if (!t.checklist?.length) return '';
  const p = t.checklistProgress || { done: 0, total: t.checklist.length, subDone: 0, subTotal: 0, hasMain: false };
  const mainItems = t.checklist.filter(i => i.isMain);
  const subItems = t.checklist.filter(i => !i.isMain);
  const progressPct = p.subTotal > 0
    ? Math.round((p.subDone / p.subTotal) * 100)
    : Math.round((p.done / p.total) * 100);

  let body = '';
  if (mainItems.length) {
    body += `<div class="checklist-section-label">Главная задача</div>`;
    body += mainItems.map(item => checklistItemHtml(t, item, canToggle)).join('');
  }
  if (subItems.length) {
    body += `<div class="checklist-section-label" style="margin-top:${mainItems.length ? '12px' : '0'}">Дополнительно</div>`;
    body += subItems.map(item => checklistItemHtml(t, item, canToggle)).join('');
  }

  const progressText = p.hasMain && p.subTotal > 0
    ? `доп. ${p.subDone}/${p.subTotal}`
    : `${p.done}/${p.total}`;

  return `
    <div class="checklist-wrap">
      <div class="checklist-header">
        <span>Задачи</span>
        <span class="checklist-progress">${progressText}</span>
      </div>
      <div class="checklist-progress-bar"><div class="checklist-progress-fill" style="width:${progressPct}%"></div></div>
      ${body}
    </div>
  `;
}

async function api(path, options = {}) {
  const headers = {
    'Content-Type': 'application/json',
    'X-Telegram-Init-Data': tg?.initData || ''
  };
  const res = await fetch(`/api${path}`, { ...options, headers });
  const text = await res.text();
  if (!res.ok) {
    let err = {};
    try { err = text ? JSON.parse(text) : {}; } catch { /* не JSON */ }
    throw new Error(err.error || `Ошибка ${res.status}`);
  }
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function invalidateCaches() {
  state.dashboard = null;
  state.workers = [];
}

async function reloadCurrent(keepScheduleSelection = true) {
  const fn = SCREENS[state.currentScreen];
  if (!fn) return;
  if (state.currentScreen === 'schedule') await fn(keepScheduleSelection);
  else await fn();
}

async function goTo(screen, keepScheduleSelection = false) {
  state.currentScreen = screen;
  updateTabs(screen);
  const fn = SCREENS[screen];
  if (!fn) return;
  if (screen === 'schedule') await fn(keepScheduleSelection);
  else await fn();
}

async function runAction(action, { message, goto, keepSchedule, skipReload } = {}) {
  try {
    const result = await action();
    invalidateCaches();
    if (message) showToast(message);
    if (goto) await goTo(goto, keepSchedule ?? goto === 'schedule');
    else if (!skipReload) await reloadCurrent(keepSchedule ?? state.currentScreen === 'schedule');
    return result;
  } catch (e) {
    showToast(e.message || 'Ошибка');
    throw e;
  }
}

function showToast(msg) {
  toast.textContent = msg;
  toast.classList.remove('hidden');
  setTimeout(() => toast.classList.add('hidden'), 2500);
}

function badgeClass(priority) {
  return `badge badge-${priority.toLowerCase()}`;
}

function rankClass(place) {
  if (place === 1) return 'gold';
  if (place === 2) return 'silver';
  if (place === 3) return 'bronze';
  return '';
}

function progressPct(stats) {
  const total = stats.completedToday + stats.totalActive;
  return total === 0 ? 0 : Math.round((stats.completedToday / total) * 100);
}

function todayIso() {
  const d = new Date();
  return d.toISOString().slice(0, 10);
}

function formatWeekRange(start, end) {
  const s = new Date(start + 'T12:00:00');
  const e = new Date(end + 'T12:00:00');
  const fmt = (x) => x.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' });
  return `${fmt(s)} — ${fmt(e)}`;
}

// --- Screens ---

async function renderHome() {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  state.dashboard = await api('/dashboard');
  const s = state.dashboard;
  const pct = progressPct(s);

  screen.innerHTML = `
    ${!state.user.profileCompleted ? `
    <div class="card" style="border-color:rgba(232,39,42,0.5)">
      <div class="card-title">Заполните профиль</div>
      <p style="font-size:0.85rem;color:var(--text-muted);margin-bottom:12px;line-height:1.45">Укажите ФИО в настройках — менеджер будет назначать задачи по имени, а не по нику Telegram.</p>
      <button class="btn btn-primary" onclick="navigate('settings')">Указать ФИО</button>
    </div>` : ''}
    <div class="card">
      <div class="card-title">Добро пожаловать, ${esc(displayName(state.user))}</div>
      <div class="stat-grid">
        <div class="stat"><div class="stat-value">${s.pending}</div><div class="stat-label">Ожидают</div></div>
        <div class="stat"><div class="stat-value">${s.inProgress}</div><div class="stat-label">В работе</div></div>
        <div class="stat"><div class="stat-value">${s.completedToday}</div><div class="stat-label">Готово</div></div>
      </div>
      <div class="progress-wrap">
        <div class="progress-label"><span>Прогресс дня</span><span>${pct}%</span></div>
        <div class="progress-bar"><div class="progress-fill" style="width:${pct}%"></div></div>
      </div>
    </div>
    <div class="card">
      <div class="card-title">Роль</div>
      <div style="font-weight:600;color:var(--cyber-white)">${ROLE_LABELS[state.user.role] || state.user.role}</div>
    </div>
    <div class="btn-row">
      <button class="btn btn-primary" onclick="navigate('tasks')">Задачи на сегодня</button>
      <button class="btn btn-secondary" onclick="navigate('schedule')">График</button>
    </div>
    ${isManagerOrAdmin() ? '<button class="btn btn-primary" style="margin-top:10px" onclick="navigate(\'create\')">Назначить задачу сотруднику</button>' : ''}
    <button class="btn btn-secondary" style="margin-top:10px" onclick="navigate('leaderboard')">Рейтинг недели</button>
  `;
}

async function renderTasks() {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const tasks = await api('/tasks/today');

  if (!tasks.length) {
    screen.innerHTML = `<div class="empty"><div class="empty-title">Нет задач</div><p>На сегодня задач нет</p></div>`;
    return;
  }

  screen.innerHTML = `<div class="screen-title">Задачи на сегодня</div>` +
    tasks.map(t => `
      <button class="task-item" onclick="openTask(${t.id})">
        <div class="task-item-top">
          <span class="${badgeClass(t.priority)}">${PRIORITY_LABELS[t.priority]}</span>
          <span class="badge badge-status-${t.status.toLowerCase()}">${STATUS_LABELS[t.status]}</span>
        </div>
        <div class="task-title">${esc(t.title)}</div>
        <div class="task-meta">${t.dueTime ? 'До ' + t.dueTime : 'Без срока'}${checklistProgressLabel(t)}${t.assignedTo ? ' · ' + esc(displayName(t.assignedTo)) : ''}</div>
      </button>
    `).join('');
}

function taskBackLabel(screen) {
  if (screen === 'archive') return 'Архив';
  if (screen === 'schedule') return 'График';
  if (screen === 'tasks') return 'Сегодня';
  return 'Назад';
}

async function openTask(id, returnTo) {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const t = await api(`/tasks/${id}`);
  state.selectedTask = t;
  state.taskReturnScreen = returnTo || state.currentScreen || 'tasks';
  const isManager = isManagerOrAdmin();
  const isWorker = !isManager;
  const isArchived = t.status === 'Completed' || t.status === 'Deleted';

  const hasChecklist = t.checklist?.length > 0;
  const canToggleChecklist = isWorker && !isArchived && hasChecklist;

  let actions = '';
  if (isWorker && t.status === 'Pending' && !hasChecklist)
    actions += `<button class="btn btn-primary" onclick="acceptTask(${t.id})">Принять в работу</button>`;
  if (isWorker && (t.status === 'Pending' || t.status === 'Accepted') && !hasChecklist)
    actions += `<button class="btn btn-primary" onclick="showCompleteForm(${t.id})">Отметить выполненной</button>`;
  if (isManager && !isArchived) {
    actions += `<div class="btn-row">
      <button class="btn btn-secondary" onclick="editTask(${t.id})">Редактировать</button>
      <button class="btn btn-danger" onclick="deleteTask(${t.id})">Удалить</button>
    </div>`;
  }

  const completedLine = t.completedAt
    ? `<div class="task-meta">${t.status === 'Deleted' ? 'Удалено' : 'Выполнено'}: ${formatDateTime(t.completedAt)}</div>`
    : '';

  screen.innerHTML = `
    <button class="btn btn-secondary" onclick="backFromTask()" style="margin-bottom:16px;width:auto;padding:8px 16px">← ${taskBackLabel(state.taskReturnScreen)}</button>
    <div class="card">
      <span class="${badgeClass(t.priority)}">${PRIORITY_LABELS[t.priority]}</span>
      <span class="badge badge-status-${t.status.toLowerCase()}" style="margin-left:8px">${STATUS_LABELS[t.status]}</span>
      <div class="detail-title">${esc(t.title)}</div>
      ${t.description ? `<div class="task-description">${esc(t.description)}</div>` : '<div class="task-meta" style="margin-top:8px">Описание не указано</div>'}
      <div class="task-meta">Дата: ${t.scheduledDate}</div>
      <div class="task-meta">${t.dueTime ? 'Дедлайн: ' + t.dueTime : 'Без срока по времени'}</div>
      ${t.reminderMinutes ? `<div class="task-meta">Напоминание за ${t.reminderMinutes} мин до дедлайна</div>` : ''}
      ${t.assignedTo ? `<div class="task-meta">Исполнитель: ${esc(displayName(t.assignedTo))}</div>` : ''}
      ${t.createdBy ? `<div class="task-meta">Назначил: ${esc(displayName(t.createdBy))}</div>` : ''}
      ${completedLine}
      ${t.completionComment ? `<div class="task-description" style="margin-top:12px"><b>Комментарий при выполнении:</b><br>${esc(t.completionComment)}</div>` : ''}
      ${checklistHtml(t, canToggleChecklist)}
      ${hasChecklist && canToggleChecklist ? '<div class="form-hint" style="margin-top:10px">Главная выполнена → задача закрывается сразу. Или отметьте все доп. пункты — тоже закроется.</div>' : ''}
    </div>
    ${actions}
  `;
}

function backFromTask() {
  navigate(state.taskReturnScreen || 'tasks');
}

function formatDateTime(iso) {
  const d = new Date(iso);
  return d.toLocaleString('ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function showCompleteForm(id) {
  const t = state.selectedTask;
  screen.innerHTML = `
    <button class="btn btn-secondary" onclick="openTask(${id})" style="margin-bottom:16px;width:auto;padding:8px 16px">Назад</button>
    <div class="screen-title">Выполнение задачи</div>
    <div class="card">
      <div class="detail-title" style="margin-bottom:12px">${esc(t?.title || 'Задача')}</div>
      <div class="form-group">
        <label class="form-label">Комментарий (необязательно)</label>
        <textarea class="form-textarea" id="completeComment" placeholder="Что сделано, результат, замечания…"></textarea>
        <div class="form-hint">Менеджер получит этот комментарий в Telegram</div>
      </div>
      <button class="btn btn-primary" onclick="submitComplete(${id})">Подтвердить выполнение</button>
    </div>
  `;
}

async function renderLeaderboard() {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const ranks = await api('/leaderboard');

  if (!ranks.length) {
    screen.innerHTML = `<div class="empty"><div class="empty-title">Рейтинг пуст</div><p>Нет выполненных задач за неделю</p></div>`;
    return;
  }

  screen.innerHTML = `
    <div class="screen-title">Рейтинг недели</div>
    <div class="card">
      ${ranks.map(r => `
        <div class="rank-item">
          <div class="rank-place ${rankClass(r.place)}">${r.place}</div>
          <div class="rank-name">${esc(r.name)}</div>
          <div class="rank-score">${r.completedCount} задач · ${r.score} очк.</div>
        </div>
      `).join('')}
    </div>
  `;
}

async function renderSettings() {
  const u = state.user;
  screen.innerHTML = `
    <div class="screen-title">Настройки</div>
    <div class="card">
      <div class="card-title">Профиль · ФИО</div>
      <div class="form-group">
        <label class="form-label">Фамилия</label>
        <input class="form-input" id="profileLastName" value="${esc(u.lastName || '')}" placeholder="Иванов" />
      </div>
      <div class="form-group">
        <label class="form-label">Имя</label>
        <input class="form-input" id="profileFirstName" value="${esc(u.firstName || '')}" placeholder="Иван" />
      </div>
      <div class="form-group">
        <label class="form-label">Отчество (необязательно)</label>
        <input class="form-input" id="profileMiddleName" value="${esc(u.middleName || '')}" placeholder="Иванович" />
      </div>
      <div class="form-hint" style="margin-bottom:12px">Формат: Фамилия Имя Отчество. Так вас увидят при назначении задач.</div>
      <button class="btn btn-primary" onclick="saveProfile()">Сохранить профиль</button>
    </div>
    <div class="card">
      <div class="card-title">Уведомления</div>
      <div class="form-group">
        <label class="form-label">Утренняя рассылка</label>
        <input class="form-input" type="time" id="notifyTime" value="${state.user.notificationTime}" />
      </div>
      <div class="form-group">
        <label class="form-label">Напоминание до дедлайна (мин)</label>
        <select class="form-select" id="reminderMin">
          ${[5,15,30,60,120].map(m => `<option value="${m}" ${m === state.user.reminderMinutes ? 'selected' : ''}>${m} минут</option>`).join('')}
        </select>
      </div>
      <button class="btn btn-primary" onclick="saveSettings()">Сохранить</button>
    </div>
    ${isManagerOrAdmin() ? `
      <button class="btn btn-secondary" onclick="renderTeam()">Команда</button>
      <button class="btn btn-secondary" onclick="renderReport()">Еженедельный отчёт</button>
      <button class="btn btn-secondary" onclick="renderArchive()">Архив</button>
    ` : ''}
    ${isAdmin() ? `<button class="btn btn-secondary" onclick="renderAdmin()">Администрирование</button>` : ''}
  `;
}

async function renderCreate() {
  if (!isManagerOrAdmin()) { navigate('home'); return; }
  if (!state.workers.length) state.workers = await api('/workers');

  screen.innerHTML = `
    <div class="screen-title">Новая задача</div>
    <div class="card">
      <div class="form-group">
        <label class="form-label">Сотрудник</label>
        <select class="form-select" id="taskWorker">
          ${state.workers.length ? state.workers.map(w => `<option value="${w.id}">${esc(displayName(w))}</option>`).join('') : '<option value="">Нет сотрудников</option>'}
        </select>
        <div class="form-hint">Задача будет назначена выбранному сотруднику</div>
      </div>
      <div class="form-group">
        <label class="form-label">Название</label>
        <input class="form-input" id="taskTitle" placeholder="Кратко: что сделать" />
      </div>
      <div class="form-group">
        <label class="form-label">Главная задача</label>
        <input class="form-input" id="taskMain" placeholder="Например: Сдать отчёт начальству" />
        <div class="form-hint">Если выполнена — остальное не важно, задача закрывается</div>
      </div>
      <div class="form-group">
        <label class="form-label">Дополнительные пункты (с новой строки)</label>
        <textarea class="form-textarea" id="taskChecklist" placeholder="Помыть пол&#10;Почистить стол&#10;Взять пакеты"></textarea>
        <div class="form-hint">Если все доп. выполнены — задача тоже закрывается (даже без главной)</div>
      </div>
      <div class="form-group">
        <label class="form-label">Комментарий (необязательно)</label>
        <textarea class="form-textarea" id="taskDesc" placeholder="Дополнительные детали…"></textarea>
      </div>
      <div class="form-group">
        <label class="form-label">Дата выполнения</label>
        <input class="form-input" type="date" id="taskDate" value="${todayIso()}" />
      </div>
      <div class="form-group">
        <label class="form-label">Приоритет</label>
        <div class="priority-grid" id="priorityGrid">
          ${['Low','Normal','High','Urgent'].map(p => `
            <button type="button" class="priority-btn ${state.createDraft.priority === p ? 'selected' : ''}" data-p="${p}" onclick="selectPriority('${p}')">${PRIORITY_LABELS[p]}</button>
          `).join('')}
        </div>
      </div>
      <div class="form-group">
        <label class="form-label">Время (необязательно)</label>
        <input class="form-input" type="time" id="taskDue" />
      </div>
      <div class="form-group">
        <label class="form-label">Напоминание (мин до дедлайна)</label>
        <select class="form-select" id="taskRem">
          ${[5,15,30,60,120].map(m => `<option value="${m}">${m} мин</option>`).join('')}
        </select>
      </div>
      <button class="btn btn-primary" onclick="createTask()" ${!state.workers.length ? 'disabled' : ''}>Назначить задачу</button>
    </div>
  `;
}

async function renderTeam() {
  state.workers = await api('/workers');
  screen.innerHTML = `
    <div class="screen-title">Команда</div>
    <div class="card">
      ${state.workers.length ? state.workers.map(w => `
        <div class="rank-item">
          <div class="rank-name">${esc(displayName(w))}</div>
          <div class="rank-score">ID: ${w.telegramId}</div>
        </div>
      `).join('') : '<div class="empty">Нет сотрудников</div>'}
      <div class="form-group" style="margin-top:16px">
        <label class="form-label">Добавить по Telegram ID</label>
        <input class="form-input" id="newWorkerId" type="number" placeholder="123456789" />
      </div>
      <button class="btn btn-primary" onclick="addWorker()">Добавить</button>
    </div>
  `;
}

async function renderReport() {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const r = await api('/weekly-report');
  screen.innerHTML = `
    <div class="screen-title">Отчёт ${r.weekStart} — ${r.weekEnd}</div>
    <div class="card">
      <div class="stat-grid">
        <div class="stat"><div class="stat-value">${r.totalCompleted}</div><div class="stat-label">Выполнено</div></div>
        <div class="stat"><div class="stat-value">${r.totalPending}</div><div class="stat-label">Ожидают</div></div>
        <div class="stat"><div class="stat-value">${r.totalInProgress}</div><div class="stat-label">В работе</div></div>
      </div>
    </div>
    <div class="card">
      <div class="card-title">Топ сотрудников</div>
      ${r.leaderboard.length ? r.leaderboard.slice(0,5).map(x => `
        <div class="rank-item">
          <div class="rank-place ${rankClass(x.place)}">${x.place}</div>
          <div class="rank-name">${esc(x.name)}</div>
          <div class="rank-score">${x.score} очк.</div>
        </div>
      `).join('') : '<div class="empty">Нет данных</div>'}
    </div>
  `;
}

async function renderArchive() {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const items = await api('/archive');
  screen.innerHTML = `
    <button class="btn btn-secondary" onclick="navigate('settings')" style="margin-bottom:12px;width:auto;padding:8px 16px">← Настройки</button>
    <div class="screen-title">Архив</div>
    ${items.length ? items.map(t => `
      <button type="button" class="task-item" onclick="openTask(${t.id}, 'archive')">
        <div class="task-item-top">
          <span class="${badgeClass(t.priority)}">${PRIORITY_LABELS[t.priority]}</span>
          <span class="badge badge-status-${t.status.toLowerCase()}">${STATUS_LABELS[t.status]}</span>
        </div>
        <div class="task-title">${esc(t.title)}</div>
        <div class="task-meta">${t.scheduledDate}${t.assignedTo ? ' · ' + esc(displayName(t.assignedTo)) : ''}</div>
      </button>
    `).join('') : '<div class="empty"><div class="empty-title">Архив пуст</div></div>'}
  `;
}

// --- Actions ---

function checklistToggleConfirm(task, itemId, isMain, isCompleted) {
  const markingDone = !isCompleted;
  const subs = (task?.checklist || []).filter(i => !i.isMain);
  const wouldCloseAllSubs = markingDone && subs.length > 0 &&
    subs.every(i => i.id === itemId || i.isCompleted);

  if (markingDone) {
    if (isMain) {
      return confirm(
        'Главная задача выполнена?\n\nВся задача закроется сразу — остальные пункты не будут важны.'
      );
    }
    if (wouldCloseAllSubs && task.checklistProgress?.hasMain) {
      return confirm(
        'Отметить пункт выполненным?\n\nЭто последний доп. пункт — задача закроется (главная может остаться).'
      );
    }
    if (wouldCloseAllSubs) {
      return confirm(
        'Отметить пункт выполненным?\n\nЭто последний пункт — задача закроется.'
      );
    }
    return confirm('Отметить этот пункт как выполненный?');
  }

  if (isMain) {
    return confirm('Снять отметку с главной задачи?\n\nЗадача снова будет в работе.');
  }
  return confirm('Снять отметку с этого пункта?');
}

async function toggleChecklistItem(taskId, itemId, isMain, isCompleted) {
  const task = state.selectedTask;
  if (!checklistToggleConfirm(task, itemId, isMain, isCompleted)) return;

  const back = state.taskReturnScreen || state.currentScreen || 'tasks';
  await runAction(
    () => api(`/tasks/${taskId}/checklist/${itemId}/toggle`, { method: 'POST' }),
    { message: 'Обновлено', skipReload: true }
  );
  await openTask(taskId, back);
}

async function acceptTask(id) {
  const back = state.taskReturnScreen || state.currentScreen || 'tasks';
  await runAction(
    () => api(`/tasks/${id}/accept`, { method: 'POST' }),
    { message: 'Задача принята', skipReload: true }
  );
  await openTask(id, back);
}

async function submitComplete(id) {
  const comment = document.getElementById('completeComment')?.value?.trim() || null;
  const back = state.taskReturnScreen || state.currentScreen || 'tasks';
  await runAction(
    () => api(`/tasks/${id}/complete`, { method: 'POST', body: JSON.stringify({ comment }) }),
    { message: 'Задача выполнена', goto: back, keepSchedule: back === 'schedule' }
  );
}

async function deleteTask(id) {
  if (!confirm('Удалить задачу?')) return;
  const back = state.taskReturnScreen || state.currentScreen || 'tasks';
  await runAction(
    () => api(`/tasks/${id}`, { method: 'DELETE' }),
    { message: 'Удалено', goto: back, keepSchedule: back === 'schedule' }
  );
}

async function editTask(id) {
  const t = state.selectedTask || await api(`/tasks/${id}`);
  screen.innerHTML = `
    <div class="screen-title">Редактировать задачу</div>
    <div class="card">
      <div class="form-group">
        <label class="form-label">Название</label>
        <input class="form-input" id="editTitle" value="${esc(t.title)}" />
      </div>
      <div class="form-group">
        <label class="form-label">Подробное описание</label>
        <textarea class="form-textarea" id="editDesc">${esc(t.description || '')}</textarea>
      </div>
      <div class="form-group">
        <label class="form-label">Дата</label>
        <input class="form-input" type="date" id="editDate" value="${t.scheduledDate}" />
      </div>
      <div class="form-group">
        <label class="form-label">Время (пусто = без срока)</label>
        <input class="form-input" type="time" id="editDue" value="${t.dueTime || ''}" />
      </div>
      <button class="btn btn-primary" onclick="saveTaskEdit(${id})">Сохранить</button>
      <button class="btn btn-secondary" onclick="openTask(${id})">Отмена</button>
    </div>
  `;
}

async function saveTaskEdit(id) {
  const body = {
    title: document.getElementById('editTitle').value.trim(),
    description: document.getElementById('editDesc').value.trim(),
    scheduledDate: document.getElementById('editDate').value,
    dueTimeSet: true,
    dueTime: document.getElementById('editDue').value || null
  };
  const back = state.taskReturnScreen || state.currentScreen || 'tasks';
  await runAction(
    () => api(`/tasks/${id}`, { method: 'PATCH', body: JSON.stringify(body) }),
    { message: 'Сохранено', skipReload: true }
  );
  await openTask(id, back);
}

function adminUserActions(u) {
  if (u.id === state.user.id) return '';

  const owner = isOwner();
  const locked = !owner && (u.isOwner || u.role === 'Admin');
  if (locked) return '<div class="rank-score">Только главный админ</div>';

  let btns = '';
  if (u.role !== 'Worker')
    btns += `<button class="btn btn-secondary" style="padding:6px 10px;font-size:0.75rem" onclick="setRole(${u.id},'Worker')">→ Сотрудник</button>`;
  if (u.role !== 'Manager')
    btns += `<button class="btn btn-secondary" style="padding:6px 10px;font-size:0.75rem" onclick="setRole(${u.id},'Manager')">→ Менеджер</button>`;
  if (u.role !== 'Admin' && owner)
    btns += `<button class="btn btn-secondary" style="padding:6px 10px;font-size:0.75rem" onclick="setRole(${u.id},'Admin')">→ Админ</button>`;
  if (!u.isOwner)
    btns += `<button class="btn btn-secondary" style="padding:6px 10px;font-size:0.75rem" onclick="setActive(${u.id},${!u.isActive})">${u.isActive ? 'Деактивировать' : 'Активировать'}</button>`;
  return btns;
}

async function renderAdmin() {
  const users = await api('/users');
  screen.innerHTML = `
    <div class="screen-title">Администрирование</div>
    ${isOwner() ? '<p class="schedule-hint" style="margin-bottom:12px">Вы — главный администратор. Можете менять роли всех, включая админов.</p>' : ''}
    <div class="card">
      ${users.map(u => `
        <div class="rank-item" style="flex-wrap:wrap;gap:8px">
          <div>
            <div class="rank-name">${esc(displayName(u))}${u.isOwner ? ' <span class="badge badge-urgent">Главный</span>' : ''}${!u.profileCompleted ? ' <span class="badge badge-normal">без ФИО</span>' : ''}</div>
            <div class="rank-score">${ROLE_LABELS[u.role]} · ID ${u.telegramId}${u.isActive ? '' : ' · неактивен'}</div>
          </div>
          <div class="btn-row" style="margin:0;flex-wrap:wrap">${adminUserActions(u)}</div>
        </div>
      `).join('')}
    </div>
  `;
}

async function setRole(id, role) {
  const label = ROLE_LABELS[role] || role;
  if (role === 'Admin' && !confirm('Назначить администратором?')) return;
  await runAction(
    () => api(`/users/${id}/role`, { method: 'PATCH', body: JSON.stringify({ role }) }),
    { message: `Роль: ${label}`, goto: 'admin' }
  );
}

async function setActive(id, active) {
  await runAction(
    () => api(`/users/${id}/active`, { method: 'PATCH', body: JSON.stringify({ isActive: active }) }),
    { message: active ? 'Активирован' : 'Деактивирован', goto: 'admin' }
  );
}

function selectPriority(p) {
  state.createDraft.priority = p;
  document.querySelectorAll('.priority-btn').forEach(b => {
    b.classList.toggle('selected', b.dataset.p === p);
  });
}

async function createTask() {
  const title = document.getElementById('taskTitle').value.trim();
  const workerId = parseInt(document.getElementById('taskWorker').value);
  if (!workerId) { showToast('Добавьте сотрудника в команду'); return; }
  if (title.length < 2) { showToast('Введите название задачи'); return; }
  const due = document.getElementById('taskDue').value;
  const mainTask = document.getElementById('taskMain').value.trim() || null;
  const checklistRaw = document.getElementById('taskChecklist').value;
  const checklistItems = checklistRaw.split('\n').map(s => s.trim()).filter(Boolean);
  if (!mainTask && !checklistItems.length) {
    showToast('Укажите главную задачу или доп. пункты');
    return;
  }
  const body = {
    title,
    description: document.getElementById('taskDesc').value.trim() || null,
    scheduledDate: document.getElementById('taskDate').value,
    assignedToId: workerId,
    dueTime: due || null,
    priority: state.createDraft.priority,
    reminderMinutes: due ? parseInt(document.getElementById('taskRem').value) : null,
    mainTask,
    checklistItems: checklistItems.length ? checklistItems : null
  };
  await runAction(
    () => api('/tasks', { method: 'POST', body: JSON.stringify(body) }),
    { message: 'Задача назначена', goto: 'schedule' }
  );
}

function isOverdue(t, dayDate) {
  if (!t.dueTime || dayDate !== todayIso()) return false;
  const [h, m] = t.dueTime.split(':').map(Number);
  const now = new Date();
  return now.getHours() > h || (now.getHours() === h && now.getMinutes() > m);
}

function scheduleCardHtml(t, dayDate) {
  const p = (t.priority || 'normal').toLowerCase();
  const overdue = isOverdue(t, dayDate);
  return `
    <button type="button" class="schedule-card priority-${p} ${overdue ? 'schedule-card-overdue' : ''}" onclick="openTask(${t.id}, 'schedule')">
      <div class="schedule-card-time">${t.dueTime || '—'}</div>
      <div class="schedule-card-body">
        <div class="schedule-card-title">${esc(t.title)}${overdue ? ' <span class="badge badge-urgent">Просрочено</span>' : ''}</div>
        <div class="schedule-card-meta">${t.assignedTo && isManagerOrAdmin() ? esc(displayName(t.assignedTo)) + ' · ' : ''}${STATUS_LABELS[t.status]}${checklistProgressLabel(t)}</div>
        ${t.description ? `<div class="schedule-card-preview">${esc(t.description)}</div>` : ''}
      </div>
    </button>
  `;
}

async function renderSchedule(keepSelection) {
  screen.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const data = await api(`/schedule?weekOffset=${state.schedule.weekOffset}`);
  if (!keepSelection) {
    const todayDay = data.days.find(d => d.isToday);
    state.schedule.selectedDate = todayDay ? todayDay.date : data.days[0]?.date;
  }

  const selected = data.days.find(d => d.date === state.schedule.selectedDate) || data.days[0];
  let tasks = selected?.tasks || [];
  if (state.schedule.workerFilter && isManagerOrAdmin())
    tasks = tasks.filter(t => t.assignedTo?.id === state.schedule.workerFilter);

  const timed = tasks.filter(t => t.dueTime).sort((a,b) => a.dueTime.localeCompare(b.dueTime));
  const untimed = tasks.filter(t => !t.dueTime);

  let filterHtml = '';
  if (isManagerOrAdmin() && data.workers.length) {
    filterHtml = `<div class="schedule-filter">
      <button type="button" class="schedule-filter-chip ${!state.schedule.workerFilter ? 'active' : ''}" onclick="setScheduleWorker(null)">Все</button>
      ${data.workers.map(w => `
        <button type="button" class="schedule-filter-chip ${state.schedule.workerFilter === w.id ? 'active' : ''}" onclick="setScheduleWorker(${w.id})">${esc(displayName(w))}</button>
      `).join('')}
    </div>`;
  }

  screen.innerHTML = `
    <div class="schedule-header">
      <button type="button" class="schedule-nav-btn" onclick="shiftScheduleWeek(-1)">‹</button>
      <div class="schedule-week-label">${formatWeekRange(data.weekStart, data.weekEnd)}</div>
      <button type="button" class="schedule-nav-btn" onclick="shiftScheduleWeek(1)">›</button>
    </div>
    <div class="schedule-days-wrap">
      <div class="schedule-days">
        ${data.days.map(d => `
          <button type="button" class="schedule-day-pill ${d.date === state.schedule.selectedDate ? 'active' : ''} ${d.isToday ? 'today' : ''}"
            onclick="selectScheduleDay('${d.date}')">
            <div class="schedule-day-name">${d.dayLabel}</div>
            <div class="schedule-day-num">${d.dayNum}</div>
            ${d.tasks.length ? `<div class="schedule-day-count">${d.tasks.length}</div>` : ''}
          </button>
        `).join('')}
      </div>
    </div>
    ${filterHtml}
    <div class="screen-title" style="margin-bottom:10px">${isManagerOrAdmin() ? 'График команды' : 'Мой график'}</div>
    ${!isManagerOrAdmin() ? '<p class="schedule-hint">Здесь все ваши задачи на неделю — с датами, временем и описанием</p>' : ''}
    <div class="schedule-timeline">
      ${!tasks.length ? `<div class="empty"><div class="empty-title">Нет задач</div><p>${isManagerOrAdmin() ? 'На этот день задач нет' : 'На этот день вам ничего не назначено'}</p></div>` : ''}
      ${untimed.length ? `<div class="schedule-slot-label">Весь день</div>${untimed.map(t => scheduleCardHtml(t, selected?.date)).join('')}` : ''}
      ${timed.length ? `<div class="schedule-slot-label">По времени</div>${timed.map(t => scheduleCardHtml(t, selected?.date)).join('')}` : ''}
    </div>
  `;
  updateTabs('schedule');
}

function shiftScheduleWeek(delta) {
  state.schedule.weekOffset += delta;
  state.schedule.selectedDate = null;
  renderSchedule(false);
}

function selectScheduleDay(date) {
  state.schedule.selectedDate = date;
  renderSchedule(true);
}

function setScheduleWorker(id) {
  state.schedule.workerFilter = id;
  renderSchedule(true);
}

async function saveProfile() {
  const lastName = document.getElementById('profileLastName').value.trim();
  const firstName = document.getElementById('profileFirstName').value.trim();
  const middleName = document.getElementById('profileMiddleName').value.trim() || null;
  if (lastName.length < 2) { showToast('Укажите фамилию'); return; }
  if (firstName.length < 2) { showToast('Укажите имя'); return; }
  await runAction(async () => {
    const res = await api('/profile', {
      method: 'PATCH',
      body: JSON.stringify({ lastName, firstName, middleName })
    });
    if (res) Object.assign(state.user, res);
  }, { message: 'Профиль сохранён', goto: 'settings' });
}

async function saveSettings() {
  const body = {
    notificationTime: document.getElementById('notifyTime').value,
    reminderMinutes: parseInt(document.getElementById('reminderMin').value)
  };
  await runAction(async () => {
    const res = await api('/settings', { method: 'PATCH', body: JSON.stringify(body) });
    if (res) {
      state.user.notificationTime = res.notificationTime;
      state.user.reminderMinutes = res.reminderMinutes;
    }
  }, { message: 'Сохранено', goto: 'settings' });
}

async function addWorker() {
  const id = parseInt(document.getElementById('newWorkerId').value);
  if (!id) { showToast('Введите Telegram ID'); return; }
  await runAction(
    () => api('/workers', { method: 'POST', body: JSON.stringify({ telegramId: id }) }),
    { message: 'Сотрудник добавлен', goto: 'team' }
  );
}

// --- Navigation ---

const SCREENS = {
  home: renderHome,
  tasks: renderTasks,
  schedule: renderSchedule,
  leaderboard: renderLeaderboard,
  settings: renderSettings,
  create: renderCreate,
  team: renderTeam,
  archive: renderArchive,
  admin: renderAdmin
};

async function navigate(name) {
  await goTo(name, name === 'schedule');
}

function updateTabs(active) {
  document.querySelectorAll('.tab').forEach(t => {
    t.classList.toggle('active', t.dataset.screen === active);
  });
}

function buildNav() {
  const isManager = isManagerOrAdmin();
  // График — всегда в нижней панели (и у работника, и у менеджера)
  const tabs = [
    { screen: 'home', label: 'Главная', icon: '<path d="M3 10.5L12 3l9 7.5V20a1 1 0 01-1 1h-5v-6H9v6H4a1 1 0 01-1-1v-9.5z"/>' },
    { screen: 'tasks', label: 'Сегодня', icon: '<path d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4"/>' },
    { screen: 'schedule', label: 'График', icon: '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><path d="M16 2v4M8 2v4M3 10h18"/>' }
  ];
  if (isManager) {
    tabs.push({ screen: 'create', label: 'Создать', icon: '<path d="M12 5v14m-7-7h14"/>' });
  }
  tabs.push({ screen: 'settings', label: 'Настройки', icon: '<circle cx="12" cy="12" r="3"/><path d="M12 1v2m0 18v2M4.22 4.22l1.42 1.42m12.72 12.72l1.42 1.42M1 12h2m18 0h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"/>' });
  nav.innerHTML = tabs.map(t => `
    <button class="tab" data-screen="${t.screen}" onclick="navigate('${t.screen}')">
      <svg viewBox="0 0 24 24">${t.icon}</svg>
      ${t.label}
    </button>
  `).join('');
  nav.classList.remove('hidden');
}

function esc(s) {
  const d = document.createElement('div');
  d.textContent = s || '';
  return d.innerHTML;
}

async function init() {
  if (!tg?.initData) {
    screen.innerHTML = `<div class="error-screen"><h2>Откройте через Telegram</h2><p>Это приложение работает только как Telegram Mini App</p></div>`;
    return;
  }

  tg.ready();
  tg.expand();
  tg.setHeaderColor('#08080c');
  tg.setBackgroundColor('#08080c');

  try {
    state.user = await api('/me');
    buildNav();
    await navigate('home');
  } catch (e) {
    screen.innerHTML = `<div class="error-screen"><h2>Доступ ограничен</h2><p>${esc(e.message)}</p><p style="margin-top:12px;font-size:0.85rem;color:var(--text-muted)">Попросите менеджера добавить вас в систему</p></div>`;
  }
}

init();
