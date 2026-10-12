"""Short-lived inbox capabilities. Dance choices contain bounded option names; no character names, files or coordinates."""
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
            peer.update(peer=None, pending=None, incoming=False, invitation='', replay=None, sync=None)
        item.update(peer=None, pending=None, incoming=False, invitation='', replay=None, sync=None)

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
            if item.get('sync') and self.clock() - item['sync']['received'] > 3:
                item['sync'] = None
            if self.clock() - item['seen'] > 8:
                self.clear(token)
                del self.users[token]
            elif item['pending'] and self.clock() >= item['until']:
                self.clear(token)
            elif item['offer'] and self.clock() >= item['offer']['until']:
                self.clear_offer(token)

    def register(self, identity, dance=False, invite_only=False):
        if not isinstance(identity, str) or not re.fullmatch('[0-9a-f]{64}', identity):
            raise ValueError('Invalid identity')
        with self.lock:
            self.prune()
            if len(self.users) >= 512 or any(u['identity'] == identity for u in self.users.values()):
                raise ValueError('Already connected or busy')
            token = secrets.token_urlsafe(32)
            self.users[token] = dict(identity=identity, dance=dance, invite_only=invite_only is True, seen=self.clock(), peer=None,
                                    pending=None, incoming=False, invitation='', until=0, next_invite=0, offer=None)
            return token

    def action(self, token, action='poll', target='', invitation='', offer=None):
        with self.lock:
            self.prune()
            item = self.users.get(token)
            if not item:
                raise ValueError('Expired connection')
            item['seen'] = self.clock()
            if action == 'restart':
                peer = self.users.get(item['peer'])
                previous = item.get('replay')
                if not peer or not previous or peer.get('replay') != previous or item['offer'] or peer['offer']:
                    raise ValueError('No accepted playback to restart')
                if self.clock() - previous['at'] > 1800:
                    raise ValueError('Playback agreement expired')
                item['offer'] = peer['offer'] = dict(id=secrets.token_hex(32), family=previous['family'],
                    command=previous['command'], role=previous['role'], previous=previous['id'],
                    dance=previous.get('dance', False), options=previous.get('options'), invite_only=previous.get('invite_only', False),
                    sender=token, status='accepted', until=self.clock()+60)
            elif action == 'propose':
                peer = self.users.get(item['peer'])
                if not peer or item['offer'] or peer['offer']:
                    raise ValueError('Launch already pending or not linked')
                if not isinstance(offer, dict) or not {'id', 'family', 'command', 'role'} <= set(offer) or set(offer) - {'id', 'family', 'command', 'role', 'dance', 'options', 'invite_only'} or not all(isinstance(offer.get(k), str) and re.fullmatch('[0-9a-f]{64}', offer[k]) for k in ('id', 'family', 'command', 'role')):
                    raise ValueError('Invalid proposal')
                if type(offer.get('dance', False)) is not bool:
                    raise ValueError('Invalid dance mode')
                if type(offer.get('invite_only', False)) is not bool or (offer.get('invite_only') and
                        not (offer.get('dance') and item.get('invite_only') and peer.get('invite_only'))):
                    raise ValueError('Both clients must support dance invitations')
                if offer.get('dance') and not (item.get('dance') and peer.get('dance')):
                    raise ValueError('Both clients must support shared dance')
                options = offer.get('options')
                if options is not None:
                    if not offer.get('dance') or not isinstance(options, dict) or len(options) > 32:
                        raise ValueError('Invalid dance options')
                    total = 0
                    for group, choices in options.items():
                        if not isinstance(group, str) or not 0 < len(group) <= 128 or not isinstance(choices, list) or len(choices) > 32 or any(not isinstance(c, str) or not 0 < len(c) <= 128 for c in choices):
                            raise ValueError('Invalid dance options')
                        total += len(group.encode('utf-8')) + sum(len(c.encode('utf-8')) for c in choices)
                    if total > 1200:
                        raise ValueError('Dance options too large')
                item['replay'] = peer['replay'] = None
                item['offer'] = peer['offer'] = dict(**offer, sender=token, status='waiting', until=self.clock()+60)
            elif action == 'request_mod':
                current = item['offer']
                if not current or current['id'] != invitation or current['sender'] == token or current['status'] != 'waiting':
                    raise ValueError('Not an incoming waiting launch')
                current['missing_mod'] = True
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
                        replay = {k: current[k] for k in ('id', 'family', 'command', 'role')}
                        replay.update(dance=current.get('dance', False), options=current.get('options'), invite_only=current.get('invite_only', False))
                        replay['at'] = self.clock()
                        item['replay'] = self.users[item['peer']]['replay'] = replay
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
                          dance=current.get('dance', False), options=current.get('options'), invite_only=current.get('invite_only', False),
                          incoming=current['sender'] != token, status=current['status'], remaining=max(0, int(current['until']-self.clock())),
                          previous=current.get('previous', ''), missing_mod=current.get('missing_mod', False)) if current else None
            return dict(state=state, partner=peer['identity'] if peer else '', invitation=item['invitation'], current_peer=bool(peer and peer.get('dance')), invitation_peer=bool(peer and peer.get('invite_only')),
                        remaining=max(0, int(item['until']-self.clock())) if item['pending'] else 0, launch=launch)
