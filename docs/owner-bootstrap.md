# Owner bootstrap procedure

This procedure creates the first BodyLife Owner account identity after migrations have been applied. It does not create passwords, tokens, default credentials or a session. Login and credential handling are a later Milestone 2 step.

## Safety rules

- Do not seed a production Owner account with hard-coded credentials.
- Do not run ad hoc SQL to create business accounts.
- Do not log connection strings, passwords, tokens or session secrets.
- Run this only against the intended environment connection string.
- Re-running the command is safe: if an Owner account already exists, the command exits successfully without creating another one.

## Local command

```bash
export BODYLIFE_BOOTSTRAP_OWNER_DISPLAY_NAME="BodyLife Owner"
./scripts/bootstrap-owner.sh
```

The script uses normal application configuration for `ConnectionStrings:BodyLife`. In local Docker development that comes from `src/BodyLife.Crm.Web/appsettings.Development.json`; in other environments set configuration explicitly before running the command.

The command returns:

- `0` when the Owner account was created or already exists.
- `64` when required bootstrap input is missing or invalid.

## What this step does not do

- It does not create a named Admin or shared Reception/Admin account.
- It does not set a password or session secret.
- It does not grant access through UI login yet.
- It does not replace the later account lifecycle, login/logout and authorization-policy work.
