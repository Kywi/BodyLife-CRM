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
    button.dataset.idleText ??= button.textContent ?? "";
    button.disabled = true;
    button.setAttribute("aria-busy", "true");

    if (button.dataset.busyText) {
      button.textContent = button.dataset.busyText;
    }
  }
});

for (const eventName of ["htmx:responseError", "htmx:sendError", "htmx:timeout"]) {
  document.addEventListener(eventName, (event) => {
    const requestElement = event.detail?.elt;
    const form = requestElement instanceof HTMLFormElement
      ? requestElement
      : requestElement?.closest?.("form[data-busy-form]");

    if (!(form instanceof HTMLFormElement)) {
      return;
    }

    for (const button of form.querySelectorAll("button[type='submit']")) {
      button.disabled = false;
      button.removeAttribute("aria-busy");

      if (button.dataset.idleText) {
        button.textContent = button.dataset.idleText;
      }
    }
  });
}

const syncCardIntentForm = (form) => {
  const clearCardInput = form.querySelector("[data-clear-card-input]");
  const cardNumberInput = form.querySelector("[data-card-number-input]");

  if (!(clearCardInput instanceof HTMLInputElement)
    || !(cardNumberInput instanceof HTMLInputElement)) {
    return;
  }

  cardNumberInput.disabled = clearCardInput.checked;
  cardNumberInput.required = !clearCardInput.checked;
};

const syncCardIntentForms = (root) => {
  if (root instanceof HTMLFormElement && root.matches("[data-card-intent-form]")) {
    syncCardIntentForm(root);
  }

  for (const form of root.querySelectorAll?.("form[data-card-intent-form]") ?? []) {
    syncCardIntentForm(form);
  }
};

document.addEventListener("change", (event) => {
  if (!(event.target instanceof HTMLInputElement)
    || !event.target.matches("[data-clear-card-input]")) {
    return;
  }

  const form = event.target.closest("form[data-card-intent-form]");
  if (form instanceof HTMLFormElement) {
    syncCardIntentForm(form);
  }
});

document.addEventListener("htmx:load", (event) => {
  syncCardIntentForms(event.detail?.elt ?? document);
});

syncCardIntentForms(document);
