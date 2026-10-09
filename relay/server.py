"""Opt-in pair readiness relay. No mod files, chat text or game commands accepted."""
import json
import re
import secrets
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HEX = re.compile(r"^[0-9a-f]{64}$")


class PairStore:
    def __init__(self, clock=time.time):
        self.clock = clock
        self.lock = threading.Lock()
        self.sessions = {}

    def prune(self):
        now = self.clock()
        self.sessions = {k: v for k, v in self.sessions.items() if v['expires'] > now}

    def ready(self, identity, target, animation):
        if not all(isinstance(v, str) and HEX.fullmatch(v) for v in (identity, target, animation)) or identity == target:
            raise ValueError('Invalid pair identifiers')
        with self.lock:
            self.prune()
            if len(self.sessions) >= 512:
                raise ValueError('Relay is busy')
            # One outstanding consent per reported character, not a persistent room.
            for token, item in list(self.sessions.items()):
                if item['self'] == identity:
                    self.cancel_locked(token)
            token = secrets.token_urlsafe(32)
            item = dict(self=identity, target=target, animation=animation,
                        expires=self.clock() + 60, status='waiting', start=0, peer=None)
            self.sessions[token] = item
            for other_token, other in self.sessions.items():
                if other_token != token and other['status'] == 'waiting' and other['self'] == target and other['target'] == identity and other['animation'] == animation:
                    start = int((self.clock() + 2) * 1000)
                    item.update(status='scheduled', start=start, peer=other_token)
                    other.update(status='scheduled', start=start, peer=token)
                    break
            return token

    def cancel_locked(self, token):
        item = self.sessions.pop(token, None)
        if item and item['peer'] in self.sessions:
            self.sessions[item['peer']]['status'] = 'cancelled'

    def cancel(self, token):
        with self.lock:
            self.cancel_locked(token)

    def get(self, token):
        with self.lock:
            self.prune()
            item = self.sessions.get(token)
            return dict(status=item['status'], start=item['start'], now=int(self.clock() * 1000)) if item else None


store = PairStore()
rate_lock = threading.Lock()
rates = {}


class Handler(BaseHTTPRequestHandler):
    server_version = 'EmoteShelfRelay/1'

    def log_message(self, *args):
        pass  # Never log session bearer tokens or character identifiers.

    def reply(self, code, payload):
        data = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Cache-Control', 'no-store')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def allowed(self):
        # Service binds loopback only; Caddy provides the client address.
        address = self.headers.get('X-Forwarded-For', self.client_address[0]).split(',')[0].strip()
        now = int(time.time())
        with rate_lock:
            for key in list(rates):
                if rates[key][0] < now - 2:
                    del rates[key]
            second, count = rates.get(address, (now, 0))
            rates[address] = (now, count + 1 if second == now else 1)
            return rates[address][1] <= 30

    def do_GET(self):
        if self.path == '/health':
            return self.reply(200, dict(service='EmoteShelf pair relay', protocol=1))
        if not self.allowed():
            return self.reply(429, dict(error='Rate limited'))
        if self.path.startswith('/v1/session/'):
            result = store.get(self.path[len('/v1/session/'):])
            return self.reply(200 if result else 404, result or dict(error='Expired'))
        self.reply(404, dict(error='Not found'))

    def do_POST(self):
        if self.path != '/v1/ready':
            return self.reply(404, dict(error='Not found'))
        if not self.allowed():
            return self.reply(429, dict(error='Rate limited'))
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= 1024:
                return self.reply(413, dict(error='Invalid body size'))
            data = json.loads(self.rfile.read(length))
            if not isinstance(data, dict) or set(data) != {'self', 'target', 'animation'}:
                raise ValueError('Invalid message')
            token = store.ready(data['self'], data['target'], data['animation'])
            self.reply(200, dict(token=token))
        except (ValueError, TypeError, KeyError):
            self.reply(400, dict(error='Invalid readiness request'))

    def do_DELETE(self):
        if not self.allowed():
            return self.reply(429, dict(error='Rate limited'))
        if not self.path.startswith('/v1/session/'):
            return self.reply(404, dict(error='Not found'))
        store.cancel(self.path[len('/v1/session/'):])
        self.reply(200, dict(status='cancelled'))

    def setup(self):
        super().setup()
        self.connection.settimeout(5)


if __name__ == '__main__':
    ThreadingHTTPServer(('127.0.0.1', 8765), Handler).serve_forever()
