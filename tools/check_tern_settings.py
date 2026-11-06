#!/usr/bin/env python3
"""Compare Tern's headless settings labels with a known, reviewed baseline."""
import argparse, json, os, re, socket, subprocess, sys, time
from pathlib import Path

PAGES = ('general','agents','appearance','terminal','lenses','databases','notebooks','carly','keyboard','git','plugins','sessions','hosts','advanced')
SELECTOR = '.pf-doc .pf-lbl > .t, .pf-doc .pf-lbl > .h, .pf-doc .pf-sec-t, .pf-doc .pf-title, .pf-doc .pf-lead'
BASELINE = Path(__file__).with_name('tern-0.9.12-settings.json')
FLAGS = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0

def call(exe, port, *args):
    p = subprocess.run([exe, 'ctl', '--control', str(port), *args], capture_output=True, text=True, encoding='utf-8', timeout=15, creationflags=FLAGS)
    if p.returncode: raise RuntimeError('Tern control request failed')
    data = json.loads(p.stdout)
    if not data.get('ok'): raise RuntimeError('Tern control request was rejected')
    return data

def capture(exe, port):
    pages = {}
    for page in PAGES:
        call(exe, port, 'prefs', page)
        nodes = call(exe, port, 'tree', SELECTOR).get('nodes', [])
        elements = [e for e in call(exe, port, 'dump', SELECTOR).get('elements', []) if e.get('visible')]
        if not nodes or len(nodes) != len(elements): raise RuntimeError('Inconsistent UI tree on '+page)
        values = {}
        for node, element in zip(nodes, elements):
            path = element['nth']
            start = path.find('section:nth-of-type')
            if start < 0: start = path.find('header:nth-of-type')
            if start < 0: raise RuntimeError('Unexpected settings path on '+page)
            key = path[start:]
            if key in values: raise RuntimeError('Duplicate UI path on '+page)
            text = node.get('text', '')
            text = re.sub(r'Tern \d+\.\d+\.\d+ \([0-9a-f]+\)', 'Tern <version>', text)
            values[key] = text
        pages[page] = values
    return pages

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--exe', required=True, help='Path to tern.com')
    parser.add_argument('--record-from-port', type=int, help='Record from an already running headless 0.9.12 harness')
    args=parser.parse_args(); exe=str(Path(args.exe))
    if args.record_from_port:
        data=capture(exe,args.record_from_port)
        BASELINE.write_text(json.dumps({'version':'0.9.12','pages':data},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print('Recorded',sum(map(len,data.values())),'settings elements across',len(data),'pages.')
        return 0
    expected=json.loads(BASELINE.read_text(encoding='utf-8'))['pages']
    with socket.socket() as s: s.bind(('127.0.0.1',0)); port=s.getsockname()[1]
    proc=subprocess.Popen([exe,'serve','--control',str(port)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=FLAGS)
    try:
        for _ in range(60):
            try:call(exe,port,'state');break
            except Exception:
                if proc.poll() is not None:raise RuntimeError('Headless Tern exited during startup')
                time.sleep(.25)
        else:raise RuntimeError('Headless Tern did not start')
        actual=capture(exe,port)
        changed=[]
        for page in PAGES:
            a=expected.get(page,{});b=actual.get(page,{})
            if a!=b:changed.append((page,sum(a.get(k)!=b.get(k) for k in a.keys()|b.keys())))
        if changed:
            print('Settings changed:', ', '.join(f'{p} ({n})' for p,n in changed))
            return 1
        print('Settings match the 0.9.12 baseline across all',len(PAGES),'pages.')
        return 0
    finally:
        try:call(exe,port,'quit')
        except Exception:pass
        try:proc.wait(timeout=4)
        except subprocess.TimeoutExpired:proc.terminate()

if __name__=='__main__':
    try:sys.exit(main())
    except Exception as e:
        print('Could not validate Tern settings:',str(e),file=sys.stderr);sys.exit(2)
