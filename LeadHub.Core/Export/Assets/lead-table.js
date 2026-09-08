(() => {
  'use strict';
  const key = 'instagram-leads-feedback-v3';
  const rows = [...document.querySelectorAll('tr[data-lead-id]')];
  const tabs = [...document.querySelectorAll('[role="tab"]')];
  const notice = document.getElementById('notice');
  let saved = {};
  const tell = text => { notice.textContent = text; };
  try {
    const value = JSON.parse(localStorage.getItem(key) || '{}');
    if (value && !Array.isArray(value) && typeof value === 'object') saved = value;
  } catch (_) { tell('Сохранение браузера недоступно. Используйте «Скачать отметки».'); }

  function persist() {
    try { localStorage.setItem(key, JSON.stringify(saved)); }
    catch (_) { tell('Не удалось сохранить в браузере. Скачайте отметки, чтобы не потерять изменения.'); }
  }
  function stateFor(row) { return saved[row.dataset.leadId] || {}; }
  function restore() {
    rows.forEach(row => {
      const s = stateFor(row);
      row.querySelector('.done').checked = Boolean(s.done);
      row.querySelector('.reason').value = s.reason || '';
      row.querySelector('.reason-note').value = s.note || '';
      if (typeof s.message === 'string') row.querySelector('.message').value = s.message;
      row.classList.toggle('done-row', Boolean(s.done));
    });
  }
  function update(row) {
    saved[row.dataset.leadId] = {
      lead_id: row.dataset.leadId, handle: row.dataset.handle,
      done: row.querySelector('.done').checked,
      reason: row.querySelector('.reason').value,
      note: row.querySelector('.reason-note').value,
      message: row.querySelector('.message').value,
      updated_at: new Date().toISOString()
    };
    row.classList.toggle('done-row', saved[row.dataset.leadId].done);
    persist(); filterRows();
  }
  let section = tabs.some(t => t.dataset.section === location.hash.slice(1)) ? location.hash.slice(1) : tabs[0].dataset.section;
  function filterRows() {
    const q = document.getElementById('search').value.toLowerCase().trim();
    const hide = document.getElementById('hide-done').checked;
    let visible = 0;
    rows.forEach(row => {
      row.hidden = row.dataset.section !== section || (q && !row.dataset.search.includes(q)) || (hide && row.classList.contains('done-row'));
      if (!row.hidden) visible++;
    });
    tabs.forEach(t => {
      t.setAttribute('aria-selected', String(t.dataset.section === section));
      t.tabIndex = t.dataset.section === section ? 0 : -1;
    });
    document.getElementById('table-panel').setAttribute('aria-labelledby', 'tab-' + section);
    const empty = document.getElementById('empty');
    empty.hidden = visible !== 0;
    empty.textContent = q || hide ? 'По выбранным условиям ничего нет.' : section === 'telegram_resolved' ? 'Подтверждённых Telegram-лидов пока нет. Оставшиеся контакты находятся во вкладке «Нужна проверка».' : 'В этой вкладке пока нет лидов.';
    document.getElementById('visible-count').textContent = 'Показано: ' + visible;
  }
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => { section = tab.dataset.section; location.hash = section; filterRows(); });
    tab.addEventListener('keydown', event => {
      let next;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
      if (event.key === 'Home') next = 0;
      if (event.key === 'End') next = tabs.length - 1;
      if (next !== undefined) { event.preventDefault(); tabs[next].focus(); tabs[next].click(); }
    });
  });
  window.addEventListener('hashchange', () => {
    if (tabs.some(t => t.dataset.section === location.hash.slice(1))) { section = location.hash.slice(1); filterRows(); }
  });
  rows.forEach(row => {
    row.querySelectorAll('input,select,textarea').forEach(el => el.addEventListener(el.tagName === 'TEXTAREA' ? 'input' : 'change', () => update(row)));
    row.querySelector('.copy').addEventListener('click', async event => {
      const button = event.currentTarget;
      const area = row.querySelector('.message');
      let copied = false;
      try { await navigator.clipboard.writeText(area.value); copied = true; }
      catch (_) { area.focus(); area.select(); try { copied = document.execCommand('copy'); } catch (_) {} }
      tell(copied ? 'Текст скопирован.' : 'Выделенный текст можно скопировать вручную.');
      if (copied) {
        button.textContent = 'Скопировано';
        setTimeout(() => { button.textContent = 'Скопировать текст'; }, 1200);
      }
    });
  });
  function download(name, text, type) {
    const url = URL.createObjectURL(new Blob([text], {type}));
    const a = document.createElement('a'); a.href = url; a.download = name; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  document.getElementById('export-feedback').addEventListener('click', () => {
    download('instagram-lead-feedback.json', JSON.stringify({schema_version:3, exported_at:new Date().toISOString(), rows:Object.values(saved)}, null, 2), 'application/json');
    tell('Отметки и тексты выгружены. Их можно загрузить в следующую таблицу.');
  });
  document.getElementById('import-feedback').addEventListener('change', async event => {
    const file = event.target.files[0]; if (!file) return;
    try {
      const payload = JSON.parse(await file.text());
      const values = Array.isArray(payload) ? payload : payload.rows;
      if (!Array.isArray(values)) throw new Error('Нет списка отметок');
      let count = 0;
      values.forEach(item => {
        const handle = String(item.handle || '').toLowerCase().replace(/^@/, '');
        if (!/^[a-z0-9._]+$/.test(handle)) return;
        const id = 'instagram:' + handle;
        const existing = saved[id];
        if (existing && Date.parse(existing.updated_at) > Date.parse(item.updated_at || '1970-01-01')) return;
        saved[id] = {lead_id:id, handle, done:item.done === true, reason:String(item.reason || ''), note:String(item.note || ''), updated_at:item.updated_at || new Date().toISOString()};
        if (typeof item.message === 'string') saved[id].message = item.message;
        count++;
      });
      persist(); restore(); filterRows(); tell('Загружено отметок: ' + count);
    } catch (_) { tell('Не удалось прочитать файл отметок. Выберите JSON из этой или прежней таблицы.'); }
    event.target.value = '';
  });
  function cell(value) {
    let text = String(value || '');
    if (/^[\s]*[=+@-]/.test(text)) text = "'" + text;
    return '"' + text.replaceAll('"', '""') + '"';
  }
  document.getElementById('export-csv').addEventListener('click', () => {
    const header = ['Instagram','Бизнес','Город','Ниша','Телефон','Telegram','WhatsApp','Статус WhatsApp','WhatsApp-ссылка найдена','Telegram проверен','Причина','Текст','Написал','Комментарий'];
    const data = rows.filter(r => !r.hidden).map(row => [row.dataset.handle,row.dataset.business,row.dataset.city,row.dataset.niche,row.dataset.phone,row.dataset.telegram,row.dataset.whatsapp,row.dataset.waStatus,row.dataset.waPublished,row.dataset.tgChecked,row.querySelector('.reason').value,row.querySelector('.message').value,row.querySelector('.done').checked ? 'Да' : '',row.querySelector('.reason-note').value]);
    download('instagram-' + section + '.csv', '\ufeff' + [header,...data].map(r => r.map(cell).join(';')).join('\r\n'), 'text/csv;charset=utf-8');
    tell('Выгружены видимые строки выбранной вкладки: ' + data.length);
  });
  document.getElementById('search').addEventListener('input', filterRows);
  document.getElementById('hide-done').addEventListener('change', filterRows);
  restore(); filterRows();
})();
