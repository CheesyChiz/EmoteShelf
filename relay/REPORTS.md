# Opt-in error reports

Uploads are disabled by default. Consent can be revoked in Settings; a request already received cannot be recalled. No full log upload, disk queue, persistent installation identifier or automatic retry. Client limits: 1/minute and same category 1/10 minutes. Only fixed-schema metadata is sent, not gameplay content. Reports are untrusted client claims, not proof of cause or identity.

## Storage and access

`/var/lib/emoteshelf-relay/reports.sqlite3`, systemd StateDirectory mode 0700, umask 0077. SQLite DELETE journal (no unbounded WAL), secure_delete enabled, 8192-page limit (32 MiB at 4096-byte pages), maximum 10,000 records. Oldest records evicted at capacity. Journal can temporarily use additional disk space. Retention is 14 days, swept hourly and before insert; expiry can lag by at most one hour while the service is running. Startup also sweeps. No diagnostic backups: do not include this directory in longer-lived backups/snapshots if promising 14-day retention.

No public read/list/delete endpoint. Administrators inspect via SSH with parameterized SQL or a local sqlite client. Indices: receipt time, version/code, attempt. `received` is server UTC Unix time. IDs are random, attempt is empty outside a pair launch. There is no stable user identifier. IP exists only in in-memory minute rate limits, not report rows. Caddy configuration has no access log enabled; verify provider/proxy logging separately.

Per-IP 3/minute and global 100/minute; body max 2048 bytes. Anonymous intake is deliberately untrusted and could be spammed despite rate/cap limits. SQLite failures return 503 without changing pair state. Report upload has no authorization or execution semantics. Never execute strings from reports. No reports are forwarded to a third party.

Suggested SSH queries:

SSH reader: `python3 /opt/emoteshelf-relay/report_admin.py` lists grouped counts. Add `--recent`, `--id REPORT_ID`, or `--attempt ATTEMPT_ID` to inspect bounded results. It opens the database read-only.

```sql
SELECT version,code,COUNT(*) FROM reports GROUP BY version,code;
SELECT datetime(received,'unixepoch'),id,code,payload FROM reports ORDER BY received DESC LIMIT 30;
SELECT payload FROM reports WHERE attempt = ? ORDER BY received;
DELETE FROM reports WHERE id = ?;
```

Retention applies to this application store only; external VM snapshots must be configured separately. Check service errors, disk space and report count during operations. Do not log request bodies or authorization headers.
