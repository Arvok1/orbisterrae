# Infraestrutura local

Inicie os serviços distribuídos locais:

```powershell
docker compose -f infra/docker-compose.yml up -d
```

Depois que o PostgreSQL estiver saudável, aplique a migração inicial com a CLI
do SQLx:

```powershell
$env:DATABASE_URL = "postgres://orbis:orbis@127.0.0.1:5432/orbis?sslmode=disable"
sqlx migrate run --source infra/migrations
```

Para encerrar os serviços sem apagar o volume de desenvolvimento:

```powershell
docker compose -f infra/docker-compose.yml down
```
