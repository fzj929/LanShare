"""End-to-end HTTP checks. Development only; Python is not required by LanShare."""
import concurrent.futures
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1]).resolve()
dotnet = sys.argv[2] if len(sys.argv) > 2 else None
with tempfile.TemporaryDirectory(prefix='lanshare-test-') as temp:
    work = Path(temp)
    appdir = work / 'app'
    appdir.mkdir()
    if source.suffix == '.dll':
        for item in source.parent.iterdir():
            if item.is_file(): shutil.copy2(item, appdir / item.name)
        command = [dotnet or 'dotnet', str(appdir / source.name), '--no-browser']
    else:
        shutil.copy2(source, appdir / source.name)
        command = [str(appdir / source.name), '--no-browser']
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        port = s.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    data = work / 'data'
    (appdir / 'lanshare.json').write_text(json.dumps(dict(port=port, dataDirectory=str(data), maxFileSizeMB=1, openBrowser=False, accessPassword='', adminPassword='integration-admin')), encoding='utf-8')
    env = os.environ.copy()
    env.update(LANSHARE_PORT=str(port), LANSHARE_DATA_DIR=str(data), LANSHARE_ADMIN_PASSWORD='integration-admin', LANSHARE_ACCESS_PASSWORD='')
    env['DOTNET_ROOT'] = str(work / 'no-dotnet-installed')
    env['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    log = open(work / 'server.log', 'wb')
    process = None
    client = None

    def fresh_client():
        return urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(path, method='GET', body=None, headers=None, expected=200, use_client=None):
        hdr = {'X-LanShare-Request': '1'}
        hdr.update(headers or {})
        req = urllib.request.Request(base + path, data=body, method=method, headers=hdr)
        try: response = (use_client or client).open(req, timeout=15)
        except urllib.error.HTTPError as ex: response = ex
        content = response.read()
        assert response.status == expected, (path, response.status, content[:300])
        return content, response.headers

    def start():
        global process, client
        client = fresh_client()
        process = subprocess.Popen(command, env=env, stdout=log, stderr=log)
        for _ in range(100):
            if process.poll() is not None: raise RuntimeError('Server exited before readiness')
            try:
                request('/api/status')
                return
            except (OSError, urllib.error.URLError): time.sleep(.1)
        raise RuntimeError('Server did not become ready')

    def stop():
        global process
        if process is not None:
            process.terminate()
            process.wait(timeout=15)
            process = None

    try:
        start()
        page, headers = request('/')
        assert '局域网文件站'.encode() in page
        assert 'frame-ancestors' in headers['Content-Security-Policy']
        request('/app.js'); request('/app.css')
        request('/api/status', headers={'Host': 'attacker.invalid'}, expected=400)
        request('/api/files?name=bad', 'PUT', b'a', {'X-LanShare-Request': ''}, expected=403)
        request('/api/files?name=bad', 'PUT', b'a', {'Origin': 'http://attacker.invalid'}, expected=403)
        payload = os.urandom(256 * 1024)
        name = '测试 <script> & 同名.pptx'
        uri = '/api/files?name=' + urllib.parse.quote(name)
        first = json.loads(request(uri, 'PUT', payload, expected=201)[0])
        second = json.loads(request(uri, 'PUT', b'second file', expected=201)[0])
        assert first['id'] != second['id'] and first['name'] == name
        downloaded, hdr = request(f"/api/files/{first['id']}/download")
        assert hashlib.sha256(downloaded).digest() == hashlib.sha256(payload).digest()
        assert 'attachment' in hdr['Content-Disposition']
        partial, _ = request(f"/api/files/{first['id']}/download", headers={'Range': 'bytes=10-29'}, expected=206)
        assert partial == payload[10:30]
        empty = json.loads(request('/api/files?name=empty.txt', 'PUT', b'', expected=201)[0])
        assert empty['size'] == 0
        clean = json.loads(request('/api/files?name=..%2F..%2Fsafe.txt', 'PUT', b'path test', expected=201)[0])
        assert clean['name'] == 'safe.txt'
        request('/api/files?name=too-large', 'PUT', b'x' * (1024 * 1024 + 1), expected=413)
        request(f"/api/files/{first['id']}", 'DELETE', expected=403)
        request('/api/files/not-a-guid/download', expected=404)
        def concurrent_upload(i):
            return request('/api/files?name=concurrent'+str(i), 'PUT', payload, expected=201, use_client=fresh_client())
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            list(pool.map(concurrent_upload, range(3)))
        with socket.create_connection(('127.0.0.1', port), timeout=3) as s:
            s.sendall(f'PUT /api/files?name=interrupted HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nX-LanShare-Request: 1\r\nContent-Length: 100000\r\n\r\n'.encode()+b'partial')
        time.sleep(.5)
        assert not list((data / 'staging').iterdir()), 'Interrupted uploads must be cleaned up'
        count = len(json.loads(request('/api/files')[0]))
        stop()
        env['LANSHARE_ACCESS_PASSWORD'] = 'integration-guest'
        start()
        state = json.loads(request('/api/status')[0]); assert state['requiresPassword'] and not state['authenticated']
        request('/api/files', expected=401)
        request(f"/api/files/{first['id']}/download", expected=401)
        request('/api/login', 'POST', b'{"password":"wrong"}', {'Content-Type':'application/json'}, expected=401)
        request('/api/login', 'POST', b'{"password":"integration-guest"}', {'Content-Type':'application/json'})
        assert len(json.loads(request('/api/files')[0])) == count, 'Files must survive restart'
        request(f"/api/files/{first['id']}", 'DELETE', expected=403)
        request('/api/logout', 'POST')
        request('/api/files', expected=401)
        request('/api/login', 'POST', b'{"password":"integration-admin"}', {'Content-Type':'application/json'})
        assert json.loads(request('/api/status')[0])['admin']
        request(f"/api/files/{second['id']}", 'DELETE', expected=204)
        request(f"/api/files/{second['id']}/download", expected=404)
        request('/api/logout', 'POST')
        for _ in range(10): request('/api/login', 'POST', b'{"password":"wrong"}', {'Content-Type':'application/json'}, expected=401)
        request('/api/login', 'POST', b'{"password":"wrong"}', {'Content-Type':'application/json'}, expected=429)
        print('PASS: embedded UI, upload/download hashes, Unicode/same names, empty files, Range, paths, limits, concurrent and interrupted uploads, restart persistence, password roles, delete, logout, login throttling.')
    finally:
        stop()
        log.close()
