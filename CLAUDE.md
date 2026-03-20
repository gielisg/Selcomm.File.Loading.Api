# Selcomm.File.Loading.Api API V4

| Setting | Value |
|---------|-------|
| Project | Selcomm.File.Loading.Api |
| Port | 5140 |
| Module (kebab) | file-loading |
## Agent instructions (read from C:\Claude — do not copy)

When working in this project, read the instruction file from the path (e.g. "Read C:\claude\projects\api-v4\agents\orchestration\INSTRUCTIONS.md"). No local copy is needed; updates in C:\Claude apply everywhere.

- Orchestration: `C:\claude\projects\api-v4\agents\orchestration\INSTRUCTIONS.md`
- API (new): `C:\claude\projects\api-v4\agents\api\INSTRUCTIONS.md`
- Conversion: `C:\claude\projects\api-v4\agents\conversion\INSTRUCTIONS.md`
- Open-API: `C:\claude\projects\api-v4\agents\open-api\INSTRUCTIONS.md`
- SP Analysis: `C:\claude\projects\api-v4\agents\sp-analysis\INSTRUCTIONS.md`
- Stored Procedure: `C:\claude\projects\api-v4\agents\stored-procedure\INSTRUCTIONS.md`
- SP Validation: `C:\claude\projects\api-v4\agents\sp-validation\INSTRUCTIONS.md`
- Testing: `C:\claude\projects\api-v4\agents\testing\INSTRUCTIONS.md`
- Documentation: `C:\claude\projects\api-v4\agents\documentation\INSTRUCTIONS.md`
- Swagger: `C:\claude\projects\api-v4\agents\swagger\INSTRUCTIONS.md`
- QA: `C:\claude\projects\api-v4\agents\qa\INSTRUCTIONS.md`
- Standards: `C:\claude\projects\api-v4\agents\standards\INSTRUCTIONS.md`
- Standards (shared): `C:\claude\projects\api-v4\shared\STANDARDS.md`
- Linux Deployment: `C:\claude\projects\api-v4\agents\linux-deployment\INSTRUCTIONS.md`

## Shared resources (read from C:\Claude)

- Port allocation: `C:\claude\shared\PORT_ALLOCATION.md`
- Error codes: `C:\claude\shared\ERROR_CODES.md`
- Patterns: `C:\claude\shared\PATTERNS.md`
- Deployment guide: `C:\claude\projects\api-v4\shared\DEPLOYMENT.md`

Reference implementation: `C:\Selcomm.Authentication.Api`

## Linux server access

You have SSH access to the Linux production server. Read the Linux Deployment agent instructions for full details:
`C:\claude\projects\api-v4\agents\linux-deployment\INSTRUCTIONS.md`

| Item | Value |
|------|-------|
| Server | `10.1.20.55` (LinWebProd0) |
| SSH | `ssh gordong@selectsoftware.com.au@10.1.20.55` |
| Y: drive | Mapped to server root `/` (for file staging) |
| App directory | `/var/www/api/v4/{service_name}/` |
