"""Ephemeral reciprocal-consent barrier. Accepts no commands or coordinates."""
import json
import re
import secrets
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from links import LinkStore

HEX = re.compile(r"^[0-9a-f]{64}$")
TOKEN = re.compile(r"^[A-Za-z0-9_-]{43}$")


class PairStore:
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.lock = threading.Lock()
        self.sessions = {}

    def cancel_locked(self, token):
        item = self.sessions.pop(token, None)
        if item and item['peer'] in self.sessions:
            peer = self.sessions[item['peer']]
            if peer['status'] != 'finished':
                peer['status'] = 'cancelled'

    def prune(self):
        now = self.clock()
        for token, item in list(self.sessions.items()):
            lease = 1.5 if item['status'] == 'scheduled' else 5
            if item['expires'] <= now or now - item['seen'] > lease:
                self.cancel_locked(token)

    def ready(self, identity, target, animation, align=False):
        if not all(isinstance(v, str) and HEX.fullmatch(v) for v in (identity, target, animation)) or identity == target or not isinstance(align, bool):
            raise ValueError('Invalid pair identifiers')
        with self.lock:
            self.prune()
            # A guessed character hash cannot cancel or replace an existing consent.
            if any(v['self'] == identity for v in self.sessions.values()):
                raise ValueError('Already waiting')
            if len(self.sessions) >= 512:
                raise ValueError('Relay is busy')
            token = secrets.token_urlsafe(32)
            item = dict(self=identity, target=target, animation=animation, align=align,
                        expires=self.clock() + 60, seen=self.clock(), status='waiting',
                        prepared=False, start=0, peer=None)
            self.sessions[token] = item
            for other_token, other in self.sessions.items():
                if other_token != token and other['status'] == 'waiting' and other['self'] == target and other['target'] == identity and other['animation'] == animation:
                    item.update(status='paired', peer=other_token)
                    other.update(status='paired', peer=token)
                    break
            return token

    def get(self, token):
        with self.lock:
            self.prune()
            item = self.sessions.get(token)
            if not item:
                return None
            item['seen'] = self.clock()
            peer = self.sessions.get(item['peer'])
            return dict(status=item['status'], delay_ms=max(0, round((item['start'] - self.clock()) * 1000)),
                        align=bool(peer and item['align'] and peer['align']), anchor=item['self'] < item['target'])

    def prepared(self, token):
        with self.lock:
            self.prune()
            item = self.sessions.get(token)
            if not item or item['status'] not in ('paired', 'scheduled'):
                raise ValueError('Pair is not ready')
            item['seen'] = self.clock()
            item['prepared'] = True
            peer = self.sessions.get(item['peer'])
            if peer and peer['prepared'] and item['status'] == 'paired':
                start = self.clock() + 2
                item.update(status='scheduled', start=start)
                peer.update(status='scheduled', start=start)

    def finished(self, token):
        with self.lock:
            item = self.sessions.get(token)
            if not item or item['status'] != 'scheduled' or self.clock() < item['start'] - .1:
                raise ValueError('Not started')
            item.update(status='finished', expires=self.clock() + 5, seen=self.clock())

    def cancel(self, token):
        with self.lock:
            self.cancel_locked(token)


store = PairStore()
links = LinkStore()
rate_lock = threading.Lock()
rates = {}


class Handler(BaseHTTPRequestHandler):
    server_version = 'EmoteShelfRelay/2'

    def log_message(self, *args):
        pass

    def reply(self, code, payload):
        data = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Cache-Control', 'no-store')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def allowed(self):
        address = self.headers.get('X-Forwarded-For', self.client_address[0]).split(',')[0].strip()
        now = int(time.monotonic())
        with rate_lock:
            for key in list(rates):
                if rates[key][0] < now - 2:
                    del rates[key]
            second, count = rates.get(address, (now, 0))
            rates[address] = (now, count + 1 if second == now else 1)
            return rates[address][1] <= 30

    def token(self):
        value = self.headers.get('Authorization', '').removeprefix('Bearer ')
        if not TOKEN.fullmatch(value):
            raise ValueError('Missing capability')
        return value

    def do_GET(self):
        if not self.allowed():
            return self.reply(429, dict(error='Rate limited'))
        if self.path == '/health':
            return self.reply(200, dict(service='EmoteShelf pair relay', protocol=2))
        if self.path == '/v1/session':
            try:
                result = store.get(self.token())
                return self.reply(200 if result else 404, result or dict(error='Expired'))
            except ValueError:
                return self.reply(401, dict(error='Missing capability'))
        self.reply(404, dict(error='Not found'))

    def do_POST(self):
        if not self.allowed():
            return self.reply(429, dict(error='Rate limited'))
        try:
            if self.path in ('/v2/connect', '/v2/link'):
                length = int(self.headers.get('Content-Length', '0'))
                if not 0 < length <= 1024:
                    return self.reply(413, dict(error='Invalid body size'))
                data = json.loads(self.rfile.read(length))
                if not isinstance(data, dict):
                    raise ValueError('Invalid message')
                if self.path == '/v2/connect':
                    if set(data) != {'identity'}:
                        raise ValueError('Invalid message')
                    return self.reply(200, dict(token=links.register(data['identity'])))
                if set(data) != {'action', 'target', 'invitation'} or not all(isinstance(v, str) for v in data.values()):
                    raise ValueError('Invalid message')
                return self.reply(200, links.action(self.token(), **data))
            if self.path in ('/v1/prepared', '/v1/finished'):
                action = store.prepared if self.path.endswith('prepared') else store.finished
                action(self.token())
                return self.reply(200, dict(ok=True))
            if self.path != '/v1/ready':
                return self.reply(404, dict(error='Not found'))
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= 1024:
                return self.reply(413, dict(error='Invalid body size'))
            data = json.loads(self.rfile.read(length))
            if not isinstance(data, dict) or set(data) != {'self', 'target', 'animation', 'align'}:
                raise ValueError('Invalid message')
            token = store.ready(data['self'], data['target'], data['animation'], data['align'])
            self.reply(200, dict(token=token))
        except (ValueError, TypeError, KeyError):
            self.reply(400, dict(error='Invalid request or unavailable pair'))

    def do_DELETE(self):
        if not self.allowed():
            return self.reply(429, dict(error='Rate limited'))
        if self.path != '/v1/session':
            return self.reply(404, dict(error='Not found'))
        try:
            store.cancel(self.token())
            self.reply(200, dict(status='cancelled'))
        except ValueError:
            self.reply(401, dict(error='Missing capability'))

    def setup(self):
        super().setup()
        self.connection.settimeout(5)


class BoundedServer(ThreadingHTTPServer):
    workers = threading.BoundedSemaphore(24)

    def process_request(self, request, address):
        if not self.workers.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, address)
        except Exception:
            self.workers.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self.workers.release()


if __name__ == '__main__':
    BoundedServer(('127.0.0.1', 8765), Handler).serve_forever()
