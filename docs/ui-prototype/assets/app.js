(() => {
  const dialog = document.querySelector('#review-dialog');
  const message = document.querySelector('#dialog-message');
  const title = document.querySelector('#dialog-title');
  const showDialog = (text, heading = 'Дія недоступна в прототипі') => {
    title.textContent = heading;
    message.textContent = text;
    dialog.showModal();
  };
  document.querySelector('#close-dialog').addEventListener('click', () => dialog.close());
  document.querySelectorAll('[data-review-link]').forEach((element) => element.addEventListener('click', (event) => {
    event.preventDefault();
    showDialog('Навігація та завершення сесії тут навмисно не працюють: це статичний review-only прототип без авторизації чи бекенду.');
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
    document.querySelectorAll('.activity-row').forEach((row) => {
      const show = event.target.value === 'all' || row.dataset.type === event.target.value;
      row.hidden = !show; if (show) visible += 1;
    });
    empty.hidden = visible !== 0;
  });
  const detail = document.querySelector('#detail-panel');
  document.querySelectorAll('[data-detail]').forEach((button) => button.addEventListener('click', () => {
    detail.textContent = button.dataset.detail;
    detail.hidden = false;
    detail.focus?.();
  }));
})();
