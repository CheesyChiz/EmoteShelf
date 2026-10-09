"""Short-lived inbox capabilities. No names, commands, files or coordinates."""
import secrets
import threading
import time
import re


class LinkStore:
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.lock = threading.Lock()
        self.users = {}

    def clear(self, token):
        item = self.users.get(token)
        if not item:
            return
        self.clear_offer(token)
        other = item['peer'] or item['pending']
        peer = self.users.get(other)
        if peer:
            peer.update(peer=None, pending=None, incoming=False, invitation='')
        item.update(peer=None, pending=None, incoming=False, invitation='')

    def clear_offer(self, token):
        item = self.users.get(token)
        if not item:
            return
        peer = self.users.get(item['peer'])
        if peer:
            peer['offer'] = None
        item['offer'] = None

    def prune(self):
        for token, item in list(self.users.items()):
            if self.clock() - item['seen'] > 8:
                self.clear(token)
                del self.users[token]
            elif item['pending'] and self.clock() >= item['until']:
                self.clear(token)
            elif item['offer'] and self.clock() >= item['offer']['until']:
                self.clear_offer(token)

    def register(self, identity):
        if not isinstance(identity, str) or not re.fullmatch('[0-9a-f]{64}', identity):
            raise ValueError('Invalid identity')
        with self.lock:
            self.prune()
            if len(self.users) >= 512 or any(u['identity'] == identity for u in self.users.values()):
                raise ValueError('Already connected or busy')
            token = secrets.token_urlsafe(32)
            self.users[token] = dict(identity=identity, seen=self.clock(), peer=None,
                                    pending=None, incoming=False, invitation='', until=0, next_invite=0, offer=None)
            return token

    def action(self, token, action='poll', target='', invitation='', offer=None):
        with self.lock:
            self.prune()
            item = self.users.get(token)
            if not item:
                raise ValueError('Expired connection')
            item['seen'] = self.clock()
            if action == 'propose':
                peer = self.users.get(item['peer'])
                if not peer or item['offer'] or peer['offer']:
                    raise ValueError('Launch already pending or not linked')
                if not isinstance(offer, dict) or set(offer) != {'id', 'family', 'command', 'role'} or not all(isinstance(v, str) and re.fullmatch('[0-9a-f]{64}', v) for v in offer.values()):
                    raise ValueError('Invalid proposal')
                item['offer'] = peer['offer'] = dict(**offer, sender=token, status='waiting', until=self.clock()+60)
            elif action in ('accept_launch', 'reject_launch', 'cancel_launch', 'finish_launch'):
                current = item['offer']
                if not current or current['id'] != invitation:
                    raise ValueError('Stale launch')
                if action == 'accept_launch':
                    if current['sender'] == token or current['status'] != 'waiting':
                        raise ValueError('Not an incoming launch')
                    current['status'] = 'accepted'
                    current['until'] = self.clock()+60
                elif action == 'finish_launch':
                    if current['status'] != 'accepted':
                        raise ValueError('Not accepted')
                    current.setdefault('finished', set()).add(token)
                    if len(current['finished']) == 2:
                        self.clear_offer(token)
                else:
                    self.clear_offer(token)
            elif action == 'invite':
                if item['peer'] or item['pending'] or self.clock() < item['next_invite']:
                    raise ValueError('Busy or cooldown')
                item['next_invite'] = self.clock() + 10
                other = next((t for t, u in self.users.items() if u['identity'] == target and t != token), None)
                peer = self.users.get(other)
                if not peer or peer['peer'] or peer['pending']:
                    raise ValueError('Partner unavailable')
                nonce = secrets.token_urlsafe(32)
                item.update(pending=other, incoming=False, invitation=nonce, until=self.clock()+30)
                peer.update(pending=token, incoming=True, invitation=nonce, until=self.clock()+30)
            elif action in ('accept', 'decline'):
                if not item['incoming'] or not invitation or item['invitation'] != invitation:
                    raise ValueError('Stale invitation')
                other = item['pending']
                peer = self.users.get(other)
                if not peer or peer['pending'] != token or peer['invitation'] != invitation:
                    raise ValueError('Invitation expired')
                self.clear(token)
                if action == 'accept':
                    item['peer'] = other
                    peer['peer'] = token
            elif action == 'disconnect':
                self.clear(token)
            elif action == 'close':
                self.clear(token)
                del self.users[token]
                return dict(state='closed', partner='', invitation='', remaining=0)
            elif action != 'poll':
                raise ValueError('Unknown action')
            peer = self.users.get(item['peer'] or item['pending'])
            state = 'linked' if item['peer'] else ('incoming' if item['incoming'] else 'outgoing') if peer else 'idle'
            current = item['offer']
            launch = dict(id=current['id'], family=current['family'], command=current['command'], role=current['role'],
                          incoming=current['sender'] != token, status=current['status'], remaining=max(0, int(current['until']-self.clock()))) if current else None
            return dict(state=state, partner=peer['identity'] if peer else '', invitation=item['invitation'],
                        remaining=max(0, int(item['until']-self.clock())) if item['pending'] else 0, launch=launch)
