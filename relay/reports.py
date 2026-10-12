"""Opt-in structured diagnostics. No free text, identities, IPs or capabilities on disk."""
import json
import math
import re
import sqlite3
import threading
import time
from contextlib import contextmanager

STATES = {'none', 'connecting', 'idle', 'incoming', 'outgoing', 'linked', 'error'}
CODES = {'connection_failed', 'request_rejected', 'alignment_failed', 'launch_cancelled', 'lightless_api', 'invite_blocked'}
CODES.add('transfer_failed')
DETAILS = {'unstable', 'input', 'timeout', 'height', 'state', 'lightless_missing', 'not_connected', 'connection_error', 'stale', 'pending', 'already_linked', 'lightless_api', 'lightless_unhandled', 'unknown'}
DETAILS |= {'package_validation', 'package_import', 'package_integrity', 'package_busy', 'package_transport'}
DETAILS |= {'import_extract', 'import_register', 'import_collections', 'import_disable', 'import_authorize', 'import_enable', 'import_verify'}
DETAILS |= {'import_' + stage + '_' + kind
            for stage in ('extract', 'register', 'collections', 'disable', 'authorize', 'enable', 'verify')
            for kind in ('argument', 'index', 'null', 'access', 'io', 'state', 'ipc', 'other')}
DETAILS |= {'preflight', 'deadline', 'partner_identity', 'lightless_check', 'actor_state',
            'pose_setting', 'pair_transport', 'heartbeat', 'proposal', 'alignment',
            'local_movement', 'alignment_consent', 'partner_movement', 'prepare_mod',
            'post_prepare_alignment', 'prepared_action', 'start_deadline', 'validate_mod',
            'final_alignment', 'dispatch', 'prepare_validation', 'stand_up', 'resource_verification'}


def validate(data):
    fields = {'id', 'version', 'attempt', 'code', 'detail', 'state', 'fresh', 'lightless', 'distance', 'angle'}
    if not isinstance(data, dict) or set(data) not in (fields, fields | {'context'}):
        raise ValueError('schema')
    if 'context' in data:
        context = data['context']
        flags = {'Anchor', 'AlignStarted', 'PreparationStarted', 'Preparing', 'Ready'}
        resource_fields = {'ResourceStatus', 'LocalAssets', 'PeerAssets', 'LocalVerified', 'PeerVerified', 'SyncHttpStatus'}
        base_fields = flags | {'PairState', 'StableFrames'}
        playback_fields = {'ActorMode', 'EmoteId', 'Emoting', 'StationaryDance', 'HeightDelta', 'SoundFailure'}
        if not isinstance(context, dict) or set(context) not in (base_fields, base_fields | resource_fields, base_fields | resource_fields | playback_fields):
            raise ValueError('context schema')
        if 'ActorMode' in context:
            if type(context['ActorMode']) is not int or not -1 <= context['ActorMode'] <= 255:
                raise ValueError('actor mode')
            if type(context['EmoteId']) is not int or not 0 <= context['EmoteId'] <= 65535:
                raise ValueError('emote id')
            if any(type(context[k]) is not bool for k in ('Emoting', 'StationaryDance')):
                raise ValueError('playback flags')
            height = context['HeightDelta']
            if height is not None and (type(height) not in (int, float) or not math.isfinite(height) or abs(height) > 100):
                raise ValueError('height delta')
            if context['SoundFailure'] not in {'none', 'idle_timeout', 'redraw_timeout', 'state'}:
                raise ValueError('sound failure')
        if 'ResourceStatus' in context:
            if context['ResourceStatus'] not in {'none', 'transport', 'mismatch', 'unavailable', 'local_pending', 'peer_missing', 'peer_pending', 'verified'}:
                raise ValueError('resource status')
            for key in ('LocalAssets', 'PeerAssets'):
                if type(context[key]) is not int or not 0 <= context[key] <= 128:
                    raise ValueError('resource count')
            if any(type(context[key]) is not bool for key in ('LocalVerified', 'PeerVerified')):
                raise ValueError('resource flags')
            if type(context['SyncHttpStatus']) is not int or not (context['SyncHttpStatus'] == 0 or 100 <= context['SyncHttpStatus'] <= 599):
                raise ValueError('resource http')
        if context['PairState'] not in {'none', 'connecting', 'waiting', 'paired', 'scheduled', 'error'}:
            raise ValueError('pair state')
        if any(type(context[key]) is not bool for key in flags):
            raise ValueError('context flags')
        if type(context['StableFrames']) is not int or not 0 <= context['StableFrames'] <= 1000:
            raise ValueError('stable frames')
    if not isinstance(data['id'], str) or not re.fullmatch('[0-9a-f]{32}', data['id']):
        raise ValueError('id')
    if not isinstance(data['version'], str) or not re.fullmatch(r'\d{1,3}(\.\d{1,3}){3}', data['version']):
        raise ValueError('version')
    if not isinstance(data['attempt'], str) or not re.fullmatch('(?:[0-9a-f]{64})?', data['attempt']):
        raise ValueError('attempt')
    if data['code'] not in CODES or data['state'] not in STATES or data['detail'] not in DETAILS:
        raise ValueError('enum')
    if type(data['fresh']) is not bool or type(data['lightless']) is not bool:
        raise ValueError('bool')
    for key, maximum in [('distance', 100), ('angle', 3.142)]:
        value = data[key]
        if value is not None and (type(value) not in (float, int) or not math.isfinite(value) or not 0 <= value <= maximum):
            raise ValueError('number')
    return json.dumps(data, separators=(',', ':'), allow_nan=False)


class Reports:
    def __init__(self, path, clock=time.time, max_rows=10000):
        self.path, self.clock, self.max_rows = path, clock, max_rows
        self.lock = threading.Lock()
        self.rates = {}
        with self.connect() as db:
            db.execute('CREATE TABLE IF NOT EXISTS reports (id TEXT PRIMARY KEY, received INTEGER NOT NULL, version TEXT NOT NULL, code TEXT NOT NULL, attempt TEXT NOT NULL, payload TEXT NOT NULL)')
            db.execute('CREATE INDEX IF NOT EXISTS reports_received ON reports(received)')
            db.execute('CREATE INDEX IF NOT EXISTS reports_group ON reports(version,code)')
            db.execute('CREATE INDEX IF NOT EXISTS reports_attempt ON reports(attempt)')
        self.prune()

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=.25)
        db.execute('PRAGMA journal_mode=DELETE')
        db.execute('PRAGMA secure_delete=ON')
        db.execute('PRAGMA max_page_count=8192')  # 32 MiB with default 4 KiB pages; no growing WAL.
        try:
            with db:
                yield db
        finally:
            db.close()

    def prune(self):
        with self.lock, self.connect() as db:
            db.execute('DELETE FROM reports WHERE received < ?', (int(self.clock()) - 14*86400,))

    def add(self, data, address):
        payload = validate(data)
        now = int(self.clock())
        with self.lock:
            # IP is transient rate-limit state only, discarded each minute.
            minute = now // 60
            self.rates = {k: v for k, v in self.rates.items() if v[0] == minute}
            for key, limit in [(address, 3), ('global', 100)]:
                if self.rates.get(key, (minute, 0))[1] >= limit:
                    return False
            for key in (address, 'global'):
                self.rates[key] = (minute, self.rates.get(key, (minute, 0))[1] + 1)
            with self.connect() as db:
                db.execute('DELETE FROM reports WHERE received < ?', (now - 14*86400,))
                if db.execute('SELECT 1 FROM reports WHERE id=?', (data['id'],)).fetchone():
                    return True
                count = db.execute('SELECT COUNT(*) FROM reports').fetchone()[0]
                if count >= self.max_rows:
                    db.execute('DELETE FROM reports WHERE id IN (SELECT id FROM reports ORDER BY received,id LIMIT ?)', (count-self.max_rows+1,))
                db.execute('INSERT INTO reports VALUES (?,?,?,?,?,?)',
                           (data['id'], now, data['version'], data['code'], data['attempt'], payload))
            return True

    def maintain(self):
        while True:
            time.sleep(3600)
            try:
                self.prune()
            except sqlite3.Error:
                print('Report retention sweep failed; inspect storage availability.', flush=True)
