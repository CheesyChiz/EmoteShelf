"""SSH-only report reader. No network listener or credentials."""
import argparse
import json
import re
import sqlite3

parser = argparse.ArgumentParser()
parser.add_argument('--db', default='/var/lib/emoteshelf-relay/reports.sqlite3')
group = parser.add_mutually_exclusive_group()
group.add_argument('--recent', action='store_true')
group.add_argument('--id')
group.add_argument('--attempt')
args = parser.parse_args()
if args.id and not re.fullmatch('[0-9a-f]{32}', args.id):
    parser.error('Invalid report id')
if args.attempt and not re.fullmatch('[0-9a-f]{64}', args.attempt):
    parser.error('Invalid attempt id')
db = sqlite3.connect('file:' + args.db + '?mode=ro', uri=True)
db.row_factory = sqlite3.Row
try:
    if args.id:
        rows = db.execute('SELECT received,payload FROM reports WHERE id=?', (args.id,))
    elif args.attempt:
        rows = db.execute('SELECT received,payload FROM reports WHERE attempt=? ORDER BY received LIMIT 100', (args.attempt,))
    elif args.recent:
        rows = db.execute('SELECT received,payload FROM reports ORDER BY received DESC LIMIT 30')
    else:
        rows = db.execute('SELECT version,code,COUNT(*) AS count,MAX(received) AS last_received FROM reports GROUP BY version,code ORDER BY count DESC')
    print(json.dumps([dict(row) for row in rows], indent=2))
finally:
    db.close()
