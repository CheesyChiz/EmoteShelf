"""Private, consent-gated ephemeral package mailbox. No disk or public URLs."""
import base64
import hashlib
import re
import secrets
import time
import json


class Transfers:
    MAX_PACKAGE = 32 * 1024 * 1024
    MAX_TOTAL = 128 * 1024 * 1024
    CHUNK = 256 * 1024
    TTL = 600

    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.items = {}
        self.cooldowns = {}
        self.outcomes = {}
        self.failed_receipts = {}

    def finish(self, key, outcome):
        item = self.items.pop(key)
        for token in (item['sender'], item['receiver']):
            self.outcomes[token] = (outcome, self.clock()+60)

    def prune(self, users):
        for key, item in list(self.items.items()):
            a, b = users.get(item['sender']), users.get(item['receiver'])
            if self.clock() >= item['expires'] or not a or not b or a['peer'] != item['receiver'] or b['peer'] != item['sender']:
                self.finish(key, 'expired')
        self.cooldowns = {k: v for k, v in self.cooldowns.items() if k in users and v > self.clock()}
        self.outcomes = {k: v for k, v in self.outcomes.items() if k in users and v[1] > self.clock()}
        self.failed_receipts = {k: v for k, v in self.failed_receipts.items() if k in users and v[1] > self.clock()}

    def action(self, links, token, data, protocol=4):
        # Lock order: only the existing link lock. An unlink invalidates both directions.
        with links.lock:
            links.prune()
            self.prune(links.users)
            user = links.users.get(token)
            if not user or not user['peer']:
                raise ValueError('transfer_not_linked')
            peer = user['peer']
            user['transfer_protocol'] = protocol
            action = data.get('action')
            if action == 'offer':
                if set(data) not in ({'action', 'name', 'size', 'sha256'}, {'action', 'name', 'size', 'sha256', 'launch'}):
                    raise ValueError('transfer_schema')
                launch = data.get('launch', '')
                if not isinstance(launch, str) or len(launch) > 8192:
                    raise ValueError('transfer_launch_invalid')
                if launch:
                    try:
                        pair_launch = json.loads(launch)
                    except (ValueError, TypeError):
                        pair_launch = {}
                    if isinstance(pair_launch, dict) and pair_launch.get('PairId') and (
                            protocol < 5 or links.users[peer].get('transfer_protocol', 0) < 5):
                        raise ValueError('transfer_update_both_clients')
                name, size, sha = data['name'], data['size'], data['sha256']
                if not isinstance(name, str) or not 1 <= len(name) <= 100 or any(ord(c) < 32 for c in name):
                    raise ValueError('transfer_name')
                if type(size) is not int or not 0 < size <= self.MAX_PACKAGE or not isinstance(sha, str) or not re.fullmatch('[0-9a-f]{64}', sha):
                    raise ValueError('transfer_size_or_hash')
                if token in self.cooldowns or len(self.items) >= 32 or any(token in (v['sender'], v['receiver']) or peer in (v['sender'], v['receiver']) for v in self.items.values()):
                    raise ValueError('transfer_busy')
                if sum(v['size'] for v in self.items.values()) + size > self.MAX_TOTAL:
                    raise ValueError('transfer_quota')
                key = secrets.token_hex(16)
                self.items[key] = dict(id=key, sender=token, receiver=peer, name=name,
                                       size=size, sha256=sha, launch=launch, expires=self.clock()+self.TTL, state='offered', payload=bytearray())
                self.cooldowns[token] = self.clock()+30
                self.outcomes.pop(token, None)
                self.outcomes.pop(peer, None)
            elif action == 'poll':
                if set(data) != {'action'}:
                    raise ValueError('transfer_schema')
            else:
                key = data.get('id')
                item = self.items.get(key) if isinstance(key, str) else None
                if action == 'fail' and set(data) == {'action', 'id'} and self.failed_receipts.get(token, (None, 0))[0] == key:
                    return dict(outcome='installation_failed', transfer=None)
                if not item or token not in (item['sender'], item['receiver']):
                    raise ValueError('transfer_expired')
                incoming = token == item['receiver']
                if action == 'accept' and incoming and item['state'] == 'offered' and set(data) == {'action', 'id'}:
                    item['state'] = 'accepted'
                elif action in ('reject', 'complete', 'cancel', 'fail') and set(data) == {'action', 'id'}:
                    if (action in ('reject', 'complete', 'fail') and not incoming) or (action == 'cancel' and incoming):
                        raise ValueError('transfer_role')
                    if action in ('complete', 'fail') and item['state'] != 'ready':
                        raise ValueError('transfer_not_ready')
                    if action == 'complete' and item['launch']:
                        try:
                            launch = json.loads(item['launch'])
                        except (ValueError, TypeError):
                            launch = {}
                        if isinstance(launch, dict) and launch.get('PairId'):
                            fields = [launch.get(k) for k in ('PairId', 'Family', 'SenderCommand', 'SenderRole')]
                            if not all(isinstance(v, str) and re.fullmatch('[0-9a-f]{64}', v) for v in fields):
                                raise ValueError('transfer_launch_invalid')
                            sender, receiver = links.users[item['sender']], links.users[item['receiver']]
                            if sender['offer'] or receiver['offer']:
                                raise ValueError('transfer_busy')
                            sender['offer'] = receiver['offer'] = dict(id=fields[0], family=fields[1],
                                command=fields[2], role=fields[3], sender=item['sender'],
                                status='accepted', until=self.clock()+60)
                    if action == 'fail':
                        self.failed_receipts[token] = (key, self.clock()+60)
                    self.finish(key, {'complete': 'completed', 'reject': 'declined', 'cancel': 'cancelled', 'fail': 'installation_failed'}[action])
                elif action == 'upload' and not incoming and item['state'] == 'accepted' and set(data) == {'action', 'id', 'offset', 'chunk'}:
                    if type(data['offset']) is not int or data['offset'] != len(item['payload']) or not isinstance(data['chunk'], str) or len(data['chunk']) > 4*((self.CHUNK+2)//3):
                        raise ValueError('transfer_chunk')
                    try:
                        chunk = base64.b64decode(data['chunk'], validate=True)
                    except Exception:
                        raise ValueError('transfer_chunk') from None
                    if not 0 < len(chunk) <= self.CHUNK or len(item['payload']) + len(chunk) > item['size']:
                        raise ValueError('transfer_chunk')
                    item['payload'].extend(chunk)
                    if len(item['payload']) == item['size']:
                        if hashlib.sha256(item['payload']).hexdigest() != item['sha256']:
                            self.finish(key, 'integrity_failed')
                            raise ValueError('transfer_hash')
                        item['state'] = 'ready'
                elif action == 'download' and incoming and item['state'] == 'ready' and set(data) == {'action', 'id', 'offset'}:
                    offset = data['offset']
                    if type(offset) is not int or not 0 <= offset < item['size']:
                        raise ValueError('transfer_chunk')
                    return dict(chunk=base64.b64encode(item['payload'][offset:offset+self.CHUNK]).decode())
                else:
                    raise ValueError('transfer_state')
            item = next((v for v in self.items.values() if token in (v['sender'], v['receiver'])), None)
            return dict(outcome=self.outcomes.pop(token, ('', 0))[0], transfer=None if not item else dict(
                **{k: item[k] for k in ('id', 'name', 'size', 'sha256', 'state', 'launch')},
                incoming=token == item['receiver'], offset=len(item['payload']),
                remaining=max(0, int(item['expires']-self.clock()))))
