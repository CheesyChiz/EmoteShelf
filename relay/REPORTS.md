# Required diagnostics with first-use disclosure

Extended reports accept an optional closed `context` object: pair protocol state, anchor role,
alignment/preparation flags, local readiness and bounded stable-frame count. Failure checkpoints
are allowlisted enum values, not exception messages. Old clients remain accepted. These fields
do not assert the peer's readiness; that is inferred only from the shared scheduled state.
Local copied diagnostics retain the last pair failure independently of the bounded event queue.
Basic lifecycle/failure events are captured even when verbose pair diagnostics are disabled.

New clients are inactive until users explicitly accept the versioned diagnostics notice. The old optional-upload preference is not migrated to acceptance. There is no upload toggle during use; users can decline and leave the plugin inactive, or disable/uninstall it. No full log upload, disk queue or persistent installation identifier. The bounded in-memory queue holds 32 reports, deduplicates each attempt/code/checkpoint, sends at most one request/minute and retries transient failures up to four attempts. Unloading discards pending reports. Delivery is not guaranteed. Only fixed-schema metadata is sent, not gameplay content. Reports are untrusted client claims, not proof of cause or identity. Legacy clients can still submit their previous optional reports.

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
