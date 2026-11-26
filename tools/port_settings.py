#!/usr/bin/env python3
"""Port the reviewed settings translations to a new Tern build.

Tern renders settings by DOM position, so inserting one row moves every rule
below it onto the wrong text. This tool aligns two recorded settings baselines
(tools/check_tern_settings.py --record-from-port) by the English text of each
row, rewrites the row positions in the previous build's per-page CSS and
control selectors, and writes the result to compat/<build>/.

Text that is new or reworded in the new build comes from a TSV of
"English <TAB> Chinese"; anything still missing is listed and exits 1. Control
rows the build gained or lost go in a second TSV using settings-controls.tsv
columns, checked against a captured button dump.
"""
import argparse, difflib, json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KEY = 120  # translation keys; baselines cut text at 200, so keys stay well inside
UNIT = re.compile(r'^(section:nth-of-type\((\d+)\))((?:>[a-z0-9]+:nth-of-type\(\d+\)){0,2})')
STEP = re.compile(r'>([a-z0-9]+):nth-of-type\((\d+)\)')
ROW_UNIT = re.compile(r'^>div:nth-of-type\(1\)>div:nth-of-type\((\d+)\)$')
RULE = re.compile(r'^(div)?\.pf-doc > (.+?)((?:\.[\w-]+)*)(::before)?\{')
VERSION = re.compile(r'^Tern \d+\.\d+\.\d+ \([0-9a-f]+\)$')
SIZES = {'.pf-title': 18, '.pf-sec-t': 13, '.t': 13, '.h': 12}
TAILS = {'>div:nth-of-type(1)>div:nth-of-type(1)': '.t',
         '>div:nth-of-type(1)>div:nth-of-type(2)': '.h',
         '>h2:nth-of-type(1)': '.pf-sec-t'}
HEADER = '/* Tern %s settings; loaded only for this page. */'


def fields(line):
    return line.split('\t')


def split(path):
    """Settings path -> (section, row unit, tail); the row unit is '' outside a setting row."""
    m = UNIT.match(path)
    if not m:
        return None
    unit, tail = m.group(3), path[m.end():]
    if not ROW_UNIT.match(unit):
        unit, tail = '', unit + tail
    return int(m.group(2)), unit, tail


def row_index(unit):
    m = ROW_UNIT.match(unit)
    return int(m.group(1)) if m else None


def units(texts):
    """{section: {row unit: {tail: text}}}; a section heading is the empty row unit."""
    result = {}
    for path, text in texts.items():
        parts = split(path)
        if parts is None:
            continue
        section, unit, tail = parts
        result.setdefault(section, {}).setdefault(unit, {})[tail] = text
    return result


def align(old, new):
    """Pair keys whose first text matches, in order; a renamed row falls out."""
    a, b = list(old), list(new)
    first = lambda table, k: next(iter(table[k].values()), '')
    matcher = difflib.SequenceMatcher(None, [first(old, k) for k in a], [first(new, k) for k in b], autojunk=False)
    pairs = {}
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == 'equal' or (tag == 'replace' and i2 - i1 == j2 - j1):
            for k, nk in zip(a[i1:i2], b[j1:j2]):
                if first(old, k) == first(new, nk):
                    pairs[k] = nk
    return pairs


class Page:
    """What moved between two builds, section by section."""

    def __init__(self, old_texts, new_texts):
        self.old, self.new = units(old_texts), units(new_texts)
        self.old_texts, self.new_texts = old_texts, new_texts
        heading = lambda table: {s: next(iter(table[s].get('', {'': ''}).values()), '') for s in table}
        self.sections = {s: n for s, n in align({s: {'': heading(self.old)[s]} for s in self.old},
                                                {s: {'': heading(self.new)[s]} for s in self.new}).items()}
        self.new_sections = [s for s in self.new if s not in self.sections.values()]
        self.kept, self.added, self.changed = {}, [], []
        for s, ns in self.sections.items():
            old_rows = {k: v for k, v in self.old[s].items() if k}
            new_rows = {k: v for k, v in self.new[ns].items() if k}
            pairs = align(old_rows, new_rows)
            for k, nk in pairs.items():
                self.kept[(s, k)] = (ns, nk)
                for tail, text in new_rows[nk].items():
                    if old_rows[k].get(tail) != text:
                        self.changed.append((s, k, tail))
            for k in new_rows:
                if k not in pairs.values():
                    self.added.append((ns, k))
            for tail, text in new_rows.get('', {}).items():
                if self.old[s].get('', {}).get(tail) != text:
                    self.changed.append((s, '', tail))
        self.changed += [(s, k, tail) for s in self.new_sections for k in self.new[s] for tail in self.new[s][k]]
        self.added += [(s, k) for s in self.new_sections for k in self.new[s] if k]
        self.offsets = {}
        for (s, k), (ns, nk) in self.kept.items():
            self.offsets.setdefault(s, set()).add(row_index(nk) - row_index(k))
        self.inferred = self._infer(old_texts, new_texts)

    def _infer(self, old_texts, new_texts):
        """Rows the alignment could not pair: follow the nearest paired neighbour's shift."""
        result = {}
        for s, ns in self.sections.items():
            paired = sorted((row_index(k), row_index(nk)) for (os_, k), (nns, nk) in self.kept.items() if os_ == s)
            rows = sorted(row_index(k) for k in self.old[s] if k)
            limit = max((row_index(k) for k in self.new[ns] if k), default=0)
            for row in rows:
                key = '>div:nth-of-type(1)>div:nth-of-type(%d)' % row
                if (s, key) in self.kept:
                    continue
                after = [p for p in paired if p[0] > row]
                before = [p for p in paired if p[0] < row]
                delta = (after[0][1] - after[0][0]) if after else (before[-1][1] - before[-1][0]) if before else 0
                target = row + delta
                moved = '>div:nth-of-type(1)>div:nth-of-type(%d)' % target
                taken = target < 1 or target > limit

                def first(texts, unit):
                    for tail in ('>div:nth-of-type(1)>div:nth-of-type(1)',):
                        path = 'section:nth-of-type(%d)%s%s' % (ns if texts is new_texts else s, unit, tail)
                        if path in texts:
                            return texts[path]
                    return None
                if not taken and first(old_texts, key) != first(new_texts, moved):
                    taken = True  # another row took the slot; its own translation covers it
                result[(s, row)] = None if taken else target
        return result

    def remap(self, selector, unverified=None):
        """Move a selector's section and row, or None when the target row is gone."""
        parts = split(selector)
        if parts is None:
            return selector
        section, unit, tail = parts
        if (section, unit) in self.kept:
            ns, nunit = self.kept[(section, unit)]
            return 'section:nth-of-type(%d)%s%s' % (ns, nunit, tail)
        if unit == '':
            if section not in self.sections:
                return None
            return 'section:nth-of-type(%d)%s' % (self.sections[section], tail)
        if unverified is not None:
            unverified.append(selector)
        if section not in self.sections:
            return None
        row = row_index(unit)
        if (section, row) not in self.inferred:
            # A row the baseline never labelled; it stays put only if nothing above it moved.
            if self.offsets.get(section, set()) <= {0}:
                return 'section:nth-of-type(%d)%s%s' % (self.sections[section], unit, tail)
            return None
        target = self.inferred[(section, row)]
        if target is None:
            return None
        return 'section:nth-of-type(%d)>div:nth-of-type(1)>div:nth-of-type(%d)%s' % (self.sections[section], target, tail)


def strict_remap(page, selector):
    """Control selectors must land on a labelled row: no inference, no guessing."""
    parts = split(selector)
    if parts is None:
        return selector
    section, unit, tail = parts
    if (section, unit) in page.kept:
        ns, nunit = page.kept[(section, unit)]
        return 'section:nth-of-type(%d)%s%s' % (ns, nunit, tail)
    if unit == '' and section in page.sections:
        return 'section:nth-of-type(%d)%s' % (page.sections[section], tail)
    return None


def quote(text):
    return text.replace('\\', '\\\\').replace('"', '\\"')


def is_identifier(text):
    """Action ids such as send_to_cloud or layout:dock stay in English."""
    return re.fullmatch(r'[\w.:-]+', text) is not None and not any(c.isupper() for c in text)


def first_words(text, count=90):
    words = text.split()
    return ' '.join(words[:count]) + ('…' if len(words) > count else '')


def load_table(path):
    table = {}
    if path and path.exists():
        for line in path.read_text(encoding='utf-8').splitlines():
            if not line.strip() or line.startswith('#'):
                continue
            cells = fields(line)
            table[cells[0][:KEY]] = cells[1] if len(cells) > 1 else ''
    return table


def page_css(page, version, source, translations, verbatim, missing, unverified):
    """Copy the previous build's rules onto this build's rows and translate the new ones."""
    lines = [HEADER % version]
    inverse = {v: k for k, v in page.kept.items()}
    rewritten = {}
    for section, unit, tail in page.changed:
        old = inverse.get((section, unit))
        if old:
            rewritten[old + (tail,)] = (section, unit, tail)
    for line in source.read_text(encoding='utf-8').splitlines():
        if not line.strip() or line.startswith('/* Tern'):
            continue
        m = RULE.match(line)
        if not m:
            lines.append(line)
            continue
        selector, cls = m.group(2), m.group(3)
        parts = split(selector)
        if parts is not None and parts in [k[:3] for k in rewritten]:
            continue
        moved = selector if verbatim else page.remap(selector, unverified)
        if moved is None:
            continue
        lines.append(line.replace('> ' + selector + cls, '> ' + moved + cls, 1))
    text = '\n'.join(lines) + '\n'
    if verbatim:
        targets = []
        for (os_, ok, tail), (section, unit, ntail) in rewritten.items():
            if (os_, ok) != (section, unit):
                missing.append('%-10s %s moved, but this page is copied as it is' % (source.stem[6:], os_))
                continue
            targets.append(('section:nth-of-type(%d)%s%s' % (section, unit, ntail), ntail))
    else:
        targets = [('section:nth-of-type(%d)%s%s' % (s, k, t), t) for s, k, t in page.changed]
        for s, k in page.added:
            prefix = 'section:nth-of-type(%d)%s' % (s, k)
            targets += [(path, path[len(prefix):]) for path in page.new_texts
                        if path.startswith(prefix) and path[len(prefix):len(prefix) + 1] in ('>', '')]
        targets += [('section:nth-of-type(%d)>h2:nth-of-type(1)' % s, '>h2:nth-of-type(1)') for s in page.new_sections]
    seen = set()
    for path, tail in targets:
        if path in seen:
            continue
        seen.add(path)
        english = page.new_texts.get(path)
        cls = TAILS.get(tail)
        if english is None or is_identifier(english):
            continue
        if cls is None:
            missing.append('%-10s %s | unknown element %s' % (source.stem[6:], path, tail))
            continue
        chinese = translations.get(english[:KEY])
        if not chinese:
            missing.append(first_words(english))
            continue
        text += ('.pf-doc > %s%s{font-size:0!important;}\n'
                 '.pf-doc > %s%s::before{content:"%s";font-size:%dpx;display:inline-block;line-height:1.45;white-space:normal;}\n'
                 % (path, cls, path, cls, quote(chinese), SIZES[cls]))
    return text


def controls_tsv(args, pages, verbatim):
    """Move the reviewed control rows onto the new build and add its extras."""
    rows, broken, dropped = [], [], []
    for line in (ROOT / 'settings-controls.tsv').read_text(encoding='utf-8').splitlines():
        if not line.strip():
            continue
        page, row, occurrence, item, selector, icon, label, mode = fields(line)
        if page in verbatim:
            rows.append(line)
            continue
        moved = strict_remap(pages[page], selector)
        if moved is None:
            if not any(text == row for text in args.new_pages[page].values()):
                dropped.append('%s | %s | row no longer exists' % (page, row))
            else:
                broken.append('%s | %s | selector no longer resolves' % (page, row))
            continue
        if args.buttons and mode == 'fixed' and selector.startswith('section:nth-of-type('):
            actual = args.buttons.get(page, {}).get(moved)
            if actual is None:
                broken.append('%s | %s | moved to nowhere' % (page, row))
                continue
            if actual != label:
                broken.append('%s | %s | expected "%s", found "%s"' % (page, row, label, actual))
                continue
        rows.append('\t'.join((page, 'Tern <version>' if VERSION.match(row) else row,
                               occurrence, item, moved, icon, label, mode)))
    if args.extras and args.extras.exists():
        for line in args.extras.read_text(encoding='utf-8').splitlines():
            if not line.strip() or line.startswith('#'):
                continue
            page, row, occurrence, item, selector, icon, label, mode = fields(line)
            actual = args.buttons.get(page, {}).get(selector) if args.buttons else None
            if label and actual is not None and actual != label:
                broken.append('%s | %s | extra expected "%s", found "%s"' % (page, row, label, actual))
                continue
            rows.append(line)
    if args.buttons and args.report_controls:
        covered = set()
        for line in rows:
            page, row, occurrence, item, selector, icon, label, mode = fields(line)
            parts = split(selector)
            if parts and row_index(parts[1]) is not None:
                covered.add((page, parts[0], parts[1]))
        for page, buttons in args.buttons.items():
            titles = {}
            for path, text in args.new_pages.get(page, {}).items():
                parts = split(path)
                if parts and parts[2] == '>div:nth-of-type(1)>div:nth-of-type(1)':
                    titles[(parts[0], parts[1])] = text
            repeats = {}
            for caption in buttons.values():
                repeats[caption] = repeats.get(caption, 0) + 1
            for path, caption in buttons.items():
                parts = split(path)
                if not parts or (page, parts[0], parts[1]) in covered:
                    continue
                title = titles.get((parts[0], parts[1]))
                if not title or not caption or is_identifier(caption) or caption.isdigit() or repeats[caption] > 3:
                    continue
                dropped.append('%s | %s | button "%s" has no row' % (page, title, caption))
    for item in sorted(set(dropped)):
        print('DROPPED', item)
    for item in broken:
        print('STALE', item)
    return None if broken else rows


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--old', default='0.9.23', help='build whose compat CSS is the source (default 0.9.23)')
    ap.add_argument('--new', required=True, help='build of the new baseline, tools/tern-<build>-settings.json')
    ap.add_argument('--translations', type=Path, help='English<TAB>Chinese for new and reworded text')
    ap.add_argument('--buttons', type=Path, help='control buttons captured from the new build')
    ap.add_argument('--extras', type=Path, help='control rows to add, in settings-controls.tsv columns')
    ap.add_argument('--verbatim', default='', help='pages the window shows more rows of than the baseline; copied as they are')
    ap.add_argument('--report', action='store_true', help='list the layout change only')
    ap.add_argument('--report-controls', action='store_true', help='name rows whose buttons no control entry translates')
    ap.add_argument('--dry-run', action='store_true', help='check everything, write nothing')
    args = ap.parse_args()
    tools = ROOT / 'tools'
    old_pages = json.loads((tools / ('tern-%s-settings.json' % args.old)).read_text(encoding='utf-8'))['pages']
    args.new_pages = json.loads((tools / ('tern-%s-settings.json' % args.new)).read_text(encoding='utf-8'))['pages']
    translations = load_table(args.translations)
    args.buttons = json.loads(args.buttons.read_text(encoding='utf-8')) if args.buttons and args.buttons.exists() else {}
    verbatim = {page for page in args.verbatim.split(',') if page}
    pages, missing, css = {}, [], {}
    for name, texts in args.new_pages.items():
        page = Page(old_pages[name], texts)
        pages[name] = page
        keep = name in verbatim
        if keep:
            print('%-10s copied as it is: the window shows rows this baseline lacks' % name)
        else:
            moved = sum(1 for k, v in page.kept.items() if k != v)
            print('%-10s sections %s | rows moved %d new %d | elements changed %d'
                  % (name, page.new_sections, moved, len(page.added), len(page.changed)))
        if args.report:
            continue
        source = ROOT / 'compat' / args.old / ('prefs-%s.css' % name)
        if not source.exists():
            source = ROOT / ('prefs-%s.css' % name)
        unverified = []
        css[name] = page_css(page, args.new, source, translations, keep, missing, unverified)
        if unverified:
            print('%-10s %d rules the baseline cannot verify (kept by row shift)' % (name, len(unverified)))
    if missing:
        for item in missing:
            print('MISSING', item)
        return 1
    if args.report:
        return 0
    rows = controls_tsv(args, pages, verbatim)
    if rows is None:
        return 1
    if args.dry_run:
        print('dry run: %d pages, %d control rows' % (len(css), len(rows)))
        return 0
    out = ROOT / 'compat' / args.new
    out.mkdir(parents=True, exist_ok=True)
    for page, text in css.items():
        (out / ('prefs-%s.css' % page)).write_text(text, encoding='utf-8', newline='\r\n')
    (out / 'settings-controls.tsv').write_text('\r\n'.join(rows) + '\r\n', encoding='utf-8', newline='')
    print('wrote %s: %d pages, %d control rows' % (out.relative_to(ROOT), len(css), len(rows)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
