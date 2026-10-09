import http.client
import json
import threading
import unittest
import server


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
