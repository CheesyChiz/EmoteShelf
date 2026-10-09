import unittest
from links import LinkStore


class LinkTests(unittest.TestCase):
    def setUp(self):
        self.now = 100
        self.store = LinkStore(lambda: self.now)
        self.a = self.store.register('a'*64)
        self.b = self.store.register('b'*64)
        self.c = self.store.register('c'*64)

    def invite(self):
        return self.store.action(self.a, 'invite', 'b'*64)

    def test_accept_requires_exact_invitation(self):
        self.invite()
        b = self.store.action(self.b)
        self.assertEqual(b['state'], 'incoming')
        with self.assertRaises(ValueError):
            self.store.action(self.b, 'accept', invitation='wrong')
        with self.assertRaises(ValueError):
            self.store.action(self.a, 'accept', invitation=b['invitation'])
        self.assertEqual(self.store.action(self.b, 'accept', invitation=b['invitation'])['state'], 'linked')
        self.assertEqual(self.store.action(self.a)['partner'], 'b'*64)
        self.assertEqual(self.store.action(self.a)['state'], 'linked')
        with self.assertRaises(ValueError):
            self.store.action(self.c, 'invite', 'a'*64)

    def test_decline_cancel_and_no_replacement(self):
        self.invite()
        with self.assertRaises(ValueError):
            self.store.register('a'*64)
        with self.assertRaises(ValueError):
            self.store.action(self.c, 'invite', 'b'*64)
        nonce = self.store.action(self.b)['invitation']
        self.store.action(self.b, 'decline', invitation=nonce)
        self.assertEqual(self.store.action(self.a)['state'], 'idle')
        with self.assertRaises(ValueError):
            self.invite()  # cooldown, even after decline

    def test_timeout_cannot_accept_old_nonce(self):
        nonce = self.invite()['invitation']
        for _ in range(6):
            self.now += 5
            self.store.action(self.a)
            self.store.action(self.b)
        self.assertEqual(self.store.action(self.b)['state'], 'idle')
        with self.assertRaises(ValueError):
            self.store.action(self.b, 'accept', invitation=nonce)

    def test_lost_peer_and_close_clear_link(self):
        nonce = self.invite()['invitation']
        self.store.action(self.b, 'accept', invitation=nonce)
        self.now += 5
        self.store.action(self.a)
        self.now += 4
        self.assertEqual(self.store.action(self.a)['state'], 'idle')
        with self.assertRaises(ValueError):
            self.store.action(self.b)
        self.store.action(self.a, 'close')
        self.store.register('a'*64)

    def test_disconnect_is_symmetric(self):
        nonce = self.invite()['invitation']
        self.store.action(self.b, 'accept', invitation=nonce)
        self.store.action(self.b, 'disconnect')
        self.assertEqual(self.store.action(self.a)['state'], 'idle')
        self.assertEqual(self.store.action(self.b)['state'], 'idle')


if __name__ == '__main__':
    unittest.main()
