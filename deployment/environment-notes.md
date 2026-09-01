# Deployment Environment Notes

Lodestone is local/demo-ready. The Docker setup is intended for local evaluation, not
as a production hosting recipe.

## Runtime

- Runtime: .NET 8, ASP.NET Core MVC.
- Database: SQL Server.
- Background work: Hangfire with SQL Server storage.
- ML artifacts, when accepted, live under `src/Lodestone.Web/App_Data/ml`.

## Required Configuration

Use environment variables or User Secrets for sensitive values:

- `ConnectionStrings__DefaultConnection`
- `ConnectionStrings__HangfireConnection`
- `PublicUrl__BaseUrl`
- `SeedData__AdminEmail`
- `SeedData__AdminPassword`
- `Email__SmtpHost`, `Email__UserName`, `Email__Password`
- `Encryption__KeyRingPath`
- `Encryption__ApplicationName`

The default SMTP host in local container examples is `smtp.example.invalid` so a demo
does not accidentally send external mail.

## ML Runtime Artifacts

Runtime ML is consent-gated, quality-gated, and fail-closed. Set
`MachineLearning__Enabled=true` only when all three accepted artifacts are present:

- `src/Lodestone.Web/App_Data/ml/risk-model.zip`
- `src/Lodestone.Web/App_Data/ml/risk-model.metadata.json`
- `src/Lodestone.Web/App_Data/ml/risk-model.publication.json`

The app validates model hash, metadata hash, schema, feature order, window size,
stride, publication eligibility, and loadability before scoring. If validation fails,
`/health/ml` and `/health/ready` report unhealthy and risk scoring is not scheduled.

Current demo state is State B: runtime ML integration is complete, but the real v2
OULAD candidate failed the fixed quality gate, so no artifact is published.

## Data Protection

Journal notes use ASP.NET Core Data Protection. Persist and back up the configured
key ring before using real data. Losing the key ring makes encrypted notes
unrecoverable. Do not change `Encryption__ApplicationName` between deployments that
must read the same protected data.

For Docker, the SQL, Data Protection key, HTTPS certificate, and optional ML-artifact
locations are mounted as volumes. `docker compose down -v` removes those volumes and
can destroy encrypted-journal recoverability.

## Local Docker

From the repository root:

```bash
docker compose --env-file deployment/docker/.env.example -f deployment/docker/docker-compose.yml up --build
```

Before using Docker with real data, replace every placeholder secret in a private env
file. Production should use external TLS, managed secrets, least-privilege database
credentials, controlled migrations, and backed-up persistent storage.

The Hangfire dashboard is exposed at `/hangfire` only when Hangfire is enabled and is
protected by Admin authorization.
