"""Package distributable files only, preserving Linux executable modes."""
import argparse
from pathlib import Path
import tarfile
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--rid', choices=['win-x64', 'linux-x64', 'linux-arm64', 'linux-arm'])
parser.add_argument('--output', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
out = args.output or root / 'artifacts'
out.mkdir(parents=True, exist_ok=True)
for rid in ([args.rid] if args.rid else ['win-x64', 'linux-x64', 'linux-arm64', 'linux-arm']):
    executable = 'LanShare.exe' if rid == 'win-x64' else 'LanShare'
    items = [(root / 'artifacts' / rid / executable, executable), (root / 'README.md', 'README.md'), (root / 'LICENSE', 'LICENSE')]
    scripts = ['Start-Windows.cmd', 'Allow-Lan-Windows.ps1'] if rid == 'win-x64' else ['start-linux.sh']
    items += [(root / 'scripts' / name, name) for name in scripts]
    items += [(p, p.relative_to(root).as_posix()) for p in sorted((root / 'docs').rglob('*')) if p.is_file()]
    for path, _ in items:
        if not path.is_file(): raise FileNotFoundError(path)
    if rid == 'win-x64':
        target = out / f'LanShare-{rid}.zip'
        with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for path, name in items: archive.write(path, name)
    else:
        target = out / f'LanShare-{rid}.tar.gz'
        with tarfile.open(target, 'w:gz', compresslevel=6) as archive:
            for path, name in items:
                info = archive.gettarinfo(str(path), arcname=name)
                info.mode = 0o755 if name in ['LanShare', 'start-linux.sh'] else 0o644
                info.uid = info.gid = 0
                info.uname = info.gname = ''
                with path.open('rb') as source: archive.addfile(info, source)
    print(target)
