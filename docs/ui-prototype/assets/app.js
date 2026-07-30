(() => {
  const dialog = document.querySelector('#review-dialog');
  const message = document.querySelector('#dialog-message');
  const title = document.querySelector('#dialog-title');
  const drawer = document.querySelector('#app-navigation') || document.createElement('aside');
  const toggle = document.querySelector('#nav-toggle') || document.createElement('button');
  const closeDrawer = document.querySelector('[data-drawer-close]') || document.createElement('button');
  const overlay = document.querySelector('[data-drawer-overlay]') || document.createElement('div');
  const header = document.querySelector('#app-header') || document.createElement('header');
  const workspace = document.querySelector('.workspace') || document.createElement('div');
  const demoBanner = document.querySelector('.demo-banner') || document.createElement('div');
  const skipLink = document.querySelector('.skip-link') || document.createElement('a');
  const smallViewport = window.matchMedia('(max-width: 1099px)');
  let opener = null;
  let chromeSyncFrame = 0;

  const showDialog = (text, heading = 'Дія недоступна в прототипі') => {
    if (!dialog || !title || !message) return;
    title.textContent = heading;
    message.textContent = text;
    dialog.showModal();
  };
  const focusableDrawerElements = () => [...drawer.querySelectorAll('a[href], button:not([disabled])')];
  const isDrawerOpen = () => document.body.classList.contains('drawer-open');
  const accountMenus = () => [...document.querySelectorAll('.account-menu')];
  const closeAccountMenus = ({ returnFocus = false } = {}) => {
    accountMenus().forEach((menu) => {
      if (!menu.open) return;
      menu.open = false;
      if (returnFocus) menu.querySelector('.account-trigger')?.focus();
    });
  };
  const syncDesktopChrome = () => {
    chromeSyncFrame = 0;
    if (smallViewport.matches || !header.isConnected) {
      document.documentElement.style.removeProperty('--desktop-nav-top');
      return;
    }
    const headerBottom = Math.max(0, header.getBoundingClientRect().bottom);
    document.documentElement.style.setProperty('--desktop-nav-top', `${headerBottom}px`);
  };
  const queueDesktopChromeSync = () => {
    if (chromeSyncFrame) return;
    chromeSyncFrame = window.requestAnimationFrame(syncDesktopChrome);
  };
  const syncDrawer = () => {
    const isSmall = smallViewport.matches;
    document.body.classList.remove('drawer-open', 'drawer-locked', 'drawer-collapsed');
    overlay.hidden = true;
    drawer.inert = isSmall;
    if (isSmall) {
      drawer.setAttribute('aria-hidden', 'true');
      drawer.setAttribute('role', 'dialog');
      drawer.setAttribute('aria-modal', 'true');
    } else {
      drawer.removeAttribute('aria-hidden');
      drawer.removeAttribute('role');
      drawer.removeAttribute('aria-modal');
    }
    workspace.inert = false;
    demoBanner.inert = false;
    skipLink.inert = false;
    toggle.hidden = !isSmall;
    toggle.disabled = !isSmall;
    toggle.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-label', 'Відкрити навігацію');
    queueDesktopChromeSync();
  };
  const openDrawer = () => {
    if (!smallViewport.matches) return;
    closeAccountMenus();
    opener = document.activeElement;
    drawer.inert = false;
    drawer.removeAttribute('aria-hidden');
    workspace.inert = true;
    demoBanner.inert = true;
    skipLink.inert = true;
    document.body.classList.add('drawer-open', 'drawer-locked');
    overlay.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    toggle.setAttribute('aria-label', 'Закрити навігацію');
    closeDrawer.focus();
  };
  const hideDrawer = ({ returnFocus = true } = {}) => {
    if (!smallViewport.matches) return;
    document.body.classList.remove('drawer-open', 'drawer-locked');
    workspace.inert = false;
    demoBanner.inert = false;
    skipLink.inert = false;
    drawer.inert = true;
    drawer.setAttribute('aria-hidden', 'true');
    overlay.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-label', 'Відкрити навігацію');
    if (returnFocus && opener instanceof HTMLElement) opener.focus();
  };

  syncDrawer();
  smallViewport.addEventListener('change', syncDrawer);
  window.addEventListener('scroll', queueDesktopChromeSync, { passive: true });
  window.addEventListener('resize', queueDesktopChromeSync);
  if ('ResizeObserver' in window && header.isConnected) {
    new ResizeObserver(queueDesktopChromeSync).observe(header);
  }
  toggle.addEventListener('click', () => {
    if (smallViewport.matches) { isDrawerOpen() ? hideDrawer() : openDrawer(); }
  });
  closeDrawer.addEventListener('click', () => hideDrawer());
  overlay.addEventListener('click', () => hideDrawer());
  drawer.querySelectorAll('a[href]').forEach((link) => link.addEventListener('click', () => {
    if (smallViewport.matches && isDrawerOpen()) hideDrawer();
  }));
  document.addEventListener('keydown', (event) => {
    if (!smallViewport.matches || !isDrawerOpen()) return;
    if (event.key === 'Escape') { event.preventDefault(); hideDrawer(); return; }
    if (event.key !== 'Tab') return;
    const items = focusableDrawerElements();
    const first = items[0];
    const last = items.at(-1);
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  });
  document.addEventListener('click', (event) => {
    accountMenus().forEach((menu) => {
      if (menu.open && !menu.contains(event.target)) {
        menu.open = false;
      }
    });
  });
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    const openMenu = accountMenus().find((menu) => menu.open);
    if (!openMenu) return;
    event.preventDefault();
    closeAccountMenus({ returnFocus: true });
  });

  document.querySelectorAll('#close-dialog, [data-close-dialog]').forEach((button) => {
    button.addEventListener('click', () => dialog?.close());
  });
  document.querySelectorAll('[data-review-link]').forEach((element) => element.addEventListener('click', (event) => {
    event.preventDefault();
    hideDrawer();
    showDialog('Навігація та завершення сеансу тут навмисно не працюють: це статичний review-only прототип без авторизації чи бекенду.');
  }));
  document.querySelectorAll('[data-review-action]').forEach((element) => {
    if (element.tagName === 'FORM') {
      element.addEventListener('submit', (event) => event.preventDefault());
      return;
    }
    element.addEventListener('click', () => showDialog(
      element.dataset.reviewAction || 'Це лише review preview. Дані не записуються, команду не виконано і canonical reread відсутній.',
      element.dataset.reviewTitle || 'Review-only дія'
    ));
  });
  const clientSearch = document.querySelector('#client-search');
  const focusSearchContext = () => {
    if (window.location.hash === '#client-search') window.requestAnimationFrame(() => clientSearch?.focus());
  };
  window.addEventListener('hashchange', focusSearchContext);
  focusSearchContext();

  // Presentation-only fixture rows: no membership calculation, cancellation,
  // command, or delete operation exists in this review-only page.
  const clientHistoryRows = `
    <thead><tr><th scope="col">Дата й час</th><th scope="col">Візит / подія</th><th scope="col">Абонемент / контекст</th><th scope="col">Статус / джерело</th><th scope="col"><span class="sr-only">Дія</span></th></tr></thead>
    <tbody>
      <tr><td data-label="Дата й час"><time datetime="2026-07-27T09:12:00+03:00">27.07 · 09:12</time></td><td data-label="Візит / подія"><strong>Відвідування</strong><span class="visit-cell-meta"><span>12 занять · Основний</span><span>Зараховано · normal</span></span></td><td data-label="Абонемент / контекст">12 занять · Основний</td><td data-label="Статус / джерело"><span class="chip good">Зараховано</span><span class="origin">normal</span></td><td data-label="Дія"><button class="history-cancel" type="button" data-review-title="Preview скасування" data-review-action="У реальному CRM скасування зберігає цей візит в історії, потребує причини або коментаря та запускає canonical reread.">Скасувати</button></td></tr>
      <tr><td data-label="Дата й час"><time datetime="2026-07-25T18:40:00+03:00">25.07 · 18:40</time></td><td data-label="Візит / подія"><strong>Відвідування</strong><span class="visit-cell-meta"><span>12 занять · Основний</span><span>Скасовано · normal</span></span></td><td data-label="Абонемент / контекст">12 занять · Основний</td><td data-label="Статус / джерело"><span class="chip danger">Скасовано</span><span class="origin">normal</span></td><td data-label="Дія"><span class="history-kept">Збережено в історії</span></td></tr>
      <tr><td data-label="Дата й час"><time datetime="2026-07-24T12:00:00+03:00">24.07 · 12:00</time></td><td data-label="Візит / подія"><strong>Відвідування</strong><span class="visit-cell-meta"><span>12 занять · Основний</span><span>Зараховано · manual_backfill</span></span></td><td data-label="Абонемент / контекст">12 занять · Основний</td><td data-label="Статус / джерело"><span class="chip info">Зараховано</span><span class="origin">manual_backfill</span></td><td data-label="Дія"><button class="history-cancel" type="button" data-review-title="Preview скасування" data-review-action="У реальному CRM скасування зберігає цей backfill-візит в історії, потребує причини або коментаря та запускає canonical reread.">Скасувати</button></td></tr>
    </tbody>`;
  const bindReviewPreview = (element) => element.addEventListener('click', () => showDialog(
    element.dataset.reviewAction || 'Це лише review preview. Дані не записуються, команду не виконано і canonical reread відсутній.',
    element.dataset.reviewTitle || 'Review-only дія'
  ));
  document.querySelectorAll('.profile-state').forEach((profile) => {
    const actions = profile.querySelector('.actions');
    if (actions) {
      actions.classList.add('profile-action-toolbar');
      actions.setAttribute('aria-label', 'Дії з профілем клієнта');
      actions.innerHTML = '<button type="button" class="primary-button" data-review-action>Позначити відвідування</button><button type="button" data-review-action>Видати абонемент</button><button type="button" data-review-action>Додати платіж</button><button type="button" data-review-action>Додати заморозку</button>';
      actions.querySelectorAll('[data-review-action]').forEach(bindReviewPreview);
      profile.prepend(actions);
    }
    const table = profile.querySelector('.data-table');
    if (table) {
      table.classList.add('visit-history-table');
      table.innerHTML = `<caption>Останні відвідування та пов’язані події — фіктивні review-only записи</caption>${clientHistoryRows}`;
      table.querySelectorAll('[data-review-action]').forEach(bindReviewPreview);
      if (!profile.querySelector('.profile-history-head')) {
        table.closest('.table-wrap')?.insertAdjacentHTML('beforebegin', '<div class="profile-history-head"><div><p class="eyebrow">Активність клієнта</p><h3>Останні відвідування</h3></div><nav aria-label="Повна історія клієнта"><a href="./client-history.html">Історія клієнта</a><a href="./audit-timeline.html">Журнал аудиту</a></nav></div>');
      }
    }
  });
  const showCreatePreview = () => showDialog('Створити клієнта — лише візуальний review state. Жодна форма, команда або дані не існують у цьому прототипі.');
  document.querySelector('#create-client')?.addEventListener('click', showCreatePreview);
  document.querySelector('#search-form')?.addEventListener('submit', (event) => {
    event.preventDefault();
    const value = document.querySelector('#client-search').value.trim();
    const normalized = value.toLocaleLowerCase('uk-UA');
    const result = document.querySelector('#search-result');
    if (!value) { result.innerHTML = '<strong>Введіть запит для демо-стану пошуку.</strong><br><span>Підказка: Іваненко, 4821 або 099.</span>'; return; }
    if (value.replace(/\D/g, '') === '4821') {
      window.location.href = './clients.html?state=exact-card'; return;
    } else if (normalized.includes('іван') || normalized.includes('иван') || normalized.includes('099')) {
      result.innerHTML = '<div class="result-title">Кілька демо-збігів — звірте картку й телефон</div><button class="demo-result-button" type="button" data-demo-profile="Ірина Іваненко · картка 6132 · телефон ••• 12 34 · Active · 2 візити">Ірина Іваненко <span>Збіг: ПІБ · картка 6132 · телефон ••• 12 34 · Active · 2 візити</span></button><button class="demo-result-button" type="button" data-demo-profile="Ірина Іваненко · без поточної картки · телефон ••• 09 90 · абонемент завершується скоро">Ірина Іваненко <span>Збіг: ПІБ/телефон · без поточної картки · ••• 09 90 · Увага: завершується скоро</span></button>';
    } else {
      result.innerHTML = '<div class="result-title">Клієнта не знайдено</div><p class="result-copy">Уточніть ПІБ, телефон або номер картки. Якщо це новий клієнт, відкрийте демо-стан створення.</p><button class="create-client-button result-create" type="button" data-demo-create>＋ Створити клієнта</button>';
    }
  });
  document.querySelector('#search-result')?.addEventListener('click', (event) => {
    const profile = event.target.closest('[data-demo-profile]');
    if (profile) {
      showDialog(`${profile.dataset.demoProfile}. Це неперсистентний preview переходу до профілю; реальні дані не завантажуються.`, 'Демо-профіль клієнта');
      return;
    }
    if (event.target.closest('[data-demo-create]')) showCreatePreview();
  });
  const empty = document.querySelector('#activity-empty');
  document.querySelector('#activity-filter')?.addEventListener('change', (event) => {
    let visible = 0;
    document.querySelectorAll('.activity-event').forEach((row) => {
      const show = event.target.value === 'all' || row.dataset.type === event.target.value;
      row.hidden = !show; if (show) visible += 1;
    });
    empty.hidden = visible !== 0;
    if (detail) detail.hidden = true;
    if (detailContent) detailContent.textContent = '';
  });
  const detail = document.querySelector('#detail-panel');
  const detailContent = document.querySelector('#detail-content');
  document.querySelectorAll('[data-detail]').forEach((button) => button.addEventListener('click', () => {
    if (!detail || !detailContent) return;
    detailContent.textContent = button.dataset.detail;
    detail.hidden = false;
    detail.focus();
  }));
  const requestedState = new URLSearchParams(window.location.search).get('state');
  const states = (document.body.dataset.states || '').split(',').filter(Boolean);
  const defaultState = document.body.dataset.defaultState || 'default';
  const activeState = states.includes(requestedState) ? requestedState : defaultState;
  if (states.length) {
    document.body.dataset.currentState = activeState;
    const preserveStates = (document.body.dataset.preserveContextStates || '').split(',').filter(Boolean);
    const preserveDefault = document.body.dataset.preserveContext === 'true'
      && (preserveStates.length === 0 || preserveStates.includes(activeState));
    document.querySelectorAll('[data-state-panel]').forEach((panel) => {
      const isDefault = panel.dataset.statePanel === defaultState;
      panel.hidden = preserveDefault
        ? (!isDefault && panel.dataset.statePanel !== activeState)
        : panel.dataset.statePanel !== activeState;
    });
  }

  // Static Audit Log fixture: filters only its public-safe rows and never calls a backend.
  const auditForm = document.querySelector('#audit-filter-form');
  if (auditForm) {
    const auditRows = [...document.querySelectorAll('[data-audit-row]')];
    const auditDefault = document.querySelector('[data-state-panel="default"]');
    const auditLoading = document.querySelector('[data-state-panel="loading"]');
    const auditEmpty = document.querySelector('[data-state-panel="empty"]');
    const auditUnavailable = document.querySelector('[data-state-panel="unavailable"]');
    const auditCount = document.querySelector('#audit-count');
    const auditLive = document.querySelector('#audit-live');
    const auditInteractive = activeState === defaultState;
    const auditFilterLock = document.querySelector('#audit-filter-lock');
    let auditTimer = 0;
    const setAuditPanel = (panel) => [auditDefault, auditLoading, auditEmpty, auditUnavailable].forEach((item) => { if (item) item.hidden = item !== panel; });
    const setAuditRowState = (row, open) => {
      const button = row.querySelector('.audit-toggle');
      const detailRow = row.nextElementSibling;
      const label = button?.querySelector('.sr-only');
      if (!button || !detailRow) return;
      if (label && !button.dataset.closedLabel) {
        button.dataset.closedLabel = label.textContent;
        button.dataset.openLabel = label.textContent.replace(/^Показати/, 'Сховати');
      }
      button.setAttribute('aria-expanded', String(open));
      if (label) label.textContent = open ? button.dataset.openLabel : button.dataset.closedLabel;
      row.classList.toggle('is-expanded', open);
      detailRow.classList.toggle('is-open', open);
      detailRow.setAttribute('aria-hidden', String(!open));
    };
    const closeAuditRow = (row) => setAuditRowState(row, false);
    const toggleAuditRow = (row) => {
      const button = row.querySelector('.audit-toggle');
      if (!button) return;
      const open = button.getAttribute('aria-expanded') === 'true';
      auditRows.forEach((candidate) => { if (candidate !== row) closeAuditRow(candidate); });
      setAuditRowState(row, !open);
    };
    auditRows.forEach((row) => {
      row.querySelector('.audit-toggle')?.addEventListener('click', (event) => { event.stopPropagation(); toggleAuditRow(row); });
      row.addEventListener('click', (event) => { if (!event.target.closest('button,a,input,select,label')) toggleAuditRow(row); });
      closeAuditRow(row);
    });
    if (!auditInteractive) {
      [...auditForm.elements].forEach((control) => { control.disabled = true; });
      const advancedFilters = auditForm.querySelector('.audit-advanced');
      if (advancedFilters) advancedFilters.inert = true;
      if (auditFilterLock) auditFilterLock.hidden = false;
    }
    const readAuditFilters = () => Object.fromEntries(new FormData(auditForm).entries());
    const matchesAuditFilter = (row, filters) => {
      const haystack = `${row.dataset.client} ${row.dataset.clientId} ${row.dataset.entityId}`.toLocaleLowerCase('uk-UA');
      const client = (filters.client || '').trim().toLocaleLowerCase('uk-UA');
      const clientId = (filters.clientId || '').trim().toLocaleLowerCase('uk-UA');
      const entityId = (filters.entityId || '').trim().toLocaleLowerCase('uk-UA');
      return (!filters.from || row.dataset.date >= filters.from) && (!filters.to || row.dataset.date <= filters.to)
        && (!filters.action || row.dataset.action === filters.action) && (!filters.actor || row.dataset.actor === filters.actor)
        && (!filters.entityType || row.dataset.entityType === filters.entityType) && (!client || haystack.includes(client))
        && (!clientId || row.dataset.clientId.toLocaleLowerCase('uk-UA').includes(clientId))
        && (!entityId || row.dataset.entityId.toLocaleLowerCase('uk-UA').includes(entityId));
    };
    const applyAuditFilters = () => {
      if (!auditInteractive) {
        auditLive.textContent = 'Review-state зафіксовано; поверніться до звичайного стану для демо-фільтрації.';
        return;
      }
      window.clearTimeout(auditTimer); setAuditPanel(auditLoading); auditLive.textContent = 'Оновлюємо демонстраційні події…';
      auditTimer = window.setTimeout(() => {
        const filters = readAuditFilters(); let visible = 0;
        auditRows.forEach((row) => { const match = matchesAuditFilter(row, filters); row.hidden = !match; if (row.nextElementSibling) row.nextElementSibling.hidden = !match; if (!match) closeAuditRow(row); if (match) visible += 1; });
        const label = `${visible} ${visible === 1 ? 'подія' : visible < 5 ? 'події' : 'подій'}`;
        auditCount.textContent = label; auditLive.textContent = visible ? `Показано ${label}` : 'Подій за вибраними фільтрами не знайдено'; setAuditPanel(visible ? auditDefault : auditEmpty);
      }, 260);
    };
    const clearAuditFilters = () => { auditForm.reset(); applyAuditFilters(); };
    auditForm.addEventListener('submit', (event) => { event.preventDefault(); applyAuditFilters(); });
    auditForm.addEventListener('reset', () => { if (auditInteractive) window.setTimeout(applyAuditFilters, 0); });
    document.querySelectorAll('[data-audit-clear]').forEach((control) => control.addEventListener('click', (event) => {
      if (!auditInteractive) return;
      event.preventDefault();
      clearAuditFilters();
    }));
  }

  // Staff accounts is a page-scoped, non-persistent review fixture. It never
  // submits to a backend or changes the public-safe rows rendered in HTML.
  const staffCreateDialog = document.querySelector('#staff-create-dialog');
  const staffManageDialog = document.querySelector('#staff-manage-dialog');
  if (staffCreateDialog || staffManageDialog) {
    let staffOpener = null;
    const staffDialogs = [staffCreateDialog, staffManageDialog].filter(Boolean);
    const closeStaffDialog = (dialogToClose) => {
      if (!dialogToClose?.open) return;
      dialogToClose.close();
    };
    const openStaffDialog = (dialogToOpen, openerElement) => {
      if (!dialogToOpen) return;
      closeAccountMenus();
      hideDrawer({ returnFocus: false });
      staffOpener = openerElement;
      document.body.classList.add('staff-dialog-open');
      dialogToOpen.showModal();
    };
    const setStaffStatus = (form, handler) => {
      const status = form.querySelector('[data-staff-status]');
      if (status) status.textContent = `Preview handler ${handler}: форму не надіслано, команду та canonical reread не виконано.`;
    };
    const activateStaffTab = (tab) => {
      const tablist = tab.closest('[role="tablist"]');
      if (!tablist) return;
      [...tablist.querySelectorAll('[role="tab"]')].forEach((candidate) => {
        const selected = candidate === tab;
        candidate.setAttribute('aria-selected', String(selected));
        candidate.tabIndex = selected ? 0 : -1;
        const panel = document.querySelector(`#${candidate.getAttribute('aria-controls')}`);
        if (panel) panel.hidden = !selected;
      });
      tab.focus();
    };
    const populateStaffDialog = (row) => {
      const data = row.dataset;
      const active = data.isActive === 'true';
      const hasCredentials = data.hasCredentials === 'true';
      staffManageDialog.querySelectorAll('form').forEach((form) => form.reset());
      staffManageDialog.querySelector('[data-staff-title]').textContent = data.displayName;
      staffManageDialog.querySelector('[data-staff-summary]').textContent = `${data.accountKind} · ${active ? 'Активний' : 'Деактивований'} · активних сесій: ${data.sessions}`;
      staffManageDialog.querySelectorAll('[data-staff-account-id]').forEach((input) => { input.value = data.accountId; });
      staffManageDialog.querySelectorAll('[data-staff-is-active]').forEach((input) => { input.value = String(active); });
      staffManageDialog.querySelector('#staff-display-name').value = data.displayName;
      staffManageDialog.querySelector('#staff-login-name').value = data.loginName;
      staffManageDialog.querySelector('[data-staff-login]').textContent = data.loginName || 'Не налаштовано';
      staffManageDialog.querySelector('[data-staff-kind]').textContent = data.accountKind;
      staffManageDialog.querySelector('[data-staff-credentials-tab]').textContent = hasCredentials ? 'Скидання пароля' : 'Налаштування входу';
      staffManageDialog.querySelector('[data-staff-credentials-title]').textContent = hasCredentials ? 'Скидання пароля' : 'Налаштування входу';
      staffManageDialog.querySelector('[data-staff-credentials-submit]').textContent = hasCredentials ? 'Скинути облікові дані' : 'Задати облікові дані';
      staffManageDialog.querySelector('[data-staff-credentials-help]').textContent = hasCredentials
        ? 'Після скидання активні сесії завершаться. Секрети не показуються й не зберігаються у цьому preview.'
        : 'Первинне налаштування створить логін і пароль без причини reset. Секрети не зберігаються у цьому preview.';
      const resetReasonField = staffManageDialog.querySelector('[data-staff-reset-reason-field]');
      const resetReason = staffManageDialog.querySelector('#staff-reset-reason');
      resetReasonField.hidden = !hasCredentials;
      resetReason.required = hasCredentials;
      staffManageDialog.querySelector('[data-staff-deactivate-confirm-text]').textContent = `Підтверджую деактивацію та завершення ${data.sessions} активних сесій.`;
      staffManageDialog.querySelector('[data-staff-shared-explanation]').hidden = data.accountKindCode !== 'SharedReceptionAdmin';
      staffManageDialog.querySelector('.staff-deactivate').hidden = !active;
      staffManageDialog.querySelector('.staff-activate').hidden = active;
      staffManageDialog.querySelectorAll('[data-staff-status]').forEach((status) => { status.textContent = ''; });
      activateStaffTab(staffManageDialog.querySelector('#staff-tab-profile'));
    };

    document.querySelector('[data-staff-create]')?.addEventListener('click', (event) => {
      staffCreateDialog.querySelector('[data-staff-status]').textContent = '';
      staffCreateDialog.querySelector('form').reset();
      openStaffDialog(staffCreateDialog, event.currentTarget);
      staffCreateDialog.querySelector('#staff-create-kind').focus();
    });
    document.querySelectorAll('[data-staff-manage]').forEach((button) => button.addEventListener('click', (event) => {
      populateStaffDialog(event.currentTarget.closest('[data-staff-row]'));
      openStaffDialog(staffManageDialog, event.currentTarget);
    }));
    document.querySelectorAll('[data-staff-close]').forEach((button) => button.addEventListener('click', () => closeStaffDialog(button.closest('dialog'))));
    staffDialogs.forEach((staffDialog) => {
      staffDialog.addEventListener('click', (event) => { if (event.target === staffDialog) closeStaffDialog(staffDialog); });
      staffDialog.addEventListener('close', () => {
        staffDialog.querySelectorAll('form').forEach((form) => form.reset());
        staffDialog.querySelectorAll('[data-staff-status]').forEach((status) => { status.textContent = ''; });
        document.body.classList.remove('staff-dialog-open');
        if (staffOpener instanceof HTMLElement) staffOpener.focus();
        staffOpener = null;
      });
    });
    staffManageDialog?.querySelectorAll('[role="tab"]').forEach((tab) => tab.addEventListener('click', () => activateStaffTab(tab)));
    staffManageDialog?.querySelector('[role="tablist"]')?.addEventListener('keydown', (event) => {
      const tabs = [...event.currentTarget.querySelectorAll('[role="tab"]')];
      const current = tabs.indexOf(document.activeElement);
      if (current < 0 || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
      activateStaffTab(tabs[next]);
    });
    document.querySelectorAll('[data-staff-form]').forEach((form) => form.addEventListener('submit', (event) => {
      event.preventDefault();
      setStaffStatus(form, form.dataset.backendHandler);
    }));
  }

  // Non-working days is a guarded, page-scoped static parity fixture. It only
  // switches presentation state: all figures stay in server-snapshot HTML.
  const nwdPage = document.querySelector('[data-nwd-page]');
  if (nwdPage) {
    const tabs = [...nwdPage.querySelectorAll('.nwd-tabs [role="tab"]')];
    const planPanel = nwdPage.querySelector('#nwd-plan-panel');
    const historyPanel = nwdPage.querySelector('#nwd-history-panel');
    const correctionRow = nwdPage.querySelector('#nwd-correction-row');
    const correctionToggle = nwdPage.querySelector('.nwd-correction-toggle');
    const activePeriod = nwdPage.querySelector('.nwd-active-period');
    const query = new URLSearchParams(window.location.search);
    const correctionState = ['correction', 'correction-confirmed'].includes(query.get('state'));
    const showHistory = query.get('tab') === 'history' || correctionState;
    const activateNwdTab = (tab, focus = false) => {
      const history = tab.id === 'nwd-history-tab';
      tabs.forEach((candidate) => {
        const selected = candidate === tab;
        candidate.setAttribute('aria-selected', String(selected));
        candidate.tabIndex = selected ? 0 : -1;
      });
      planPanel.hidden = history;
      historyPanel.hidden = !history;
      if (focus) tab.focus();
    };
    tabs.forEach((tab) => tab.addEventListener('click', () => activateNwdTab(tab)));
    nwdPage.querySelector('.nwd-tabs')?.addEventListener('keydown', (event) => {
      const current = tabs.indexOf(document.activeElement);
      if (current < 0 || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
      activateNwdTab(tabs[next], true);
    });
    activateNwdTab(nwdPage.querySelector(showHistory ? '#nwd-history-tab' : '#nwd-plan-tab'));
    const setCorrectionOpen = (open) => {
      if (correctionRow) {
        correctionRow.hidden = !open;
        correctionRow.classList.toggle('is-open', open);
      }
      correctionToggle?.setAttribute('aria-expanded', String(open));
      activePeriod?.classList.toggle('is-open', open);
    };
    setCorrectionOpen(correctionState);
    correctionToggle?.addEventListener('click', () => {
      const expanded = correctionToggle.getAttribute('aria-expanded') === 'true';
      setCorrectionOpen(!expanded);
    });

    const planForm = nwdPage.querySelector('.nwd-plan-form');
    const confirmation = nwdPage.querySelector('#nwd-confirmed');
    const confirmButton = nwdPage.querySelector('.nwd-confirm-form button[type="submit"]');
    const confirmationBlocked = ['expired-token', 'scope-changed'].includes(query.get('state'));
    let planSnapshotDirty = false;
    const setPlanConfirmationBlocked = (blocked, message = '') => {
      if (confirmation) {
        confirmation.checked = false;
        confirmation.disabled = blocked;
      }
      if (confirmButton) confirmButton.disabled = blocked || !confirmation?.checked;
      if (message) {
        const status = nwdPage.querySelector('[data-nwd-confirm-status]');
        if (status) status.textContent = message;
      }
    };
    setPlanConfirmationBlocked(confirmationBlocked);
    confirmation?.addEventListener('change', () => {
      if (confirmButton) confirmButton.disabled = confirmationBlocked || planSnapshotDirty || !confirmation.checked;
    });
    planForm?.addEventListener('input', () => {
      if (confirmationBlocked) return;
      planSnapshotDirty = true;
      setPlanConfirmationBlocked(true, 'Параметри змінено — snapshot застарів. Потрібен новий серверний preview.');
    });
    planForm?.addEventListener('submit', (event) => {
      event.preventDefault();
      const status = nwdPage.querySelector('[data-nwd-preview-status]');
      const values = new FormData(planForm);
      const matchesFixture = values.get('form.ProposedStartDate') === '2026-08-24'
        && values.get('form.ProposedEndDate') === '2026-08-25'
        && values.get('form.ReasonCode') === 'maintenance'
        && values.get('form.ReasonComment') === 'Оновлення вентиляції';
      if (matchesFixture && !confirmationBlocked) {
        planSnapshotDirty = false;
        setPlanConfirmationBlocked(false, 'Демо-snapshot 24–25.08 відновлено; жодної серверної команди не виконано.');
        if (status) status.textContent = 'Preview handler Preview: показано збережений server fixture без розрахунків у браузері.';
      } else {
        planSnapshotDirty = true;
        setPlanConfirmationBlocked(true, 'Статичний lab не обчислює новий scope. Поверніть демо-параметри або відкрийте valid fixture.');
        if (status) status.textContent = 'Preview handler Preview потребує сервера; старий snapshot більше не можна підтвердити.';
      }
    });
    nwdPage.querySelector('.nwd-confirm-form')?.addEventListener('submit', (event) => {
      event.preventDefault();
      const status = nwdPage.querySelector('[data-nwd-confirm-status]');
      if (status) status.textContent = 'Confirm handler Confirm: review-only; команда, audit і canonical reread не виконані.';
    });

    const correctionForm = nwdPage.querySelector('[data-nwd-correction-form]');
    const rangeFields = nwdPage.querySelector('[data-nwd-replace-range]');
    const reasonFields = nwdPage.querySelector('[data-nwd-replace-reason]');
    const correctionPreviews = [...nwdPage.querySelectorAll('[data-nwd-correction-preview-mode]')];
    const correctionConfirmForms = [...nwdPage.querySelectorAll('[data-nwd-correction-confirm]')];
    correctionConfirmForms.forEach((form) => {
      const checkbox = form.querySelector('input[name="form.Confirmed"]');
      const button = form.querySelector('button[type="submit"]');
      checkbox?.addEventListener('change', () => {
        if (button) button.disabled = form.dataset.stale === 'true' || !checkbox.checked;
      });
      form.addEventListener('submit', (event) => {
        event.preventDefault();
        const status = form.parentElement?.querySelector('[data-nwd-correction-confirm-status]');
        if (status) status.textContent = 'CorrectionConfirm: review-only; source, audit і canonical reread не змінені.';
      });
    });
    const setCorrectionPreviewStale = () => {
      const mode = correctionForm?.querySelector('input[name="form.Mode"]:checked')?.value;
      const preview = correctionPreviews.find((item) => item.dataset.nwdCorrectionPreviewMode === mode);
      const form = preview?.querySelector('[data-nwd-correction-confirm]');
      const checkbox = form?.querySelector('input[name="form.Confirmed"]');
      const button = form?.querySelector('button[type="submit"]');
      if (form) form.dataset.stale = 'true';
      if (checkbox) {
        checkbox.checked = false;
        checkbox.disabled = true;
      }
      if (button) button.disabled = true;
      const status = nwdPage.querySelector('[data-nwd-correction-status]');
      if (status) status.textContent = 'Параметри correction змінено — confirmation заблоковано до нового серверного preview.';
    };
    const updateCorrectionFields = () => {
      const mode = correctionForm?.querySelector('input[name="form.Mode"]:checked')?.value;
      const rangeActive = mode === 'ReplaceRange';
      const reasonActive = mode !== 'Cancel';
      const replacementStart = correctionForm?.querySelector('input[name="form.ReplacementStartDate"]');
      const replacementEnd = correctionForm?.querySelector('input[name="form.ReplacementEndDate"]');
      const replacementCode = correctionForm?.querySelector('input[name="form.ReplacementReasonCode"]');
      const replacementComment = correctionForm?.querySelector('input[name="form.ReplacementReasonComment"]');
      const correctionReason = correctionForm?.querySelector('input[name="form.CorrectionReason"]');
      const correctionComment = correctionForm?.querySelector('input[name="form.CorrectionComment"]');
      const fixtureValues = mode === 'ReplaceReason'
        ? ['holiday', 'Святковий графік', 'Уточнено причину', 'За наказом власника']
        : mode === 'Cancel'
          ? ['', '', 'Закриття не відбулося', 'Зал працював за звичайним графіком']
          : ['maintenance', 'Роботи подовжено', 'Уточнено тривалість', 'За листом підрядника'];
      if (replacementStart) replacementStart.value = '2026-08-26';
      if (replacementEnd) replacementEnd.value = '2026-08-28';
      if (replacementCode) replacementCode.value = fixtureValues[0];
      if (replacementComment) replacementComment.value = fixtureValues[1];
      if (correctionReason) correctionReason.value = fixtureValues[2];
      if (correctionComment) correctionComment.value = fixtureValues[3];
      if (rangeFields) rangeFields.hidden = !rangeActive;
      if (reasonFields) reasonFields.hidden = !reasonActive;
      rangeFields?.querySelectorAll('input').forEach((input) => {
        input.disabled = !rangeActive;
        input.required = rangeActive;
      });
      reasonFields?.querySelectorAll('input').forEach((input) => {
        input.disabled = !reasonActive;
        input.required = reasonActive && input.name === 'form.ReplacementReasonCode';
      });
      correctionPreviews.forEach((preview) => {
        preview.hidden = preview.dataset.nwdCorrectionPreviewMode !== mode;
      });
      correctionConfirmForms.forEach((form) => {
        const checkbox = form.querySelector('input[name="form.Confirmed"]');
        const button = form.querySelector('button[type="submit"]');
        form.dataset.stale = 'false';
        if (checkbox) {
          checkbox.checked = false;
          checkbox.disabled = false;
        }
        if (button) button.disabled = true;
      });
    };
    correctionForm?.querySelectorAll('input[name="form.Mode"]').forEach((control) => control.addEventListener('change', updateCorrectionFields));
    correctionForm?.addEventListener('input', (event) => {
      if (event.target.name !== 'form.Mode') setCorrectionPreviewStale();
    });
    correctionForm?.addEventListener('submit', (event) => {
      event.preventDefault();
      const status = nwdPage.querySelector('[data-nwd-correction-status]');
      if (status) status.textContent = 'CorrectionPreview потребує сервера; fixture нижче не перераховано, confirmation лишається заблокованим після змін.';
    });
    updateCorrectionFields();
  }
})();
