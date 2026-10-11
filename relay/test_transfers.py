import base64
import hashlib
import unittest
import json
from links import LinkStore
from transfers import Transfers


class TransferTests(unittest.TestCase):
    def test_128_mib_package_boundary(self):
        self.assertEqual(self.store.MAX_PACKAGE, 128 * 1024 * 1024)
        with self.assertRaises(ValueError):
            self.act(self.a, **dict(self.offer, size=self.store.MAX_PACKAGE + 1))
        result = self.act(self.a, **dict(self.offer, size=self.store.MAX_PACKAGE))
        self.assertEqual(result['transfer']['size'], self.store.MAX_PACKAGE)
        self.act(self.a, action='cancel', id=result['transfer']['id'])

    def test_transfer_shared_dance_keeps_mode_and_same_command(self):
        for user in (self.a, self.b): self.links.users[user]['dance'] = True
        self.store.action(self.links, self.b, dict(action='poll'), protocol=5)
        launch = json.dumps(dict(PairId='1'*64, Family='2'*64, SenderCommand=hashlib.sha256(b'/stepdance').hexdigest(),
                                 SenderRole='4'*64, Command='/stepdance', Dance=True))
        key = self.store.action(self.links, self.a, dict(self.offer, launch=launch), protocol=5)['transfer']['id']
        self.act(self.b, action='accept', id=key)
        self.act(self.a, action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode())
        self.act(self.b, action='complete', id=key)
        self.assertTrue(self.links.users[self.a]['offer']['dance'])
        self.assertTrue(self.links.users[self.b]['offer']['dance'])

    def test_accept_retry_preserves_ready_payload_and_consent(self):
        key = self.act(self.a, **self.offer)['transfer']['id']
        for _ in range(2):
            self.assertEqual(self.act(self.b, action='accept', id=key)['transfer']['state'], 'accepted')
        self.act(self.a, action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode())
        ready = self.act(self.b, action='accept', id=key)['transfer']
        self.assertEqual((ready['state'], ready['offset']), ('ready', len(self.payload)))
        self.assertEqual(bytes(self.store.items[key]['payload']), self.payload)
        with self.assertRaises(ValueError): self.act(self.a, action='accept', id=key)
        with self.assertRaises(ValueError): self.act(self.c, action='accept', id=key)
        self.act(self.b, action='reject', id=key)
        with self.assertRaises(ValueError): self.act(self.b, action='accept', id=key)
        self.assertFalse(self.store.items)

    def test_completed_outcome_survives_lost_response_and_duplicate_command(self):
        key = self.act(self.a, **self.offer)['transfer']['id']
        self.act(self.b, action='accept', id=key)
        self.act(self.a, action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode())
        for _ in range(3):
            self.assertEqual(self.act(self.b, action='complete', id=key)['outcome'], 'completed')
            self.assertEqual(self.act(self.a, action='poll')['outcome'], 'completed')
        with self.assertRaises(ValueError): self.act(self.a, action='complete', id=key)
        with self.assertRaises(ValueError): self.act(self.b, action='fail', id=key)
        self.store.cooldowns.clear()
        new_key = self.act(self.a, **self.offer)['transfer']['id']
        replay = self.act(self.b, action='complete', id=key)
        self.assertEqual(replay['outcome'], '')
        self.assertEqual(replay['transfer']['id'], new_key)
        self.assertEqual(replay['transfer']['state'], 'offered')

    def test_outcome_expires(self):
        key = self.act(self.a, **self.offer)['transfer']['id']
        self.act(self.b, action='reject', id=key)
        self.now += self.store.TTL + 1
        for user in self.links.users.values(): user['seen'] = self.now
        self.assertEqual(self.act(self.a, action='poll')['outcome'], '')
        self.assertFalse(self.store.terminal_receipts)

    def test_import_failure_notifies_sender_and_is_retry_safe(self):
        key = self.act(self.a, **self.offer)['transfer']['id']
        with self.assertRaises(ValueError): self.act(self.b, action='fail', id=key)
        self.act(self.b, action='accept', id=key)
        self.act(self.a, action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode())
        with self.assertRaises(ValueError): self.act(self.a, action='fail', id=key)
        with self.assertRaises(ValueError): self.act(self.b, action='fail', id=key, reason='untrusted')
        self.assertEqual(self.act(self.b, action='fail', id=key)['outcome'], 'installation_failed')
        self.assertFalse(self.store.items)
        self.assertEqual(self.act(self.b, action='fail', id=key)['outcome'], 'installation_failed')
        self.assertEqual(self.act(self.a, action='poll')['outcome'], 'installation_failed')
        self.assertIsNone(self.links.action(self.a)['launch'])
        self.assertIsNone(self.links.action(self.b)['launch'])
        with self.assertRaises(ValueError): self.act(self.a, action='fail', id=key)

    def test_shared_launch_waits_for_import_and_requires_new_clients(self):
        self.setUp()
        launch = json.dumps(dict(PairId='1'*64, Family='2'*64, SenderCommand='3'*64, SenderRole='4'*64))
        offer = dict(self.offer, launch=launch)
        with self.assertRaises(ValueError):
            self.store.action(self.links, self.a, offer, protocol=5)
        self.store.action(self.links, self.b, dict(action='poll'), protocol=5)
        item = self.store.action(self.links, self.a, offer, protocol=5)['transfer']
        key = item['id']
        self.store.action(self.links, self.b, dict(action='accept', id=key), protocol=5)
        self.assertIsNone(self.links.action(self.a)['launch'])
        self.store.action(self.links, self.a, dict(action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode()), protocol=5)
        self.assertIsNone(self.links.action(self.a)['launch'])
        with self.assertRaises(ValueError):
            self.store.action(self.links, self.a, dict(action='complete', id=key), protocol=5)
        self.store.action(self.links, self.b, dict(action='complete', id=key), protocol=5)
        result = self.links.action(self.b)['launch']
        self.assertEqual((result['id'], result['status'], result['incoming']), ('1'*64, 'accepted', True))
        original = self.links.users[self.a]['offer']
        self.store.action(self.links, self.b, dict(action='complete', id=key), protocol=5)
        self.assertIs(self.links.users[self.a]['offer'], original)

    def setUp(self):
        self.now = 100
        self.links = LinkStore(lambda: self.now)
        self.store = Transfers(lambda: self.now)
        self.a = self.links.register('a'*64)
        self.b = self.links.register('b'*64)
        self.c = self.links.register('c'*64)
        invitation = self.links.action(self.a, 'invite', 'b'*64)['invitation']
        self.links.action(self.b, 'accept', invitation=invitation)
        self.payload = b'not an archive; archive validation belongs to the client'
        self.offer = dict(action='offer', name='Example.pmp', size=len(self.payload), sha256=hashlib.sha256(self.payload).hexdigest())

    def act(self, token, **data):
        return self.store.action(self.links, token, data)

    def test_reciprocal_consent_and_integrity(self):
        item = self.act(self.a, **self.offer)['transfer']
        key = item['id']
        upload = dict(action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode())
        with self.assertRaises(ValueError): self.act(self.a, **upload)
        with self.assertRaises(ValueError): self.act(self.a, action='accept', id=key)
        with self.assertRaises(ValueError): self.act(self.c, action='accept', id=key)
        self.act(self.b, action='accept', id=key)
        with self.assertRaises(ValueError): self.act(self.b, **upload)
        self.assertEqual(self.act(self.a, **upload)['transfer']['state'], 'ready')
        with self.assertRaises(ValueError): self.act(self.a, action='download', id=key, offset=0)
        self.assertEqual(base64.b64decode(self.act(self.b, action='download', id=key, offset=0)['chunk']), self.payload)
        self.act(self.b, action='complete', id=key)
        self.assertIsNone(self.act(self.a, action='poll')['transfer'])

    def test_launch_offer_is_bounded_and_immutable(self):
        for invalid in (None, {}, 'x'*8193):
            with self.assertRaises(ValueError):
                self.act(self.a, **dict(self.offer, launch=invalid))
        launch = '{"Label":"Wave","Command":"/wave","Pose":null,"Options":{}}'
        item = self.act(self.a, **dict(self.offer, launch=launch))['transfer']
        self.assertEqual(self.act(self.b, action='poll')['transfer']['launch'], launch)
        with self.assertRaises(ValueError):
            self.act(self.b, action='accept', id=item['id'], launch='changed')
        self.assertEqual(self.act(self.b, action='accept', id=item['id'])['transfer']['launch'], launch)

    def test_expiry_disconnect_quota_hash(self):
        for bad in (0, -1, True, self.store.MAX_PACKAGE+1):
            with self.assertRaises(ValueError): self.act(self.a, **dict(self.offer, size=bad))
        key = self.act(self.a, **dict(self.offer, sha256='0'*64))['transfer']['id']
        self.act(self.b, action='accept', id=key)
        with self.assertRaises(ValueError):
            self.act(self.a, action='upload', id=key, offset=0, chunk=base64.b64encode(self.payload).decode())
        self.assertEqual(len(self.store.items), 0)
        self.store.cooldowns.clear()
        self.act(self.a, **self.offer)
        self.links.action(self.b, 'disconnect')
        with self.assertRaises(ValueError): self.act(self.a, action='poll')
        self.assertEqual(len(self.store.items), 0)

    def test_expiry_even_when_connected(self):
        self.act(self.a, **self.offer)
        self.now += self.store.TTL
        for user in self.links.users.values(): user['seen'] = self.now
        self.assertIsNone(self.act(self.a, action='poll')['transfer'])

    def test_reserved_quota(self):
        self.store.MAX_TOTAL = 1
        with self.assertRaises(ValueError): self.act(self.a, **self.offer)
