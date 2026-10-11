import base64
import hashlib
import unittest
from links import LinkStore
from transfers import Transfers


class StreamingTests(unittest.TestCase):
    def setUp(self):
        self.links = LinkStore()
        self.a = self.links.register('a' * 64)
        self.b = self.links.register('b' * 64)
        self.links.users[self.a]['peer'] = self.b
        self.links.users[self.b]['peer'] = self.a
        self.store = Transfers()
        self.act(self.b, action='poll')

    def act(self, token, **data):
        return self.store.action(self.links, token, data, protocol=7)

    def offer(self, payload):
        return self.act(self.a, action='offer', name='test.pmp', size=len(payload),
                        sha256=hashlib.sha256(payload).hexdigest())['transfer']['id']

    def upload(self, key, offset, data):
        return self.act(self.a, action='upload', id=key, offset=offset,
                        chunk=base64.b64encode(data).decode())['transfer']

    def download(self, key, offset):
        return base64.b64decode(self.act(self.b, action='download', id=key, offset=offset)['chunk'])

    def test_stream_before_complete_bounded_and_retryable(self):
        chunk = b'x' * self.store.CHUNK
        payload = chunk * 84
        key = self.offer(payload)
        with self.assertRaises(ValueError): self.download(key, 0)
        self.act(self.b, action='accept', id=key)
        self.assertEqual(self.download(key, 0), b'')
        uploaded = 0
        while uploaded < self.store.WINDOW:
            uploaded = self.upload(key, uploaded, chunk)['offset']
        self.assertEqual(self.upload(key, uploaded, chunk)['offset'], uploaded)
        self.assertEqual(self.store.items[key]['state'], 'accepted')
        self.assertEqual(self.download(key, 0), chunk)
        self.assertEqual(self.download(key, 0), chunk)  # Lost HTTP reply.
        received = chunk
        while len(received) < len(payload):
            data = self.download(key, len(received))
            received += data
            if uploaded < len(payload):
                uploaded = self.upload(key, uploaded, chunk)['offset']
            self.assertLessEqual(len(self.store.items[key]['payload']), self.store.WINDOW)
        self.assertEqual(received, payload)
        with self.assertRaises(ValueError): self.act(self.b, action='complete', id=key)
        self.assertEqual(self.download(key, len(payload)), b'')
        self.assertEqual(self.download(key, len(payload)), b'')
        self.assertEqual(len(self.store.items[key]['payload']), 0)
        self.act(self.b, action='complete', id=key)
        self.assertFalse(self.store.items)

    def test_hash_failure_discards_buffer(self):
        key = self.offer(b'good')
        self.act(self.b, action='accept', id=key)
        with self.assertRaisesRegex(ValueError, 'transfer_hash'): self.upload(key, 0, b'evil')
        self.assertFalse(self.store.items)

    def test_requires_streaming_peer(self):
        self.links.users[self.b]['transfer_protocol'] = 5
        with self.assertRaisesRegex(ValueError, 'transfer_update_both_clients'): self.offer(b'x')

    def test_cancel_releases_buffer(self):
        key = self.offer(b'xx')
        self.act(self.b, action='accept', id=key)
        self.upload(key, 0, b'x')
        self.act(self.a, action='cancel', id=key)
        self.assertFalse(self.store.items)
