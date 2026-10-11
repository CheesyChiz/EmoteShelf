"""Bounded, ephemeral resource fingerprints and phase telemetry for accepted launches."""
import math
import re

HEX = re.compile(r'^[0-9a-f]{64}$')
PATH = re.compile(r'^(chara|sound|vfx)/[a-z0-9_/.-]+\.(pap|tmb|scd|avfx|atex|tex|mdl|mtrl|sklb)$')

def exchange(links, token, data):
    if set(data) != {'attempt', 'assets', 'verified', 'phase', 'duration', 'age'}:
        raise ValueError('sync_schema')
    if not isinstance(data['attempt'], str) or not HEX.fullmatch(data['attempt']):
        raise ValueError('sync_attempt')
    assets = data['assets']
    if not isinstance(assets, list) or len(assets) > 128 or type(data['verified']) is not bool:
        raise ValueError('sync_assets')
    seen = set()
    for asset in assets:
        if not isinstance(asset, dict) or set(asset) != {'path', 'sha256'}:
            raise ValueError('sync_asset')
        path, digest = asset['path'], asset['sha256']
        if not isinstance(path, str) or len(path) > 220 or not PATH.fullmatch(path) or '..' in path or path in seen:
            raise ValueError('sync_path')
        if not isinstance(digest, str) or not HEX.fullmatch(digest):
            raise ValueError('sync_hash')
        seen.add(path)
    for field in ('phase', 'duration', 'age'):
        value = data[field]
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0 or value > 3600:
            raise ValueError('sync_number')
    if data['age'] > 2 or data['phase'] > data['duration']:
        raise ValueError('sync_phase')
    with links.lock:
        links.prune()
        user = links.users.get(token)
        peer = links.users.get(user['peer']) if user else None
        if not peer:
            raise ValueError('sync_link')
        agreement = user.get('offer') or user.get('replay')
        if not agreement or agreement['id'] != data['attempt'] or agreement.get('status', 'accepted') != 'accepted':
            raise ValueError('sync_consent')
        now = links.clock()
        if 'at' in agreement and now-agreement['at'] > 1800:
            raise ValueError('sync_expired')
        user['sync'] = dict(data, received=now)
        other = peer.get('sync')
        if not other or other['attempt'] != data['attempt'] or now-other['received'] > 2:
            return {'peer': None}
        return {'peer': {k: v for k, v in other.items() if k != 'received'},
                'age': now-other['received']+other['age']}
