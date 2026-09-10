"""Check handoff integrity and shell refusal paths without requiring Apple/GCP."""
from pathlib import Path
import json, os, re, subprocess, tempfile, shutil
root=Path(__file__).resolve().parents[1]
ignored_dirs={'.terraform','node_modules','obj','bin','dist','Library','Temp','Logs','Obj','UserSettings','builds','logs'}
for p in root.rglob('*.json'):
 if not any(x in p.relative_to(root).parts for x in ignored_dirs): json.loads(p.read_text())
for p in root.rglob('*.md'):
 if any(x in p.relative_to(root).parts for x in ignored_dirs): continue
 for target in re.findall(r'\]\(([^)]+)\)',p.read_text()):
  if '://' not in target and not target.startswith('#'):
   assert (p.parent / target.split('#')[0]).exists(), (p,target)
# A missing project must fail, rather than report a successful install/export.
with tempfile.TemporaryDirectory(prefix='town checks with spaces ') as d:
 env={**os.environ,'UNITY_PROJECT':str(Path(d)/'missing game')}
 result=subprocess.run([str(root/'scripts/install-unity-overlay.sh')],env=env,capture_output=True,text=True)
 assert result.returncode != 0 and 'Create a Universal 3D' in result.stderr
 # Install is idempotent, protects a changed file, and handles paths with spaces.
 project=Path(d)/'existing game'
 (project/'ProjectSettings').mkdir(parents=True)
 (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.0f1\n')
 env['UNITY_PROJECT']=str(project)
 for _ in range(2):
  result=subprocess.run([str(root/'scripts/install-unity-overlay.sh')],env=env,capture_output=True,text=True)
  assert result.returncode == 0, result.stderr
 target=project/'Assets/Editor/TownBuild.cs'
 target.write_text('// local change\n')
 result=subprocess.run([str(root/'scripts/install-unity-overlay.sh')],env=env,capture_output=True,text=True)
 assert result.returncode != 0 and target.read_text() == '// local change\n'
print('Bundle links/JSON and overlay guard checks passed.')
# Mock platform executables to verify orchestration, not Unity/Xcode compilation.
with tempfile.TemporaryDirectory(prefix='town build flow ') as d:
 temp=Path(d)
 shutil.copytree(root/'scripts',temp/'scripts')
 project=temp/'project with spaces'
 (project/'ProjectSettings').mkdir(parents=True)
 (project/'Assets/Editor').mkdir(parents=True)
 (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.0f1\n')
 (project/'Assets/Editor/TownBuild.cs').write_text('// placeholder\n')
 (project/'Assets/Town/Plugins/iOS').mkdir(parents=True)
 (project/'Assets/Town/Plugins/iOS/TownApple.mm').write_text('// placeholder\n')
 mock=temp/'mock tools';mock.mkdir()
 def executable(name,body):
  p=mock/name;p.write_text(body);p.chmod(0o755);return p
 executable('uname','#!/bin/sh\necho Darwin\n')
 unity=executable('Unity Fake',r'''#!/usr/bin/env python3
import os,sys,json
from pathlib import Path
if os.environ.get('FAIL_UNITY') == '1': sys.exit(2)
a=sys.argv[1:]
assert a[a.index('-projectPath')+1] == os.environ['UNITY_PROJECT']
assert os.environ['TOWN_EXPECTED_UNITY_VERSION'] == '6000.0.0f1'
out=Path(os.environ['TOWN_BUILD_OUTPUT'])
if a[a.index('-executeMethod')+1].endswith('.IOS'):
 assert a[a.index('-buildTarget')+1] == 'iOS'
 (out/'Xcode/Unity-iPhone.xcodeproj').mkdir(parents=True)
 (out/'Xcode/town-build.json').write_text(json.dumps({'bundleId':os.environ['BUNDLE_ID']}))
else:
 (out/'Town.app').mkdir()
''')
 executable('xcodebuild',r'''#!/usr/bin/env python3
import os,sys
from pathlib import Path
if os.environ.get('FAIL_XCODE') == '1': sys.exit(3)
a=sys.argv[1:]
assert a[a.index('-destination')+1] == 'id=test-device'
p=Path(a[a.index('-derivedDataPath')+1])/'Build/Products/Release-iphoneos/Town.app'
p.mkdir(parents=True)
''')
 executable('xcrun',r'''#!/usr/bin/env python3
import os,sys,json
if len(sys.argv)>1 and sys.argv[1]=='clang++':
 from pathlib import Path
 Path(sys.argv[sys.argv.index('-o')+1]).write_bytes(b'fake dylib');sys.exit(0)
with open(os.environ['XCRUN_LOG'],'a') as f: f.write(json.dumps(sys.argv[1:])+'\n')
''')
 env={**os.environ,'PATH':str(mock)+os.pathsep+os.environ['PATH'],
      'UNITY_EDITOR':str(unity),'UNITY_PROJECT':str(project),
      'BUNDLE_ID':'com.test.town','APPLE_TEAM_ID':'TESTTEAM',
      'BUILD_CONFIGURATION':'release','XCRUN_LOG':str(temp/'device.log')}
 def run(script,*args,changes=None):
  return subprocess.run([str(temp/'scripts'/script),*args],env={**env,**(changes or {})},capture_output=True,text=True)
 assert run('unity.sh','macos').returncode == 0
 assert run('unity.sh','ios').returncode == 0
 pointer=(temp/'builds/latest-ios.txt').read_text()
 assert run('unity.sh','ios',changes={'FAIL_UNITY':'1'}).returncode != 0
 assert (temp/'builds/latest-ios.txt').read_text() == pointer
 assert run('ios-device.sh','build-and-install','test-device',changes={'FAIL_XCODE':'1'}).returncode != 0
 assert not (temp/'device.log').exists(), 'Failed Xcode build must never install an old app'
 result=run('ios-device.sh','build-and-install','test-device')
 assert result.returncode == 0, result.stderr
 calls=[json.loads(l) for l in (temp/'device.log').read_text().splitlines()]
 assert len(calls) == 2 and calls[0][:4] == ['devicectl','device','install','app']
 assert calls[1][-1] == 'com.test.town'
 result=run('ios-device.sh','build-and-install','test-device',changes={'BUNDLE_ID':'com.test.changed'})
 assert result.returncode != 0, 'Changed bundle ID must require a fresh export'
print('Mocked build/sign/install flow passed (not a real Apple build).')
