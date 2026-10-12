import json
import os
import sqlite3
import tempfile
import unittest
from reports import Reports, validate


class ReportTests(unittest.TestCase):
    def test_playback_context_is_bounded_and_excludes_free_text(self):
        context = dict(PairState='paired', Anchor=True, AlignStarted=False, PreparationStarted=True,
                       Preparing=True, Ready=False, StableFrames=0, ResourceStatus='none',
                       LocalAssets=0, PeerAssets=0, LocalVerified=False, PeerVerified=False, SyncHttpStatus=0,
                       ActorMode=1, EmoteId=219, Emoting=True, StationaryDance=True,
                       HeightDelta=-.05, SoundFailure='idle_timeout')
        data = dict(self.data, attempt='b'*64, context=context)
        for reason in ('actor', 'manual', 'timeout', 'position', 'distance', 'height'):
            validate(dict(data, context=dict(context, SoundFailure='motion_' + reason)))
        self.assertTrue(self.store.add(data, 'test-ip'))
        with self.store.connect() as db:
            self.assertEqual(json.loads(db.execute('SELECT payload FROM reports').fetchone()[0])['context'], context)
        for change in (dict(ActorMode=256), dict(EmoteId=-1), dict(Emoting=1),
                       dict(HeightDelta=float('nan')), dict(SoundFailure='private path'), dict(Log='private log')):
            with self.assertRaises(ValueError): validate(dict(data, context=dict(context, **change)))

    def test_import_failure_stage_and_kind(self):
        validate(dict(self.data, code='transfer_failed', detail='import_collections_index'))
        with self.assertRaises(ValueError):
            validate(dict(self.data, code='transfer_failed', detail='import_arbitrary_private_text'))

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

    def test_resource_context(self):
        context = dict(PairState='paired', Anchor=False, AlignStarted=True, PreparationStarted=True,
                       Preparing=False, Ready=False, StableFrames=3, ResourceStatus='transport',
                       LocalAssets=12, PeerAssets=0, LocalVerified=False, PeerVerified=False, SyncHttpStatus=400)
        self.assertEqual(json.loads(validate(dict(self.data, context=context)))['context'], context)
        for change in (dict(ResourceStatus='private path'), dict(LocalAssets=129), dict(PeerVerified=1), dict(SyncHttpStatus=999)):
            with self.assertRaises(ValueError): validate(dict(self.data, context=dict(context, **change)))

    def test_disk_excludes_ip(self):
        self.store.add(self.data, 'private-ip-marker')
        with self.store.connect() as db:
            payload = db.execute('SELECT payload FROM reports').fetchone()[0]
            self.assertEqual(json.loads(payload), self.data)
            self.assertNotIn('private-ip-marker', payload)

    def test_context_and_failure_checkpoints(self):
        context = dict(PairState='paired', Anchor=True, AlignStarted=False, PreparationStarted=True,
                       Preparing=False, Ready=False, StableFrames=0)
        data = dict(self.data, code='launch_cancelled', detail='post_prepare_alignment', context=context)
        self.assertEqual(json.loads(validate(data)), data)
        for change in [dict(Name='private'), dict(PairState='secret'), dict(Ready=1),
                       dict(StableFrames=-1), dict(StableFrames=True)]:
            with self.assertRaises(ValueError):
                validate(dict(data, context=dict(context, **change)))


if __name__ == '__main__':
    unittest.main()
