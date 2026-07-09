# Local development

## PostgreSQL in Docker

BodyLife CRM uses PostgreSQL for local development and integration checks. The Docker setup is for local/dev/test parity only; production should still use managed PostgreSQL with documented backup retention and a restore rehearsal.

Start the local database:

```bash
./scripts/dev-postgres.sh up
```

The default container exposes PostgreSQL on `localhost:55432`. Using a nonstandard host port avoids collisions with developer machines that already have PostgreSQL on `5432`.

```text
Database: bodylife_crm_dev
Username: bodylife
Password: bodylife_dev_password
```

The web app's Development settings already point `ConnectionStrings:BodyLife` at this database. The infrastructure test harness uses `ConnectionStrings:BodyLifeTestAdmin` to create disposable test databases during validation.

Run the shared validation gate:

```bash
./scripts/validate.sh
```

Useful local database commands:

```bash
./scripts/dev-postgres.sh status
./scripts/dev-postgres.sh logs
./scripts/dev-postgres.sh down
./scripts/dev-postgres.sh reset
```

`reset` deletes the local Docker volume. Use it only for disposable development data.

If port `55432` is already taken, start the container on another host port and override the app/test connection strings for that shell:

```bash
BODYLIFE_POSTGRES_PORT=55433 ./scripts/dev-postgres.sh up
export ConnectionStrings__BodyLife="Host=localhost;Port=55433;Database=bodylife_crm_dev;Username=bodylife;Password=bodylife_dev_password"
export BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING="Host=localhost;Port=55433;Database=postgres;Username=bodylife;Password=bodylife_dev_password"
```
