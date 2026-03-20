# File Loading API - Administration Manual

## Overview

This document covers deployment, configuration, monitoring, and troubleshooting for the File Loading API (v4). The API runs as a self-hosted ASP.NET Core application on port **5140**.

---

## Deployment

### Linux (Production -- systemd)

The production deployment target is `LinWebProd0` at `10.1.20.55`. The application is deployed to `/var/www/api/v4/file-loading/`.

#### systemd Service File

Create `/etc/systemd/system/selcomm-file-loading.service`:

```ini
[Unit]
Description=Selcomm File Loading API v4
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/api/v4/file-loading
ExecStart=/usr/bin/dotnet /var/www/api/v4/file-loading/FileLoading.dll --urls "http://0.0.0.0:5140"
Restart=always
RestartSec=10
SyslogIdentifier=selcomm-file-loading
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=SELCOMM_CONFIG_PATH=/etc/selcomm/appsettings.shared.json

[Install]
WantedBy=multi-user.target
```

#### Service Management

```bash
# Enable the service to start on boot
sudo systemctl enable selcomm-file-loading

# Start the service
sudo systemctl start selcomm-file-loading

# Check service status
sudo systemctl status selcomm-file-loading

# View logs
sudo journalctl -u selcomm-file-loading -f

# Restart after deployment
sudo systemctl restart selcomm-file-loading
```

#### Deployment Steps

1. Build the project in Release configuration:
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. Copy the published output to the server:
   ```bash
   scp -r ./publish/* gordong@selectsoftware.com.au@10.1.20.55:/var/www/api/v4/file-loading/
   ```

3. Restart the service:
   ```bash
   ssh gordong@selectsoftware.com.au@10.1.20.55 "sudo systemctl restart selcomm-file-loading"
   ```

4. Verify the deployment:
   ```bash
   curl http://10.1.20.55:5140/api/v4/file-loading/health-check
   ```

### Windows

On Windows, the application can be run directly or as a Windows service.

#### Direct Execution

```powershell
cd C:\Selcomm.File.Loading.Api\FileLoading
dotnet run --urls "http://0.0.0.0:5140"
```

#### As a Windows Service

1. Publish the application:
   ```powershell
   dotnet publish -c Release -o C:\Services\FileLoading
   ```

2. Create the Windows service:
   ```powershell
   sc.exe create "SelcommFileLoading" binPath="C:\Services\FileLoading\FileLoading.exe --urls http://0.0.0.0:5140" start=auto
   sc.exe description "SelcommFileLoading" "Selcomm File Loading API v4"
   ```

3. Start the service:
   ```powershell
   sc.exe start "SelcommFileLoading"
   ```

---

## Configuration

The application loads configuration from multiple sources in this order (later sources override earlier ones):

1. **Shared configuration** (`appsettings.shared.json`) -- loaded first
2. **Local configuration** (`appsettings.json`) -- local overrides
3. **Environment-specific** (`appsettings.{Environment}.json`) -- environment overrides
4. **Environment variables** -- highest priority

### Shared Configuration Path

The shared configuration file path is determined by:

1. The `SELCOMM_CONFIG_PATH` environment variable (if set)
2. Windows default: `C:\Selcomm\configuration\appsettings.shared.json`
3. Linux default: `/etc/selcomm/appsettings.shared.json`

### appsettings.shared.json (Required Settings)

This file is shared across all Selcomm API v4 services. It must contain:

```json
{
  "JwtSettings": {
    "SecretKey": "your-256-bit-secret-key-here-minimum-32-chars",
    "Issuer": "AuthenticationApi",
    "Audience": "SelcommApi"
  },
  "DomainJwtSettings": {
    "domain1": {
      "Issuer": "AuthenticationApi-domain1"
    }
  },
  "ConnectionStrings": {
    "HealthCheck": "DSN=SelcommDSN;UID=user;PWD=password;"
  }
}
```

**Required fields:**

| Setting | Description |
|---------|-------------|
| `JwtSettings:SecretKey` | Symmetric signing key for JWT validation (minimum 32 characters). Must match the Authentication API. |
| `JwtSettings:Issuer` | Expected JWT issuer value. |
| `JwtSettings:Audience` | Expected JWT audience value. |
| `ConnectionStrings:HealthCheck` | ODBC connection string used by the health check endpoint. |

**Optional fields:**

| Setting | Description |
|---------|-------------|
| `DomainJwtSettings:{domain}:Issuer` | Per-domain JWT issuer overrides. All listed issuers are accepted as valid. |

### appsettings.json (Application-Specific Settings)

```json
{
  "ConnectionStrings": {
    "Default": "DSN=SelcommDSN;UID=user;PWD=password;"
  },
  "FileLoaderOptions": {
    "Default": {
      "Default": {
        "BatchSize": 1000,
        "TransactionBatchSize": 1000,
        "UseStreamingMode": true
      }
    },
    "domain1": {
      "Default": {
        "BatchSize": 500
      },
      "CDR": {
        "BatchSize": 2000,
        "TransactionBatchSize": 2000
      },
      "CHG": {
        "BatchSize": 1000
      }
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### FileLoaderOptions

The `FileLoaderOptions` section configures file processing behavior with a hierarchical domain/file-type structure.

**Resolution order** (most specific wins):

1. `{domain}` -> `{fileType}` (exact match)
2. `{domain}` -> `Default` (domain default)
3. `Default` -> `{fileType}` (global file type default)
4. `Default` -> `Default` (global default)
5. Built-in defaults

**Available options:**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `BatchSize` | int | 1000 | Number of records to buffer before flushing to database |
| `TransactionBatchSize` | int | 1000 | Number of records per database transaction |
| `UseStreamingMode` | bool | true | Enable streaming mode for large files (two-pass: validate then stream insert) |

---

## Serilog Logging Configuration

The API uses Serilog for structured logging with two output sinks.

### Console Output

```
[10:30:00 INF] Loading file: cdr_20260320.csv, Type: TEL_GSM
```

Format: `[{Timestamp:HH:mm:ss} {Level:u3}] {Message}{NewLine}{Exception}`

### File Output

Logs are written to rolling daily files at `logs/fileloading-{date}.log`.

Format: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message}{NewLine}{Exception}`

All log entries are enriched with the `Application=FileLoading` property.

### Configuring Log Levels

Override log levels in `appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "System": "Warning",
        "FileLoading.Workers": "Debug"
      }
    }
  }
}
```

Common log level adjustments:

- Set `FileLoading.Workers` to `Debug` to see detailed transfer scheduling decisions.
- Set `FileLoading.Parsers` to `Debug` to see record-level parsing details.
- Set `Microsoft.AspNetCore` to `Information` to see HTTP request logs.

---

## Background Worker (FileTransferWorker)

The `FileTransferWorker` is a hosted background service that runs scheduled file transfers automatically.

### How It Works

1. The worker starts when the application starts and runs continuously.
2. Every **60 seconds**, it checks all enabled transfer sources that have a CRON schedule.
3. For each source, it evaluates whether the CRON schedule indicates a run is due (comparing against the last run time).
4. If a run is due, it calls the same fetch logic as the `POST /transfers/{source-id}/fetch` endpoint.
5. Results are logged (files found, downloaded, skipped, failed).

### CRON Schedule Format

The worker uses **6-part CRON expressions** (with seconds) via the Cronos library:

```
┌───────────── second (0-59)
│ ┌───────────── minute (0-59)
│ │ ┌───────────── hour (0-23)
│ │ │ ┌───────────── day of month (1-31)
│ │ │ │ ┌───────────── month (1-12)
│ │ │ │ │ ┌───────────── day of week (0-6, Sun=0)
│ │ │ │ │ │
* * * * * *
```

**Common schedules:**

| Expression | Description |
|------------|-------------|
| `0 */15 * * * *` | Every 15 minutes |
| `0 0 */6 * * *` | Every 6 hours |
| `0 0 2 * * *` | Daily at 2:00 AM |
| `0 30 8 * * 1-5` | Weekdays at 8:30 AM |
| `0 0 0 1 * *` | First day of each month at midnight |

### Worker Behavior

- The worker runs with a system security context (`file_transfer_worker`).
- Invalid CRON expressions are logged as warnings and the source is skipped.
- Errors during individual source fetches do not stop the worker; it continues to the next source.
- If the application is restarted, the worker treats all sources as potentially due for a run (last run time defaults to 2 minutes before startup).

---

## CORS Configuration

The API is configured with a permissive CORS policy that allows:

- Any origin
- Any HTTP method
- Any header

This is suitable for development and internal use. For production environments exposed to the internet, consider restricting the CORS policy in `appsettings.json` or `Program.cs`.

---

## Health Check Monitoring

### Endpoint

```
GET /api/v4/file-loading/health-check
```

This endpoint is unauthenticated and returns:

- **200 OK** with `Status: "Healthy"` when the API can reach the database.
- **503 Service Unavailable** with `Status: "Unhealthy"` when the database is unreachable or the HealthCheck connection string is not configured.

### Database Connectivity Test

The health check executes:

```sql
SELECT 1 FROM systables WHERE tabid = 1
```

This is a lightweight Informix query that verifies ODBC connectivity and database availability. The response includes `ResponseTimeMs` for monitoring latency.

### Monitoring Integration

Configure external monitoring tools (e.g., Nagios, Zabbix, Uptime Robot) to poll the health check endpoint:

```bash
# Simple monitoring script
curl -sf http://host:5140/api/v4/file-loading/health-check | grep -q '"Healthy"' || echo "ALERT: File Loading API unhealthy"
```

---

## Database Requirements

### Database Engine

The API connects to an **IBM Informix** database via **ODBC**. The ODBC driver must be installed and a DSN configured on the server.

### ODBC Driver Setup (Linux)

1. Install the Informix ODBC driver package.
2. Configure the DSN in `/etc/odbc.ini`:
   ```ini
   [SelcommDSN]
   Driver = /opt/informix/lib/cli/iclit09b.so
   Server = informix_server
   Database = selcomm_db
   ```
3. Verify connectivity:
   ```bash
   isql SelcommDSN user password
   ```

### ODBC Driver Setup (Windows)

1. Install the IBM Informix Client SDK.
2. Configure the DSN via ODBC Data Source Administrator (64-bit).
3. Test the connection in the ODBC administrator.

### Required Database Tables

The API interacts with the following tables (partial list):

| Table | Purpose |
|-------|---------|
| `nt_file` | Loaded file records (NtFileNum is the primary key) |
| `file_class` | File class definitions (CDR, CHG, etc.) |
| `file_type` | File type definitions with FK to file_class and networks |
| `file_type_nt` | File type NT records (customer/filename/skip configuration) |
| `networks` | Vendor/network records |
| `ntfl_transfer_source` | Transfer source configurations |
| `ntfl_transfer_record` | File transfer tracking records |
| `ntfl_downloaded_file` | Download history for duplicate prevention |
| `ntfl_folder_config` | Folder workflow configuration per domain |
| `ntfl_activity_log` | Activity/audit log |
| `ntfl_file_format_config` | Generic parser configurations |
| `ntfl_column_mapping` | Column mappings for generic parsers |
| `ntfl_generic_detail` | Generic parsed detail records |

### Stored Procedures

The API calls stored procedures for file loading operations. These include vendor-specific insert procedures and the generic file loading procedures. Custom stored procedures can be configured per parser via the `CustomSpName` field in parser configurations.

---

## Security Considerations

### Authentication

- **JWT tokens** are validated using a symmetric signing key shared with the Authentication API. The key must be at least 32 characters long.
- **API keys** are validated against the database. The API key handler uses the same `DbContext` as the application.
- The `MultiAuth` policy scheme automatically selects the appropriate authentication handler based on whether the `X-API-Key` header is present.

### Sensitive Data

- Transfer source passwords are stored encrypted in the database.
- Private key paths and certificate paths reference files on the server filesystem; ensure appropriate file permissions.
- Connection strings contain database credentials; restrict access to configuration files.
- Log files may contain file names and error details; ensure log directory permissions are appropriate.

### Network Security

- The API listens on port **5140**. Configure firewall rules to restrict access to trusted networks.
- For production, deploy behind a reverse proxy (e.g., Nginx, Apache) with TLS termination.
- The CORS policy is currently permissive (any origin). Restrict this for internet-facing deployments.

### File System Security

- The API reads and writes files to directories defined in folder workflow configurations. Ensure the service account has appropriate read/write permissions.
- Transfer sources may connect to external SFTP/FTP servers. Ensure outbound connections are allowed in firewall rules.
- Uploaded files (via the `/upload` endpoint) are written to the server filesystem. Consider file size limits and disk space monitoring.

---

## Troubleshooting

### Common Errors

#### "HealthCheck connection string not configured"

**Cause:** The `ConnectionStrings:HealthCheck` value is missing from configuration.

**Fix:** Add the HealthCheck connection string to `appsettings.shared.json`:
```json
{
  "ConnectionStrings": {
    "HealthCheck": "DSN=SelcommDSN;UID=user;PWD=password;"
  }
}
```

#### "JWT SecretKey is not configured"

**Cause:** The application cannot find `JwtSettings:SecretKey` in any configuration source.

**Fix:** Ensure `appsettings.shared.json` contains the `JwtSettings` section with a `SecretKey` value. Verify the `SELCOMM_CONFIG_PATH` environment variable points to the correct file.

#### 401 Unauthorized on all requests

**Cause:** JWT token is expired, malformed, or signed with a different key.

**Fix:**
1. Verify the `JwtSettings:SecretKey` matches the Authentication API.
2. Verify the `JwtSettings:Issuer` and `JwtSettings:Audience` values match.
3. Check that the token has not expired.
4. If using API key authentication, verify the `X-API-Key` header is present and the key is valid in the database.

#### Database connectivity errors

**Cause:** ODBC driver not installed, DSN not configured, or database is unreachable.

**Fix:**
1. Verify the ODBC driver is installed: `odbcinst -q -d` (Linux) or check ODBC Administrator (Windows).
2. Test the DSN directly: `isql SelcommDSN user password` (Linux).
3. Check the connection string format in configuration.
4. Verify network connectivity to the database server.
5. Check the health check endpoint for the specific error message.

#### FileTransferWorker not fetching files

**Cause:** The worker may not be triggered, or the source may be misconfigured.

**Fix:**
1. Check that the transfer source has `IsEnabled = true`.
2. Check that the `CronSchedule` is a valid 6-part CRON expression (with seconds field).
3. Set log level for `FileLoading.Workers` to `Debug` to see scheduling decisions.
4. Test the connection manually via `POST /sources/{source-id}/test`.
5. Check that the remote path exists and contains files matching the `FileNamePattern`.

#### File parsing errors

**Cause:** The file format does not match the parser expectations.

**Fix:**
1. Check the validation summary via `GET /files/{nt-file-num}/validation-summary` for detailed error information.
2. Review the file type NT configuration (`SkipHdr`, `SkipTlr` values).
3. For generic parsers, review the column mappings and delimiter settings.
4. Check the activity log via `GET /activity?ntFileNum={id}` for processing history.

#### "Connection test failed" for transfer source

**Cause:** Unable to connect to the remote SFTP/FTP server.

**Fix:**
1. Verify the hostname and port are correct.
2. Check that the authentication credentials (password/key) are correct.
3. For SFTP with key-based auth, verify the private key file exists at the specified path and has correct permissions.
4. Check that outbound connections to the remote host/port are allowed by the firewall.
5. Use `POST /sources/test` with the configuration to test without saving.

### Log File Locations

| Platform | Location |
|----------|----------|
| Linux | `/var/www/api/v4/file-loading/logs/fileloading-{date}.log` |
| Windows | `{app-directory}/logs/fileloading-{date}.log` |
| systemd journal | `journalctl -u selcomm-file-loading` |

### Swagger UI

The Swagger UI is available at:

```
https://host:5140/swagger
```

The Swagger JSON specification is at:

```
https://host:5140/swagger/v4/swagger.json
```

Swagger is enabled in all environments and provides interactive API documentation with authentication support (both JWT Bearer and API Key).
