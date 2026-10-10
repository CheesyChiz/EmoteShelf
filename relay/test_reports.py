import json
import os
import sqlite3
import tempfile
import unittest
from reports import Reports, validate


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.now = 2000000
        self.store = Reports(os.path.join(self.tmp.name, 'reports.db'), lambda: self.now, max_rows=2)
        self.data = dict(id='a'*32, version='0.3.5.0', attempt='', code='alignment_failed', detail='unstable', state='linked',
                         fresh=True, lightless=True, distance=.01, angle=.2)

    def tearDown(self):
        self.tmp.cleanup()

    def count(self):
        with self.store.connect() as db:
            return db.execute('SELECT COUNT(*) FROM reports').fetchone()[0]

    def test_dedup_and_rate(self):
        for _ in range(3):
            self.assertTrue(self.store.add(self.data, 'test-ip'))
        self.assertFalse(self.store.add(self.data, 'test-ip'))
        self.assertEqual(self.count(), 1)

    def test_retention_and_capacity(self):
        for i in range(4):
            self.now += 61
            self.store.add(dict(self.data, id=f'{i:032x}'), 'test-ip')
        self.assertEqual(self.count(), 2)
        self.now += 14*86400+1
        self.store.prune()
        self.assertEqual(self.count(), 0)

    def test_closed_schema(self):
        for change in [dict(name='name'), dict(code='arbitrary text'), dict(distance=float('nan')),
                       dict(angle=100), dict(attempt='secret'), dict(fresh=1)]:
            with self.assertRaises((ValueError, TypeError)):
                validate(dict(self.data, **change))

    def test_disk_excludes_ip(self):
        self.store.add(self.data, 'private-ip-marker')
        with self.store.connect() as db:
            payload = db.execute('SELECT payload FROM reports').fetchone()[0]
            self.assertEqual(json.loads(payload), self.data)
            self.assertNotIn('private-ip-marker', payload)


if __name__ == '__main__':
    unittest.main()
