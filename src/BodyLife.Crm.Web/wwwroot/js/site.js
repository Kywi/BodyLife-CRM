document.addEventListener("submit", (event) => {
  const form = event.target;

  if (!(form instanceof HTMLFormElement) || !form.matches("[data-busy-form]")) {
    return;
  }

  const confirmation = form.dataset.confirm;
  if (confirmation && !window.confirm(confirmation)) {
    event.preventDefault();
    return;
  }

  for (const button of form.querySelectorAll("button[type='submit']")) {
    button.disabled = true;
    button.setAttribute("aria-busy", "true");

    if (button.dataset.busyText) {
      button.textContent = button.dataset.busyText;
    }
  }
});
