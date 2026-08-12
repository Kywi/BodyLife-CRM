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

const drawer = document.querySelector("[data-app-drawer]");
const drawerToggle = document.querySelector("[data-drawer-toggle]");
const drawerScrim = document.querySelector("[data-drawer-scrim]");
const drawerClose = document.querySelector("[data-drawer-close]");
const appMainColumn = document.querySelector(".app-main-column");
let drawerReturnFocus = null;

const drawerFocusableSelector = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "summary",
  "textarea:not([disabled])",
  "[tabindex]:not([tabindex='-1'])",
].join(",");

const getDrawerFocusables = () => {
  if (!(drawer instanceof HTMLElement)) {
    return [];
  }

  return Array.from(drawer.querySelectorAll(drawerFocusableSelector))
    .filter((element) => {
      if (!(element instanceof HTMLElement)
        || element.closest("[hidden]")
        || element.getClientRects().length === 0) {
        return false;
      }

      const closedDetails = element.closest("details:not([open])");
      return !(closedDetails instanceof HTMLDetailsElement)
        || element === closedDetails.querySelector(":scope > summary");
    });
};

const isDrawerViewport = () => window.matchMedia("(max-width: 1099px)").matches;

const setDrawerState = (open) => {
  if (!(drawer instanceof HTMLElement)
    || !(drawerToggle instanceof HTMLButtonElement)
    || !(drawerScrim instanceof HTMLElement)) {
    return;
  }

  const useDrawer = isDrawerViewport();
  const isOpen = useDrawer && open;
  drawer.classList.toggle("is-open", isOpen);
  drawerToggle.setAttribute("aria-expanded", String(isOpen));
  drawerScrim.classList.toggle("is-open", isOpen);
  drawerScrim.hidden = !isOpen;
  drawer.inert = useDrawer && !isOpen;
  if (appMainColumn instanceof HTMLElement) {
    appMainColumn.inert = isOpen;
  }
  document.body.classList.toggle("drawer-open", isOpen);
};

const closeDrawer = (restoreFocus) => {
  setDrawerState(false);
  if (restoreFocus && drawerReturnFocus instanceof HTMLElement) {
    drawerReturnFocus.focus();
  }
};

if (drawerToggle instanceof HTMLButtonElement) {
  drawerToggle.addEventListener("click", () => {
    const willOpen = drawerToggle.getAttribute("aria-expanded") !== "true";
    if (willOpen) {
      drawerReturnFocus = drawerToggle;
      setDrawerState(true);
      getDrawerFocusables()[0]?.focus();
    } else {
      closeDrawer(true);
    }
  });
}

drawerScrim?.addEventListener("click", () => closeDrawer(true));
drawerClose?.addEventListener("click", () => closeDrawer(true));

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && drawerToggle?.getAttribute("aria-expanded") === "true") {
    event.preventDefault();
    closeDrawer(true);
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key !== "Tab"
    || drawerToggle?.getAttribute("aria-expanded") !== "true"
    || !(drawer instanceof HTMLElement)) {
    return;
  }

  const focusables = getDrawerFocusables();
  if (focusables.length === 0) {
    event.preventDefault();
    return;
  }

  const first = focusables[0];
  const last = focusables[focusables.length - 1];
  const activeElement = document.activeElement;

  if (event.shiftKey && (activeElement === first || !drawer.contains(activeElement))) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && (activeElement === last || !drawer.contains(activeElement))) {
    event.preventDefault();
    first.focus();
  }
});

window.addEventListener("resize", () => setDrawerState(false));
setDrawerState(false);

const accountMenus = document.querySelectorAll("details[data-account-menu]");

document.addEventListener("click", (event) => {
  for (const menu of accountMenus) {
    if (menu instanceof HTMLDetailsElement && !menu.contains(event.target)) {
      menu.open = false;
    }
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape"
    || drawerToggle?.getAttribute("aria-expanded") === "true") {
    return;
  }

  for (const menu of accountMenus) {
    if (!(menu instanceof HTMLDetailsElement) || !menu.open) {
      continue;
    }

    event.preventDefault();
    menu.open = false;
    menu.querySelector("summary")?.focus();
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

    if (form.matches("[data-correct-payment-form]")) {
      syncCorrectPaymentForm(form);
    }

    if (form.matches("[data-non-working-day-correction-form]")) {
      syncNonWorkingDayCorrectionForm(form);
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

const syncCorrectPaymentForm = (form) => {
  const selectedMode = form.querySelector("[data-payment-correction-mode]:checked");
  const replacementFields = form.querySelector("[data-payment-replacement-fields]");
  const replacePayment = selectedMode?.dataset.paymentCorrectionMode === "replace";

  if (replacementFields instanceof HTMLElement) {
    replacementFields.dataset.active = replacePayment ? "true" : "false";
  }

  for (const input of form.querySelectorAll("[data-payment-replacement-input]")) {
    input.disabled = !replacePayment;
    input.required = replacePayment
      && input.hasAttribute("data-payment-replacement-required");
  }

  for (const button of form.querySelectorAll("[data-correct-payment-submit]")) {
    if (!button.hasAttribute("aria-busy")) {
      button.disabled = !(selectedMode instanceof HTMLInputElement);
    }
  }
};

const syncCorrectPaymentForms = (root) => {
  if (root instanceof HTMLFormElement && root.matches("[data-correct-payment-form]")) {
    syncCorrectPaymentForm(root);
  }

  for (const form of root.querySelectorAll?.("form[data-correct-payment-form]") ?? []) {
    syncCorrectPaymentForm(form);
  }
};

const syncNonWorkingDayCorrectionForm = (form) => {
  const selectedMode = form.querySelector(
    "[data-non-working-day-correction-mode]:checked");
  const replaceRange = selectedMode?.dataset.nonWorkingDayCorrectionMode
    === "replace-range";
  const hasReplacementReason = replaceRange
    || selectedMode?.dataset.nonWorkingDayCorrectionMode === "replace-reason";
  const rangeFields = form.querySelector(
    "[data-non-working-day-replacement-range]");
  const reasonFields = form.querySelector(
    "[data-non-working-day-replacement-reason]");

  if (rangeFields instanceof HTMLElement) {
    rangeFields.dataset.active = replaceRange ? "true" : "false";
  }

  if (reasonFields instanceof HTMLElement) {
    reasonFields.dataset.active = hasReplacementReason ? "true" : "false";
  }

  for (const input of form.querySelectorAll(
    "[data-non-working-day-replacement-range-input]")) {
    input.disabled = !replaceRange;
    input.required = replaceRange;
  }

  for (const input of form.querySelectorAll(
    "[data-non-working-day-replacement-reason-input]")) {
    input.disabled = !hasReplacementReason;
    input.required = hasReplacementReason
      && input.hasAttribute("data-non-working-day-replacement-reason-required");
  }

  const period = form.querySelector("[data-non-working-day-correction-period]");
  const canSubmit = selectedMode instanceof HTMLInputElement
    && period instanceof HTMLSelectElement
    && period.value !== "";
  for (const button of form.querySelectorAll(
    "[data-preview-non-working-day-correction-submit]")) {
    if (!button.hasAttribute("aria-busy")) {
      button.disabled = !canSubmit;
    }
  }
};

const syncNonWorkingDayCorrectionForms = (root) => {
  if (root instanceof HTMLFormElement
    && root.matches("[data-non-working-day-correction-form]")) {
    syncNonWorkingDayCorrectionForm(root);
  }

  for (const form of root.querySelectorAll?.(
    "form[data-non-working-day-correction-form]") ?? []) {
    syncNonWorkingDayCorrectionForm(form);
  }
};

const resetNonWorkingDayCorrectionReplacement = (form) => {
  const period = form.querySelector("[data-non-working-day-correction-period]");
  if (!(period instanceof HTMLSelectElement)) {
    return;
  }

  const selected = period.selectedOptions[0];
  if (!(selected instanceof HTMLOptionElement)) {
    return;
  }

  const values = [
    ["[data-non-working-day-replacement-start]", selected.dataset.periodStart],
    ["[data-non-working-day-replacement-end]", selected.dataset.periodEnd],
    [
      "[data-non-working-day-replacement-reason-code]",
      selected.dataset.periodReasonCode,
    ],
    [
      "[data-non-working-day-replacement-reason-comment]",
      selected.dataset.periodReasonComment,
    ],
  ];
  for (const [selector, value] of values) {
    const input = form.querySelector(selector);
    if (input instanceof HTMLInputElement
      || input instanceof HTMLTextAreaElement) {
      input.value = value ?? "";
    }
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
    || !event.target.matches("[data-non-working-day-correction-mode]")) {
    return;
  }

  const form = event.target.closest("form[data-non-working-day-correction-form]");
  if (form instanceof HTMLFormElement) {
    syncNonWorkingDayCorrectionForm(form);
  }
});

document.addEventListener("change", (event) => {
  if (!(event.target instanceof HTMLSelectElement)
    || !event.target.matches("[data-non-working-day-correction-period]")) {
    return;
  }

  const form = event.target.closest("form[data-non-working-day-correction-form]");
  if (form instanceof HTMLFormElement) {
    resetNonWorkingDayCorrectionReplacement(form);
    syncNonWorkingDayCorrectionForm(form);
  }
});

document.addEventListener("change", (event) => {
  if (!(event.target instanceof HTMLInputElement)
    || !event.target.matches("[data-payment-correction-mode]")) {
    return;
  }

  const form = event.target.closest("form[data-correct-payment-form]");
  if (form instanceof HTMLFormElement) {
    syncCorrectPaymentForm(form);
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

const profileActionWorkspaceSelector = "[data-profile-action-workspace]";

const getProfileActionWorkspaces = (root) => {
  const workspaces = new Set();

  if (root instanceof Element) {
    if (root.matches(profileActionWorkspaceSelector)) {
      workspaces.add(root);
    }

    const ancestor = root.closest(profileActionWorkspaceSelector);
    if (ancestor instanceof HTMLElement) {
      workspaces.add(ancestor);
    }
  }

  for (const workspace of root.querySelectorAll?.(profileActionWorkspaceSelector) ?? []) {
    workspaces.add(workspace);
  }

  return workspaces;
};

const activateProfileAction = (workspace, targetId) => {
  const panels = Array.from(workspace.querySelectorAll(
    ".profile-action-dock > details.profile-action-panel"));
  const selectedPanel = panels.find((panel) => panel.id === targetId) ?? panels[0];
  if (!(selectedPanel instanceof HTMLDetailsElement)) {
    return;
  }

  for (const panel of panels) {
    panel.open = panel === selectedPanel;
  }

  for (const trigger of workspace.querySelectorAll("[data-profile-action-target]")) {
    trigger.setAttribute(
      "aria-pressed",
      String(trigger.dataset.profileActionTarget === selectedPanel.id));
  }
};

const syncProfileActionWorkspace = (workspace) => {
  if (!(workspace instanceof HTMLElement)) {
    return;
  }

  const switcher = workspace.querySelector("[data-profile-action-switcher]");
  const panels = Array.from(workspace.querySelectorAll(
    ".profile-action-dock > details.profile-action-panel"));
  if (!(switcher instanceof HTMLElement) || panels.length === 0) {
    return;
  }

  const openPanel = panels.find((panel) => panel.open);
  const pressedTarget = workspace.querySelector(
    "[data-profile-action-target][aria-pressed='true']")?.dataset.profileActionTarget;
  activateProfileAction(workspace, openPanel?.id ?? pressedTarget ?? panels[0].id);
  workspace.classList.add("is-enhanced");
  switcher.hidden = false;
};

const syncProfileActionWorkspaces = (root) => {
  for (const workspace of getProfileActionWorkspaces(root)) {
    syncProfileActionWorkspace(workspace);
  }
};

document.addEventListener("click", (event) => {
  if (!(event.target instanceof Element)) {
    return;
  }

  const trigger = event.target.closest("[data-profile-action-target]");
  const workspace = trigger?.closest(profileActionWorkspaceSelector);
  if (!(trigger instanceof HTMLButtonElement) || !(workspace instanceof HTMLElement)) {
    return;
  }

  activateProfileAction(workspace, trigger.dataset.profileActionTarget);
});

document.addEventListener("htmx:load", (event) => {
  syncCardIntentForms(event.detail?.elt ?? document);
  syncMarkVisitForms(event.detail?.elt ?? document);
  syncIssueMembershipForms(event.detail?.elt ?? document);
  syncCorrectPaymentForms(event.detail?.elt ?? document);
  syncNonWorkingDayCorrectionForms(event.detail?.elt ?? document);
  syncProfileActionWorkspaces(event.detail?.elt ?? document);
});

syncCardIntentForms(document);
syncMarkVisitForms(document);
syncIssueMembershipForms(document);
syncCorrectPaymentForms(document);
syncNonWorkingDayCorrectionForms(document);
syncProfileActionWorkspaces(document);
