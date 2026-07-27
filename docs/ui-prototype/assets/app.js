(() => {
  const dialog = document.querySelector('#review-dialog');
  const message = document.querySelector('#dialog-message');
  const title = document.querySelector('#dialog-title');
  const drawer = document.querySelector('#app-navigation') || document.createElement('aside');
  const toggle = document.querySelector('#nav-toggle') || document.createElement('button');
  const closeDrawer = document.querySelector('[data-drawer-close]') || document.createElement('button');
  const overlay = document.querySelector('[data-drawer-overlay]') || document.createElement('div');
  const workspace = document.querySelector('.workspace') || document.createElement('div');
  const demoBanner = document.querySelector('.demo-banner') || document.createElement('div');
  const skipLink = document.querySelector('.skip-link') || document.createElement('a');
  const smallViewport = window.matchMedia('(max-width: 1099px)');
  let opener = null;

  const showDialog = (text, heading = 'Дія недоступна в прототипі') => {
    if (!dialog || !title || !message) return;
    title.textContent = heading;
    message.textContent = text;
    dialog.showModal();
  };
  const focusableDrawerElements = () => [...drawer.querySelectorAll('a[href], button:not([disabled])')];
  const isDrawerOpen = () => document.body.classList.contains('drawer-open');
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
  };
  const openDrawer = () => {
    if (!smallViewport.matches) return;
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
})();
