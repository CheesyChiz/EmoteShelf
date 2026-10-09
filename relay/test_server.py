import unittest
from server import PairStore


class Tests(unittest.TestCase):
    def setUp(self):
        self.now = 100
        self.s = PairStore(lambda: self.now)

    def pair(self, align=True):
        a = self.s.ready('a'*64, 'b'*64, 'c'*64, align)
        b = self.s.ready('b'*64, 'a'*64, 'c'*64, True)
        return a, b

    def test_preparation_barrier(self):
        a, b = self.pair()
        self.assertEqual(self.s.get(a)['status'], 'paired')
        self.s.prepared(a)
        self.assertEqual(self.s.get(b)['status'], 'paired')
        self.s.prepared(b)
        self.assertEqual(self.s.get(a)['delay_ms'], 2000)
        self.assertEqual(self.s.get(a)['status'], 'scheduled')
        self.now += 1
        self.assertEqual(self.s.get(b)['delay_ms'], 1000)
        self.s.prepared(a)
        self.assertEqual(self.s.get(a)['delay_ms'], 1000)

    def test_mutual_alignment_only(self):
        a, b = self.pair(False)
        self.assertFalse(self.s.get(a)['align'])
        self.assertTrue(self.s.get(a)['anchor'])
        self.assertFalse(self.s.get(b)['anchor'])

    def test_cancel_propagates(self):
        a, b = self.pair()
        self.s.cancel(a)
        self.assertEqual(self.s.get(b)['status'], 'cancelled')
        with self.assertRaises(ValueError):
            self.s.prepared(b)

    def test_disconnect_cancels_peer(self):
        a, b = self.pair()
        self.now += 4
        self.s.get(a)
        self.now += 2
        self.assertEqual(self.s.get(a)['status'], 'cancelled')

    def test_finish_does_not_cancel_peer(self):
        a, b = self.pair()
        self.s.prepared(a)
        self.s.prepared(b)
        with self.assertRaises(ValueError):
            self.s.finished(a)
        self.now += 1
        self.s.get(a)
        self.s.get(b)
        self.now += 1
        self.s.finished(a)
        self.assertEqual(self.s.get(b)['status'], 'scheduled')

    def test_countdown_loses_stale_peer(self):
        a, b = self.pair()
        self.s.prepared(a)
        self.s.prepared(b)
        self.now += .8
        self.s.get(a)
        self.now += .8
        self.assertEqual(self.s.get(a)['status'], 'cancelled')

    def test_wrong_mod_does_not_match(self):
        a = self.s.ready('a'*64, 'b'*64, 'c'*64)
        self.s.ready('b'*64, 'a'*64, 'd'*64)
        self.assertEqual(self.s.get(a)['status'], 'waiting')

    def test_guess_cannot_replace_consent(self):
        a, b = self.pair()
        with self.assertRaises(ValueError):
            self.s.ready('a'*64, 'b'*64, 'c'*64)
        self.assertEqual(self.s.get(b)['status'], 'paired')

    def test_invalid_and_expiry(self):
        for values in [('a'*64, 'a'*64, 'c'*64), ('/command', 'b'*64, 'c'*64)]:
            with self.assertRaises(ValueError):
                self.s.ready(*values)
        a, b = self.pair()
        self.now += 61
        self.assertIsNone(self.s.get(a))


if __name__ == '__main__':
    unittest.main()
