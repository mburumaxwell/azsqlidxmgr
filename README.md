# azsqlidxmgr – Azure SQL Index Maintenance CLI Tool

[![NuGet](https://img.shields.io/nuget/v/azsqlidxmgr.svg)](https://www.nuget.org/packages/azsqlidxmgr/)
[![GitHub Workflow Status](https://github.com/mburumaxwell/azsqlidxmgr/actions/workflows/build.yml/badge.svg)](https://github.com/mburumaxwell/azsqlidxmgr/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/release/mburumaxwell/azsqlidxmgr.svg)](https://github.com/mburumaxwell/azsqlidxmgr/releases/latest)
[![License](https://img.shields.io/github/license/mburumaxwell/azsqlidxmgr.svg)](LICENSE)

A cross-platform .NET global tool that automates index maintenance (rebuild & reorganize) across one or many Azure SQL databases. Includes a **dry-run** mode, configurable retries/timeouts, and produces a concise summary report at the end. Relies on scripts from [AzureSQL](https://github.com/yochananrachamim/AzureSQL).

## ✅ Features

- Discovery of subscriptions, servers & databases with optional filters (`--subscription`, `--server-name`, `--database-name`, etc).
- Embedded T-SQL script for index rebuild/reorganize—no external files needed.
- **Dry-run** (`--dry-run`) to preview the discovery without running SQL commands.
- Configurable **retry** count (`--retries`)
- Per-database **timeout** (`--timeout`) (default: 60 min)
- **Interactive** (browser) or **headless** (Service Principal / Managed Identity) auth
- Summarizes succeeded/failed/skipped databases on completion

## 🚀 CLI Usage

### 1. Interactive / Developer Mode

```bash
azsqlidxmgr si \
  --subscription MySubscription \
  --server-name sql-server-1 sql-server-2 \
  --database-name SalesDb InventoryDb \
  --dry-run \
  --timeout 00:30:00 \
  --retries 3
```

- Uses `DefaultAzureCredential` with interactive browser login allowed.
- Useful on dev laptops where `az login` has already been run.

### 2. Headless / Automated Mode

```bash
export AZURE_TENANT_ID=<tenant-id>
export AZURE_CLIENT_ID=<client-id>
export AZURE_CLIENT_SECRET=<client-secret>

azsqlidxmgr si \
  --subscription svc-sub \
  --server-name prod-sqlsvr \
  --database-name ProdDb \
  --timeout 01:00:00
```

- Leverages Service Principal or Managed Identity via environment vars.
- Perfect for CI/CD pipelines, cron jobs, or containerized tasks.

### ⚙️ Options

|Option|Description|Default|
|--|--|--|
|`--subscription <name>`|One or more subscription IDs or names (empty = all subscriptions)|—|
|`--server-name <name>`|One or more logical SQL server names|—|
|`--database-name <name>`|One or more database names (empty = all databases on each server)|—|
|`--dry-run`|Show SQL commands without executing them|`false`|
|`--timeout <hh:mm:ss>`|Per-database maximum run time|`01:00:00`|
|`--retries <int>`|Transient-fault retry attempts|`6`|

## 🔐 Authentication

Leverages Azure.Identity’s `DefaultAzureCredential`, which tries in order:

1. Environment variables (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`)
2. Managed identity (when running in Azure)
3. Azure CLI / Visual Studio credentials

> ![IMPORTANT]
> The database server must be configured to EntraID and have the current user (service principal, managed identity, user account) added to the user group of each database targeted. Otherwise, it is skipped.

## 📥 Installation

### 🍎 macOS (Homebrew)

```bash
brew install mburumaxwell/tap/azsqlidxmgr
```

### 🐧 Linux (DEB, RPM, APK)

Download the appropriate `.deb`, `.rpm`, or `.apk` from [Releases](https://github.com/mburumaxwell/azsqlidxmgr/releases) and install via:

```bash
# Debian/Ubuntu
sudo dpkg -i azsqlidxmgr-<version>-linux-<arch>.deb

# RHEL/Fedora/AlmaLinux
sudo dnf install -y azsqlidxmgr-<version>-linux-<arch>.rpm

# Alpine
sudo apk add --allow-untrusted azsqlidxmgr-<version>-linux-<arch>.apk
```

### 🖥️ Windows (Scoop)

```bash
scoop bucket add mburumaxwell https://github.com/mburumaxwell/scoop-tools.git
scoop install azsqlidxmgr
```

### 🛠️ .NET Tool

```bash
dotnet tool install --global azsqlidxmgr
azsqlidxmgr --help
```

### 🐳 Docker

```bash
docker run --rm -it \
  --env AZURE_TENANT_ID=<tenant> \
  --env AZURE_CLIENT_ID=<client> \
  --env AZURE_CLIENT_SECRET=<secret> \
  ghcr.io/mburumaxwell/azsqlidxmgr \
  --subscription prod-sub --server-name prod-sql --database-name ProdDb --dry-run
```

## ☸️ Kubernetes Deployment

Apply the sample manifest:

```bash
kubectl apply -f k8s.yaml
kubectl logs -l app=azsqlidxmgr -f
```

## License

This project is licensed under the MIT License; see [LICENSE](./LICENSE) for details.
