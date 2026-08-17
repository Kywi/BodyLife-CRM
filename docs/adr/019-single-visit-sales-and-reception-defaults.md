# ADR-019: Single-visit sales and reception defaults

## Статус

Accepted - 2026-08-17.

## Контекст

Поточна система вже має `one_off` MembershipTypes, `one_off`/`trial` Visit
contexts і такі самі standalone Payment contexts. Проте ці факти створюються
окремими commands, не мають спільного sale id або immutable tariff snapshot і
можуть бути скасовані незалежно. Owner command contract приймає MembershipType
kind, але Owner UI не дає створити `one_off` type або призначити reception
defaults. Dedicated technical Client для пробних відвідувань також ще не має
захищеної системної identity.

Product Owner уточнив v1 workflow:

- разове відвідування продається відомому Client як одна операція з Visit і
  exact cash Payment;
- пробне продається глобально з Reception dashboard без створення профілю
  людини;
- Owner керує тарифами й окремо визначає default one-off та trial type;
- скасування Visit і Payment має бути однією пояснюваною дією, навіть якщо його
  виконали наступного дня;
- daily reports лишаються live canonical queries; refund/delta accounting і
  day-close ledger для цього workflow не потрібні.

Roadmap Milestone 6 залишав one-off/trial product polish поза scope, доки його
явно не оберуть для v1. Цим ADR він обраний як окремий Milestone 10.6 після
завершеного Milestone 10.5 і до operations readiness.

## Рішення

### Catalog and Owner defaults

- `one_off` MembershipType є tariff template для one-off, trial і наявного
  negative-closure workflow. Він не створює Issued Membership.
- Owner може створювати, редагувати й деактивувати `one_off` types через той
  самий catalog. Kind незмінний; `visits_limit = 1`; active type має positive
  price. Значення catalog для duration/visit shape не створюють Membership і не
  впливають на Memberships state.
- Owner-managed singleton settings мають два nullable references:
  `default_one_off_membership_type_id` і `trial_membership_type_id`. Обидва
  посилаються тільки на active positive-price `one_off` types; один type може
  виконувати обидві ролі.
- Відсутній default one-off не забороняє явний вибір type, але система нічого не
  обирає мовчки. Відсутній trial type вимикає trial action з явним owner-facing
  configuration message.
- Assigned default не можна деактивувати до remap/clear. Settings updates є
  Owner-only, optimistic-concurrency protected і створюють business audit.
- Price має одне source of truth у MembershipType. Catalog edit або remap
  змінює тільки майбутні sales; кожен committed sale зберігає immutable type id,
  name, price і currency snapshot.
- V1 не додає promotion engine. Акційну ціну Owner реалізує окремим type,
  catalog edit або remap; coupons, schedules і automatic discount rules не
  входять у scope.

### SingleVisitSale aggregate

- Visits owns a first-class `SingleVisitSale` source aggregate and its public
  commands. MembershipTypes provides the locked tariff/settings contract,
  Clients/Search owns the protected technical Client, Payments writes the
  exact child Payment, and Reports/Audit only read or explain the committed
  aggregate.
- Sale purpose is `one_off` or `trial`. One active aggregate links exactly one
  Visit and one cash Payment for the same Client, purpose and `occurred_at`.
  Payment amount/currency equal the server-read tariff snapshot. The aggregate
  creates no Issued Membership, Visit consumption or Memberships recalculation.
- `CreateOneOffSaleForClient` requires an ordinary Client and explicit active
  `one_off` type. Reception may preselect the Owner default, but Actor can
  change it before submit.
- `RecordTrialSale` is a global Reception action. It always uses the configured
  trial type and the system technical trial Client; Reception neither chooses
  nor creates a Client profile for the visitor.
- Both create commands are available to Owner, named Admin and the shared
  Reception/Admin account. UI visibility does not replace this server policy.
- Source aggregate, Visit, Payment, idempotency result and one aggregate
  business-audit entry commit in one PostgreSQL transaction. Commands lock and
  revalidate Client/config/type, set `recorded_at` on the server and preserve
  actor/session, correlation id, entry origin, occurred time and paper batch
  row where applicable.
- New product workflows cannot create unlinked facts: generic `MarkVisit` and
  `CreatePayment` reject every `one_off`/`trial` context outright, while generic
  `CancelVisit` and `CorrectPayment` reject a linked sale child server-side.
  Because there is no deployed database or production data, no invented
  compatibility links or historical conversion are required.

### Technical trial Client

- Clients/Search owns exactly one protected `system_trial` Client created by
  deterministic idempotent bootstrap and backed by a database uniqueness
  invariant.
- It has no card, phone or human identity and is excluded from ordinary search,
  duplicate detection, Client creation/update/deactivation, Membership issue,
  inactive-client reports and normal profile navigation.
- Only the trial-sale workflow may create Visits/Payments for it. Daily report,
  history and audit drill-downs show a clear translated trial label and links
  to sale/audit explanation rather than pretending it is a person.

### Cancellation, reports and audit

- `CancelSingleVisitSale` is reason-required and available to Owner, named Admin
  and the shared Reception/Admin account, including an older business date. It
  atomically adds linked Visit and Payment cancellation facts plus aggregate
  cancellation/audit; it never hard-deletes or partially cancels the pair.
- A future/existing reconciliation marker may label the result
  `changed_after_close`, but older date alone does not require Owner and this ADR
  adds no day-close workflow.
- No refund, cash delta or later-day compensating Payment is calculated. Live
  daily visit/cash totals for the original business date exclude both canceled
  child facts; drill-down/history/audit retain the original sale, cancellation
  time, Actor/session and reason.
- Audit uses aggregate events `single_visit_sale.created`,
  `single_visit_sale.canceled` and `single_visit_settings.updated` with related
  Visit/Payment/type/Client ids and snapshots. It must not expose two
  unexplained raw events as if Reception performed separate workflows.

### Existing negative closure boundary

- ADR-018 one-off negative closure, oldest-first allocation, correction,
  cancellation, exact Payment and snapshots remain unchanged.
- The configured default one-off type may initialize the first line only after
  Actor deliberately selects the one-off closure method. Actor can change the
  type; method and quantity are never preselected or submitted automatically.
- A current one-off sale never closes old negative Visits. Negative closure
  remains its existing explicit aggregate and command.

## Наслідки

- Reception gets one coherent paid action instead of manually pairing Visit and
  Payment.
- Trial remains fast and anonymous without weakening Client identity/search
  invariants.
- Owner can change future one-off/trial prices and defaults without rewriting
  history.
- Aggregate constraints, transaction rollback, generic-command rejection and
  report/audit cross-links add implementation work, but prevent half-created or
  half-canceled money/attendance facts.
- Free trials, anonymous one-off sales, trial lead capture/conversion, receipts,
  POS/cash-drawer integration, online payments, scheduled promotions, bundles,
  refunds/deltas, day-close ledger and generic linked-fact correction remain out
  of scope. A wrong type/amount is corrected by canceling the sale and creating
  the right one.

## Що це означає для реалізації

- Add Owner catalog kind controls and audited singleton reception settings.
- Add protected technical Client identity and exclusions.
- Add reviewable `SingleVisitSale` source schema, one-to-one links, snapshots,
  deferred commit invariants, indexes and cancellation facts to the sole
  pre-deployment baseline migration.
- Implement commands/queries with authorization, idempotency, deterministic
  locking, stale checks, canonical rereads, paper-fallback metadata and audit.
- Replace raw one-off/trial Reception forms with named-client one-off and global
  trial actions; add aggregate cancellation and report/history explanations.
- Prove PostgreSQL rollback/concurrency/constraints, snapshot immutability,
  generic bypass rejection, live report consistency, audit explanation and
  tablet/phone duplicate-submit behavior before Milestone 10.6 acceptance.

ADR-019 supplements ADR-005, ADR-006, ADR-007, ADR-010, ADR-011, ADR-012,
ADR-014, ADR-017 and ADR-018. It supersedes ADR-014 ordinary-Client trial/raw
one-off-trial product entry, ADR-012 Owner-only default for this command's
older-day cancellation, and ADR-018's no-type-preselection clause only for the
post-method default initialization described above.
