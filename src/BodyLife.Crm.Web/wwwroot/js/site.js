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

    if (form.matches("[data-mark-visit-form]")) {
      syncMarkVisitForm(form);
    }

    if (form.matches("[data-issue-membership-form]")) {
      syncIssueMembershipForm(form);
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

const syncMarkVisitForm = (form) => {
  const selectedKind = form.querySelector("[data-visit-kind]:checked");
  const selectedMembership = form.querySelector("[data-visit-membership-choice]:checked");
  const usesMembership = selectedKind?.dataset.visitKind === "membership";
  const selectableMembership = usesMembership
    && selectedMembership instanceof HTMLInputElement
    && !selectedMembership.disabled;
  const selectedMembershipId = selectableMembership
    ? selectedMembership.dataset.membershipId
    : null;

  for (const group of form.querySelectorAll("[data-visit-acknowledgements]")) {
    const isActive = selectedMembershipId !== null
      && group.dataset.visitAcknowledgements === selectedMembershipId;
    group.dataset.active = isActive ? "true" : "false";

    for (const input of group.querySelectorAll("[data-visit-acknowledgement]")) {
      input.disabled = !isActive;
      input.required = isActive;

      if (!isActive) {
        input.checked = false;
      }
    }
  }

  const canSubmit = selectedKind instanceof HTMLInputElement
    && (selectedKind.dataset.visitKind !== "membership" || selectableMembership);

  for (const button of form.querySelectorAll("[data-mark-visit-submit]")) {
    if (!button.hasAttribute("aria-busy")) {
      button.disabled = !canSubmit;
    }
  }
};

const syncMarkVisitForms = (root) => {
  if (root instanceof HTMLFormElement && root.matches("[data-mark-visit-form]")) {
    syncMarkVisitForm(root);
  }

  for (const form of root.querySelectorAll?.("form[data-mark-visit-form]") ?? []) {
    syncMarkVisitForm(form);
  }
};

const syncIssueMembershipForm = (form) => {
  const paymentToggle = form.querySelector("[data-issue-payment-toggle]");
  const paymentFields = form.querySelector("[data-issue-payment-fields]");
  const includePayment = paymentToggle instanceof HTMLInputElement
    && paymentToggle.checked;

  if (paymentFields instanceof HTMLElement) {
    paymentFields.dataset.active = includePayment ? "true" : "false";
  }

  for (const input of form.querySelectorAll("[data-issue-payment-input]")) {
    input.disabled = !includePayment;
    input.required = includePayment;
  }

  const canSubmit = form.dataset.canSubmit === "true";
  for (const button of form.querySelectorAll("[data-issue-membership-submit]")) {
    if (!button.hasAttribute("aria-busy")) {
      button.disabled = !canSubmit;
    }
  }
};

const syncIssueMembershipForms = (root) => {
  if (root instanceof HTMLFormElement && root.matches("[data-issue-membership-form]")) {
    syncIssueMembershipForm(root);
  }

  for (const form of root.querySelectorAll?.("form[data-issue-membership-form]") ?? []) {
    syncIssueMembershipForm(form);
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

document.addEventListener("change", (event) => {
  if (!(event.target instanceof HTMLInputElement)
    || !event.target.matches("[data-visit-kind], [data-visit-membership-choice]")) {
    return;
  }

  const form = event.target.closest("form[data-mark-visit-form]");
  if (!(form instanceof HTMLFormElement)) {
    return;
  }

  if (event.target.matches("[data-visit-membership-choice]")) {
    const membershipKind = form.querySelector("[data-visit-kind='membership']");
    if (membershipKind instanceof HTMLInputElement && !membershipKind.disabled) {
      membershipKind.checked = true;
    }
  } else if (event.target.dataset.visitKind !== "membership") {
    for (const membership of form.querySelectorAll("[data-visit-membership-choice]")) {
      membership.checked = false;
    }
  }

  syncMarkVisitForm(form);
});

document.addEventListener("change", (event) => {
  if (!(event.target instanceof HTMLInputElement)
    || !event.target.matches("[data-issue-payment-toggle]")) {
    return;
  }

  const form = event.target.closest("form[data-issue-membership-form]");
  if (form instanceof HTMLFormElement) {
    syncIssueMembershipForm(form);
  }
});

document.addEventListener("htmx:load", (event) => {
  syncCardIntentForms(event.detail?.elt ?? document);
  syncMarkVisitForms(event.detail?.elt ?? document);
  syncIssueMembershipForms(event.detail?.elt ?? document);
});

syncCardIntentForms(document);
syncMarkVisitForms(document);
syncIssueMembershipForms(document);
