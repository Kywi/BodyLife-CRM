# Client search normalization contract

Status: accepted implementation contract for Milestone 3, 2026-07-10.

Sources: ADR-008, `docs/domain-model.md`, `docs/data-architecture.md`, `docs/interaction-contracts.md`, and `docs/implementation-roadmap.md` Milestone 3.

## Boundary

Clients/Search owns normalization for client identity search. Normalized values are deterministic derived values for indexes, duplicate detection and query matching. They do not replace raw card, phone or name values used for display and accountable business history.

The implementation lives in `ClientSearchNormalizer`. Commands added later must translate its argument errors into the shared `validation_failed` result rather than exposing exceptions to the UI.

## Card number

- A card remains optional at the Client workflow boundary; normalization is called only when a card value is present.
- Apply Unicode NFKC normalization, remove all Unicode whitespace and uppercase with invariant casing.
- Preserve leading zeroes and non-whitespace punctuation. `bl - 1001` becomes `BL-1001`; `00123` remains `00123`.
- Blank values and unsupported control characters are invalid when normalization is requested.
- Do not add scanner, barcode, QR, NFC or turnstile semantics. Those remain input mechanisms outside the v1 identity model.

## Phone

- Apply Unicode NFKC normalization and store ASCII digits only in the normalized value.
- Ignore ordinary formatting whitespace, parentheses, hyphens and periods. Allow at most one plus sign before the first digit.
- Require at least four digits so `phone_last4` is always defined when a normalized phone exists.
- Preserve leading zeroes and do not infer, add or remove a country code. Local and international representations remain distinct until an explicit locale policy is accepted.
- Reject alphabetic content, misplaced or repeated plus signs and other unsupported characters instead of silently deleting them.
- Derive `phone_last4` as the exact final four ASCII digits of `phone_normalized`.

## Name

- Normalize each surname, name and optional patronymic independently with Unicode NFC.
- Trim outer whitespace and collapse internal Unicode whitespace to one ASCII space.
- Canonicalize common apostrophe variants to ASCII apostrophe and common dash variants to ASCII hyphen.
- Uppercase with invariant casing and compose `normalized_full_name` in surname, name, patronymic order, omitting a blank patronymic.
- Do not transliterate, strip diacritics, reorder identity fields or add fuzzy/phonetic matching. Structured prefix search and duplicate-warning behavior are separate later steps.

## Persistence handoff

The next persistence step may map these outputs to `card_number_normalized`, `phone_normalized`, `phone_last4` and `normalized_full_name`. PostgreSQL constraints and indexes must enforce the same representations; they must not introduce a second normalization formula.
