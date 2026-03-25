# TODO: Virus Scanning for File Transfers

## Background

From Jennifer — virus scanning has historically been offloaded to the OS-wide scanner rather than done in-connection. In-connection scanning is ideal but would require a paid tool.

## Proposed Approach: Quarantine Folder

Middle ground using open-source tooling (e.g. ClamAV):

1. **Quarantine folder** mirrors the final Transfer directory structure
2. Files downloaded from FTP/SFTP sources land in quarantine first (not directly into Transfer)
3. **Monitoring script** watches the quarantine folder, scans each file, then moves clean files into the Transfer folder
4. Infected files are logged and moved to a rejection/quarantine-hold folder
5. Code change is minimal — just change the initial download target directory

### Considerations
- How sensitive is the workflow to files being immediately available after fetch? If there's tolerance for a short delay (scan time), this approach works well
- All achievable with open-source tooling (ClamAV + inotifywait/incron or a simple cron poll)

## Pre-scan Sanity Checks (in-app, no external tooling)

Basic validation to apply in `FetchFilesFromSourceAsync` before/after download:

- [ ] **File size limits** — reject files above a configurable max size, reject zero-byte files
- [ ] **Expected extensions** — whitelist allowed extensions per file type (e.g. `.csv`, `.txt`, `.dat`)
- [ ] **File name validation** — reject names with suspicious characters, double extensions (e.g. `.csv.exe`), or names that don't match the configured `FileNamePattern`
- [ ] **Magic bytes check** — verify file header bytes match expected format (e.g. CSV/text files should not have PE/ELF/ZIP headers unless expected)

## Implementation Notes

- Quarantine folder path could be a config setting on `FtpServer.TempLocalPath` or a separate `QuarantinePath` field
- Sanity checks can be added as a validation step in `FileTransferService` without external dependencies
- ClamAV scanning script is external to the .NET API — deployed alongside as a systemd service or cron job
