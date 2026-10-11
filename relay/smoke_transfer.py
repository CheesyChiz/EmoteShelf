"""Explicit live smoke test: synthetic identities/data only; no game users or mods."""
import base64
import hashlib
import json
import secrets
import urllib.request
import urllib.error


def main():
    def post(path, data, token=None):
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        path = path.replace('/v4/transfer', '/v5/transfer')
        request = urllib.request.Request('https://anarchyclan.ru' + path,
                                         json.dumps(data).encode(), headers)
        with urllib.request.urlopen(request, timeout=5) as response:
            return json.load(response)

    a = b = None
    try:
        identity_a, identity_b = secrets.token_hex(32), secrets.token_hex(32)
        a = post('/v2/connect', dict(identity=identity_a))['token']
        b = post('/v2/connect', dict(identity=identity_b))['token']
        def link(token, action='poll', target='', invitation=''):
            return post('/v2/link', dict(action=action, target=target, invitation=invitation), token)
        invitation = link(a, 'invite', identity_b)['invitation']
        assert link(b, 'accept', invitation=invitation)['state'] == 'linked'
        content = secrets.token_bytes(300000)
        pair_id = secrets.token_hex(32)
        launch = json.dumps(dict(Label='Synthetic wave', Command='/wave', Pose=None, Options={},
                                 PairId=pair_id, Family=secrets.token_hex(32),
                                 SenderCommand=secrets.token_hex(32), SenderRole=secrets.token_hex(32)))
        post('/v5/transfer', dict(action='poll'), b)
        offer = post('/v4/transfer', dict(action='offer', name='Synthetic smoke test', size=len(content),
                                         sha256=hashlib.sha256(content).hexdigest(), launch=launch), a)['transfer']
        assert post('/v4/transfer', dict(action='poll'), b)['transfer']['launch'] == launch
        key = offer['id']
        try:
            post('/v4/transfer', dict(action='download', id=key, offset=0), b)
            raise AssertionError('Downloaded before consent')
        except urllib.error.HTTPError as error:
            assert error.code == 400
        post('/v4/transfer', dict(action='accept', id=key), b)
        for offset in range(0, len(content), 256*1024):
            link(a); link(b)
            result = post('/v4/transfer', dict(action='upload', id=key, offset=offset,
                          chunk=base64.b64encode(content[offset:offset+256*1024]).decode()), a)
        assert result['transfer']['state'] == 'ready'
        received = b''
        while len(received) < len(content):
            received += base64.b64decode(post('/v4/transfer', dict(action='download', id=key, offset=len(received)), b)['chunk'])
        assert received == content
        post('/v4/transfer', dict(action='complete', id=key), b)
        assert post('/v4/transfer', dict(action='poll'), a)['transfer'] is None
        for token in (a, b):
            accepted = link(token)['launch']
            assert accepted['id'] == pair_id and accepted['status'] == 'accepted'
        sample = dict(attempt=pair_id, assets=[dict(path='chara/human/test.pap', sha256='a'*64)],
                      verified=False, phase=1., duration=10., age=0.)
        assert post('/v6/sync', sample, a)['peer'] is None
        assert post('/v6/sync', sample, b)['peer']['assets'] == sample['assets']
        sample['verified'] = True
        post('/v6/sync', sample, a)
        assert post('/v6/sync', sample, b)['peer']['verified'] is True
        link(a, 'finish_launch', invitation=pair_id)
        link(b, 'finish_launch', invitation=pair_id)
        replay = link(a, 'restart')['launch']
        assert replay['previous'] == pair_id and replay['status'] == 'accepted'
        assert link(b)['launch']['id'] == replay['id']
        link(a, 'cancel_launch', invitation=replay['id'])
        print('HTTPS consent, multi-chunk integrity/deletion, shared offer, resource/phase exchange and agreed restart: passed')
    finally:
        for token in (a, b):
            if token:
                try:
                    post('/v2/link', dict(action='close', target='', invitation=''), token)
                except Exception:
                    pass


if __name__ == '__main__':
    main()
