import unittest
from links import LinkStore
from sync import exchange

class SyncTests(unittest.TestCase):
    def setUp(self):
        self.now = 100
        self.links = LinkStore(lambda: self.now)
        self.a = self.links.register('a'*64)
        self.b = self.links.register('b'*64)
        invite = self.links.action(self.a, 'invite', target='b'*64)['invitation']
        self.links.action(self.b, 'accept', invitation=invite)
        self.offer = dict(id='1'*64, family='2'*64, command='3'*64, role='4'*64)
        self.links.action(self.a, 'propose', offer=self.offer)
        self.data = dict(attempt='1'*64, assets=[dict(path='chara/human/test.pap', sha256='5'*64)],
                         verified=False, phase=1., duration=10., age=.1)
    def accept(self):
        self.links.action(self.b, 'accept_launch', invitation='1'*64)
    def test_requires_accepted_launch(self):
        with self.assertRaises(ValueError): exchange(self.links, self.a, self.data)
        self.accept()
        self.assertIsNone(exchange(self.links, self.a, self.data)['peer'])
        self.assertEqual(exchange(self.links, self.b, self.data)['peer']['assets'], self.data['assets'])
    def test_age_and_stale_peer(self):
        self.accept()
        exchange(self.links, self.a, self.data)
        self.now += .5
        self.assertAlmostEqual(exchange(self.links, self.b, self.data)['age'], .6)
        self.now += 2
        self.assertIsNone(exchange(self.links, self.b, self.data)['peer'])
    def test_rejects_paths_and_unbounded_data(self):
        self.accept()
        for path in ('C:/private.txt', '../secret.pap', 'chara/../../file.pap', 'chara/code.dll'):
            with self.assertRaises(ValueError): exchange(self.links, self.a, dict(self.data, assets=[dict(path=path, sha256='5'*64)]))
        for key, value in [('phase', float('nan')), ('duration', 5000), ('age', 3), ('verified', 'true'), ('assets', self.data['assets']*129)]:
            with self.assertRaises(ValueError): exchange(self.links, self.a, dict(self.data, **{key:value}))
    def test_unlink_and_new_attempt_invalidate(self):
        self.accept()
        exchange(self.links, self.a, self.data)
        self.links.action(self.a, 'cancel_launch', invitation='1'*64)
        with self.assertRaises(ValueError): exchange(self.links, self.b, self.data)
        self.links.action(self.a, 'disconnect')
        with self.assertRaises(ValueError): exchange(self.links, self.a, self.data)
