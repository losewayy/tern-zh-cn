#!/usr/bin/env python3
"""Record the control buttons of every settings page, for tools/port_settings.py.

The CSS translation draws new labels over native buttons, so a port has to know
which English caption each row's buttons carry in the build it targets. Run it
against a headless Tern (`tern serve --control PORT`) of that build and pass the
JSON to `port_settings.py --buttons`.
"""
import argparse, json, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_tern_settings as settings


def capture(exe, port):
    buttons = {}
    for page in settings.PAGES:
        settings.call(exe, port, 'prefs', page)
        selector = '.pf-doc button'
        nodes = settings.call(exe, port, 'tree', selector).get('nodes', [])
        elements = [e for e in settings.call(exe, port, 'dump', selector).get('elements', []) if e.get('visible')]
        if len(nodes) != len(elements):
            raise RuntimeError('Inconsistent button tree on ' + page)
        rows = {}
        for node, element in zip(nodes, elements):
            path = element['nth']
            start = path.find('section:nth-of-type')
            if start >= 0:
                rows[path[start:]] = node.get('text', '')
        buttons[page] = rows
    return buttons


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--exe', required=True, help='Path to tern.com')
    parser.add_argument('--port', type=int, required=True, help='Control endpoint of a running `tern serve`')
    parser.add_argument('--out', type=Path, required=True, help='Where to write the captured buttons')
    args = parser.parse_args()
    buttons = capture(str(Path(args.exe)), args.port)
    args.out.write_text(json.dumps(buttons, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('Recorded %d buttons across %d pages.' % (sum(map(len, buttons.values())), len(buttons)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
