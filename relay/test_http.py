import http.client
import json
import threading
import unittest
import server
import tempfile
import os
import base64
import hashlib
from reports import Reports


class HttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        server.store = server.PairStore()
        cls.http = server.BoundedServer(('127.0.0.1', 0), server.Handler)
        cls.thread = threading.Thread(target=cls.http.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.http.shutdown()
        cls.http.server_close()
        cls.thread.join()

    def request(self, method, path, body=None, token=None):
        connection = http.client.HTTPConnection('127.0.0.1', self.http.server_port, timeout=2)
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        connection.request(method, path, json.dumps(body) if body else None, headers)
        response = connection.getresponse()
        result = (response.status, json.loads(response.read()))
        connection.close()
        return result

    def test_requires_capability(self):
        self.assertEqual(self.request('GET', '/v1/session')[0], 401)
        self.assertEqual(self.request('GET', '/v1/session/' + 'x'*43)[0], 404)
        self.assertEqual(self.request('GET', '/v1/session', token='x'*43)[0], 404)

    def test_stop_barrier_over_http(self):
        server.rates.clear()
        tokens = []
        try:
            for identity, target in [('6', '7'), ('7', '6')]:
                status, body = self.request('POST', '/v1/ready', dict(
                    self=identity*64, target=target*64, animation='8'*64,
                    align=True, stop_barrier=True))
                self.assertEqual(status, 200)
                tokens.append(body['token'])
            a, b = tokens
            self.assertEqual(self.request('POST', '/v1/stopped')[0], 400)
            self.assertEqual(self.request('POST', '/v1/stopped', token=a)[0], 200)
            self.assertFalse(self.request('GET', '/v1/session', token=a)[1]['stopped'])
            self.assertEqual(self.request('POST', '/v1/prepared', token=a)[0], 400)
            self.assertEqual(self.request('POST', '/v1/stopped', token=b)[0], 200)
            self.assertTrue(self.request('GET', '/v1/session', token=a)[1]['stopped'])
            for token in tokens:
                self.assertEqual(self.request('POST', '/v1/prepared', token=token)[0], 200)
            self.assertEqual(self.request('GET', '/v1/session', token=b)[1]['status'], 'scheduled')
        finally:
            for token in tokens: server.store.cancel(token)
            server.rates.clear()

    def test_v7_streams_over_http_before_upload_finishes(self):
        server.rates.clear()
        a = server.links.register('d' * 64)
        b = server.links.register('e' * 64)
        server.links.users[a]['peer'] = b
        server.links.users[b]['peer'] = a
        def act(token, **body):
            status, response = self.request('POST', '/v7/transfer', body, token)
            self.assertEqual(status, 200, response)
            return response
        try:
            act(b, action='poll')
            chunk = b'x' * 262144
            payload = chunk * 2
            key = act(a, action='offer', name='test.pmp', size=len(payload),
                      sha256=hashlib.sha256(payload).hexdigest())['transfer']['id']
            act(b, action='accept', id=key)
            act(a, action='upload', id=key, offset=0, chunk=base64.b64encode(chunk).decode())
            self.assertEqual(act(a, action='poll')['transfer']['state'], 'accepted')
            self.assertEqual(base64.b64decode(act(b, action='download', id=key, offset=0)['chunk']), chunk)
            act(b, action='download', id=key, offset=len(chunk))
            self.assertEqual(len(server.transfers.items[key]['payload']), 0)
            act(a, action='upload', id=key, offset=len(chunk), chunk=base64.b64encode(chunk).decode())
            self.assertEqual(base64.b64decode(act(b, action='download', id=key, offset=len(chunk))['chunk']), chunk)
            act(b, action='download', id=key, offset=len(payload))
            act(b, action='complete', id=key)
        finally:
            server.links.users.pop(a, None)
            server.links.users.pop(b, None)
            server.transfers.prune(server.links.users)
            server.rates.clear()

    def test_sync_private_schema(self):
        server.rates.clear()
        self.assertEqual(self.request('POST', '/v6/sync', {'attempt': '1'*64})[0], 400)
        self.assertEqual(self.request('GET', '/v6/sync')[0], 404)
        connection = http.client.HTTPConnection('127.0.0.1', self.http.server_port, timeout=2)
        connection.request('POST', '/v6/sync', headers={'Content-Length':'48001'})
        response = connection.getresponse()
        self.assertEqual(response.status, 413)
        response.read()
        connection.close()

    def test_transfer_route_is_private_and_bounded(self):
        server.rates.clear()
        self.assertEqual(self.request('POST', '/v4/transfer', {'action': 'poll'})[0], 400)
        self.assertEqual(self.request('POST', '/v4/transfer', {'action': 'poll'}, 'x'*43)[0], 400)
        # Oversize is rejected from headers before reading a body. Sending the body
        # concurrently with the early close produces a platform-dependent TCP reset.
        connection = http.client.HTTPConnection('127.0.0.1', self.http.server_port, timeout=2)
        connection.request('POST', '/v4/transfer', headers={'Content-Length': '360001', 'Authorization': 'Bearer ' + 'x'*43})
        response = connection.getresponse()
        self.assertEqual(response.status, 413)
        response.read()
        connection.close()
        self.assertEqual(self.request('GET', '/v4/transfer')[0], 404)
        server.rates.clear()

    def test_reports_closed_schema_and_no_public_read(self):
        server.rates.clear()
        with tempfile.TemporaryDirectory() as temp:
            server.reports = Reports(os.path.join(temp, 'reports.db'))
            try:
                data = dict(id='f'*32, version='0.3.5.0', attempt='', code='invite_blocked', detail='stale',
                            state='idle', fresh=False, lightless=True, distance=None, angle=None)
                self.assertEqual(self.request('POST', '/v3/report', data)[0], 202)
                self.assertEqual(self.request('POST', '/v3/report', dict(data, chat='no'))[0], 400)
                self.assertEqual(self.request('POST', '/v3/report', dict(data, chat='x'*2200))[0], 413)
                self.assertEqual(self.request('GET', '/v3/report')[0], 404)
            finally:
                server.reports = None
                server.rates.clear()

    def test_link_http(self):
        server.rates.clear()
        server.links = server.LinkStore()
        code, a = self.request('POST', '/v2/connect', {'identity': 'd'*64})
        self.assertEqual(code, 200)
        code, b = self.request('POST', '/v2/connect', {'identity': 'e'*64})
        self.assertEqual(code, 200)
        def action(token, name='poll', target='', invitation=''):
            return self.request('POST', '/v2/link', dict(action=name, target=target, invitation=invitation), token)
        self.assertEqual(action(None)[0], 400)
        self.assertEqual(action(a['token'], 'invite', 'e'*64)[1]['state'], 'outgoing')
        invitation = action(b['token'])[1]['invitation']
        self.assertEqual(action(b['token'], 'accept', invitation=invitation)[1]['state'], 'linked')
        self.assertEqual(action(a['token'])[1]['state'], 'linked')
        action(b['token'], 'close')
        self.assertEqual(action(a['token'])[1]['state'], 'idle')
        action(a['token'], 'close')
        server.rates.clear()

    def test_rejects_commands_and_coordinates(self):
        self.assertEqual(self.request('POST', '/v1/ready', {'command': '/dance'})[0], 400)
        self.assertEqual(self.request('POST', '/v1/ready', {'self': 'a'*64, 'target': 'b'*64, 'animation': 'c'*64, 'align': True, 'x': 12})[0], 400)

    def test_two_clients_and_cancellation(self):
        code, a = self.request('POST', '/v1/ready', {'self': 'a'*64, 'target': 'b'*64, 'animation': 'c'*64, 'align': True})
        self.assertEqual(code, 200)
        code, b = self.request('POST', '/v1/ready', {'self': 'b'*64, 'target': 'a'*64, 'animation': 'c'*64, 'align': True})
        self.assertEqual(code, 200)
        self.assertEqual(self.request('POST', '/v1/prepared', token=a['token'])[0], 200)
        self.assertEqual(self.request('GET', '/v1/session', token=b['token'])[1]['status'], 'paired')
        self.assertEqual(self.request('POST', '/v1/prepared', token=b['token'])[0], 200)
        self.assertEqual(self.request('GET', '/v1/session', token=a['token'])[1]['status'], 'scheduled')
        self.assertEqual(self.request('DELETE', '/v1/session', token=a['token'])[0], 200)
        self.assertEqual(self.request('GET', '/v1/session', token=b['token'])[1]['status'], 'cancelled')


if __name__ == '__main__':
    unittest.main()
