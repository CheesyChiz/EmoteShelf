import unittest
from server import PairStore


class Tests(unittest.TestCase):
    def test_mutual_ready_and_cancel(self):
        s = PairStore(lambda: 100)
        a = s.ready('a'*64, 'b'*64, 'c'*64)
        self.assertEqual(s.get(a)['status'], 'waiting')
        b = s.ready('b'*64, 'a'*64, 'c'*64)
        self.assertEqual(s.get(a)['start'], 102000)
        self.assertEqual(s.get(a)['start'], s.get(b)['start'])
        s.cancel(a)
        self.assertEqual(s.get(b)['status'], 'cancelled')

    def test_wrong_target_or_animation_cannot_start(self):
        s = PairStore()
        a = s.ready('a'*64, 'b'*64, 'c'*64)
        s.ready('b'*64, 'a'*64, 'd'*64)
        self.assertEqual(s.get(a)['status'], 'waiting')
        s.ready('b'*64, 'e'*64, 'c'*64)
        self.assertEqual(s.get(a)['status'], 'waiting')

    def test_expiry_and_validation(self):
        now = [100]
        s = PairStore(lambda: now[0])
        token = s.ready('a'*64, 'b'*64, 'c'*64)
        now[0] = 161
        self.assertIsNone(s.get(token))
        with self.assertRaises(ValueError):
            s.ready('a'*64, 'a'*64, 'c'*64)
        with self.assertRaises(ValueError):
            s.ready('/command', 'b'*64, 'c'*64)


if __name__ == '__main__':
    unittest.main()
