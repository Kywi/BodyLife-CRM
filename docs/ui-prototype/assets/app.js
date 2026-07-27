(() => {
  const dialog = document.querySelector('#review-dialog');
  const message = document.querySelector('#dialog-message');
  const title = document.querySelector('#dialog-title');
  const drawer = document.querySelector('#app-navigation');
  const toggle = document.querySelector('#nav-toggle');
  const closeDrawer = document.querySelector('[data-drawer-close]');
  const overlay = document.querySelector('[data-drawer-overlay]');
  const workspace = document.querySelector('.workspace');
  const demoBanner = document.querySelector('.demo-banner');
  const skipLink = document.querySelector('.skip-link');
  const smallViewport = window.matchMedia('(max-width: 960px)');
  let opener = null;

  const showDialog = (text, heading = 'Дія недоступна в прототипі') => {
    title.textContent = heading;
    message.textContent = text;
    dialog.showModal();
  };
  const focusableDrawerElements = () => [...drawer.querySelectorAll('a[href], button:not([disabled])')];
  const isDrawerOpen = () => document.body.classList.contains('drawer-open');
  const syncDrawer = () => {
    const isSmall = smallViewport.matches;
    document.body.classList.toggle('drawer-ready', isSmall);
    document.body.classList.remove('drawer-open', 'drawer-locked', 'drawer-collapsed');
    overlay.hidden = true;
    drawer.inert = isSmall;
    drawer.toggleAttribute('aria-hidden', isSmall);
    if (isSmall) {
      drawer.setAttribute('role', 'dialog');
      drawer.setAttribute('aria-modal', 'true');
    } else {
      drawer.removeAttribute('role');
      drawer.removeAttribute('aria-modal');
    }
    workspace.inert = false;
    demoBanner.inert = false;
    skipLink.inert = false;
    toggle.setAttribute('aria-expanded', String(!isSmall));
    toggle.setAttribute('aria-label', isSmall ? 'Відкрити навігацію' : 'Згорнути навігацію');
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
    if (smallViewport.matches) { isDrawerOpen() ? hideDrawer() : openDrawer(); return; }
    const collapsed = document.body.classList.toggle('drawer-collapsed');
    toggle.setAttribute('aria-expanded', String(!collapsed));
    toggle.setAttribute('aria-label', collapsed ? 'Відкрити навігацію' : 'Згорнути навігацію');
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

  document.querySelector('#close-dialog').addEventListener('click', () => dialog.close());
  document.querySelectorAll('[data-review-link]').forEach((element) => element.addEventListener('click', (event) => {
    event.preventDefault();
    hideDrawer();
    showDialog('Навігація та завершення сеансу тут навмисно не працюють: це статичний review-only прототип без авторизації чи бекенду.');
  }));
  document.querySelector('#create-client').addEventListener('click', () => showDialog('Створити клієнта — лише візуальний review state. Жодна форма, команда або дані не існують у цьому прототипі.'));
  document.querySelector('#search-form').addEventListener('submit', (event) => {
    event.preventDefault();
    const value = document.querySelector('#client-search').value.trim();
    const result = document.querySelector('#search-result');
    if (!value) { result.innerHTML = '<strong>Введіть запит для демо-стану пошуку.</strong><br><span>Підказка: Іваненко, 4821 або 099.</span>'; return; }
    if (value.replace(/\D/g, '') === '4821') {
      result.innerHTML = '<div class="result-title">Точний демо-збіг картки</div><button class="demo-result-button" type="button" data-demo-profile="Марія Іваненко · картка 4821 · Active · 8 візитів">Марія Іваненко <span>картка 4821 · Active</span></button>';
      result.querySelector('[data-demo-profile]').click();
    } else {
      result.innerHTML = '<div class="result-title">Кілька демо-збігів — оберіть клієнта</div><button class="demo-result-button" type="button" data-demo-profile="Ірина Сидоренко · Active · 2 візити">Ірина Сидоренко <span>Active · 2 візити</span></button><button class="demo-result-button" type="button" data-demo-profile="Ірина Савчук · закінчується скоро">Ірина Савчук <span>закінчується скоро</span></button>';
    }
  });
  document.querySelector('#search-result').addEventListener('click', (event) => {
    const profile = event.target.closest('[data-demo-profile]');
    if (!profile) return;
    showDialog(`${profile.dataset.demoProfile}. Це неперсистентний preview переходу до профілю; реальні дані не завантажуються.`, 'Демо-профіль клієнта');
  });
  const empty = document.querySelector('#activity-empty');
  document.querySelector('#activity-filter').addEventListener('change', (event) => {
    let visible = 0;
    document.querySelectorAll('.activity-event').forEach((row) => {
      const show = event.target.value === 'all' || row.dataset.type === event.target.value;
      row.hidden = !show; if (show) visible += 1;
    });
    empty.hidden = visible !== 0;
    detail.hidden = true;
    detailContent.textContent = '';
  });
  const detail = document.querySelector('#detail-panel');
  const detailContent = document.querySelector('#detail-content');
  document.querySelectorAll('[data-detail]').forEach((button) => button.addEventListener('click', () => {
    detailContent.textContent = button.dataset.detail;
    detail.hidden = false;
    detail.focus();
  }));
})();
