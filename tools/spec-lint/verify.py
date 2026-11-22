"""spec-lint v2: 契约一致性机器校验。

v1 的教训（第三轮评审抓到的）：检查函数必须能被自证会失败。
因此本文件把所有检查抽成 `check_*(con, defs) -> list[str]`，并对每一项：
  1) 在真实库上跑 → 必须为空；
  2) 在**被改坏的副本**上跑同一函数 → 必须非空。
只做第 1 步的检查视为没有检查。
"""
import io, re, json, sqlite3, sys, copy, glob

fail = []
def ok(m): print('OK  ', m)
def bad(m): fail.append(m); print('FAIL', m)

SQL = io.open('spec/schema/v1.sql', encoding='utf-8').read()
DEFS = json.load(io.open('spec/schema/export-v1.json', encoding='utf-8'))['$defs']

MAP = {'provider':'provider','model':'model','agent_profile':'agent','prompt_template':'prompt','memory_item':'memory',
       'conversation':'conversation','message':'message','message_part':'message_part','attachment':'attachment',
       'mcp_server':'mcp_server','mcp_tool':'mcp_tool','usage_record':'usage_record','tool_call_record':'tool_call',
       'folder':'folder','tag':'tag','conversation_tag':'conversation_tag','message_attachment':'message_attachment',
       'provider_key':'provider_key','tombstone':'tombstone','app_setting':'setting'}
SKIP = {('provider_key','key_ref'), ('app_setting','value_json')}   # 句柄不入导出；value_json 以同一字段名导出

def cols(con, tbl):
    return [(r[1], r[2], r[3], r[5]) for r in con.execute('PRAGMA table_info(%s)' % tbl)]  # name, type, notnull, pk

def schema_nullable(prop):
    if prop.get('type') == 'null': return True
    t = prop.get('type')
    if isinstance(t, list) and 'null' in t: return True
    for alt in prop.get('oneOf', []) or []:
        if alt.get('type') == 'null': return True
    return False

def schema_base(prop):
    t = prop.get('type')
    if isinstance(t, list):
        t = [x for x in t if x != 'null'][0]
    if t: return t
    if '$ref' in prop:
        return 'string'          # uuid 等 $defs 都是字符串
    enum = prop.get('enum')
    if enum:
        vals = [v for v in enum if v is not None]
        if vals and all(isinstance(v, str) for v in vals): return 'string'
        if vals and all(isinstance(v, int) for v in vals): return 'integer'
    for alt in prop.get('oneOf', []) or []:
        if alt.get('type') == 'integer': return 'integer'
        if alt.get('$ref'): return 'string'
        if alt.get('const') is not None: return 'integer'
    if 'const' in prop: return 'integer'
    return None

# ---------------- 检查函数（纯函数：con + defs → 问题列表） ----------------
def check_missing_props(con, defs):
    out = []
    for tbl, df in MAP.items():
        props = defs[df]['properties']
        for name, _t, _nn, pk in cols(con, tbl):
            if pk or (tbl, name) in SKIP: continue
            if name not in props: out.append('%s.%s 缺在 %s.properties' % (tbl, name, df))
    return out

def check_missing_required(con, defs):
    out = []
    for tbl, df in MAP.items():
        props = defs[df]['properties']; req = set(defs[df].get('required', []))
        for name, _t, nn, pk in cols(con, tbl):
            if pk or (tbl, name) in SKIP or nn != 1: continue
            if name in props and name not in req: out.append('%s.%s 是 NOT NULL 但不在 required' % (tbl, name))
    return out

def check_phantom_required(con, defs):
    out = []
    for tbl, df in MAP.items():
        ks = {c[0] for c in cols(con, tbl)}
        for name in defs[df].get('required', []):
            if name not in ks: out.append('%s.%s 在 required 但 DDL 没有该列' % (df, name))
    return out

TYPE_OK = {'integer': {'integer', 'boolean'}, 'number': {'number', 'integer'}, 'string': {'string'},
           'boolean': {'boolean', 'integer'}, 'object': {'object'}, 'array': {'array'}}
def check_types(con, defs):
    out = []
    for tbl, df in MAP.items():
        props = defs[df]['properties']
        for name, decl, _nn, pk in cols(con, tbl):
            if pk or (tbl, name) in SKIP or name not in props: continue
            base = schema_base(props[name])
            if base is None:
                out.append('%s.%s 无法解析 schema 类型' % (tbl, name)); continue
            decl_up = (decl or '').upper()
            ddl_kind = 'integer' if 'INT' in decl_up else ('number' if ('REAL' in decl_up or 'NUM' in decl_up) else 'string')
            if ddl_kind not in TYPE_OK.get(base, {base}):
                out.append('%s.%s DDL=%s 但 schema=%s' % (tbl, name, decl, base))
    return out

def check_nullability(con, defs):
    out = []
    for tbl, df in MAP.items():
        props = defs[df]['properties']
        for name, _t, nn, pk in cols(con, tbl):
            if pk or (tbl, name) in SKIP or name not in props: continue
            ddl_nullable = (nn == 0)
            sch_nullable = schema_nullable(props[name])
            if ddl_nullable != sch_nullable:
                out.append('%s.%s DDL nullable=%s 但 schema nullable=%s' % (tbl, name, ddl_nullable, sch_nullable))
    return out

def check_ranges(con, defs):
    """DDL 的 CHECK (x BETWEEN a AND b) 必须与 schema 的 minimum/maximum 一致；
    CHECK (x = 0 OR x >= n) 必须与 schema 的 oneOf[{const:0},{minimum:n}] 一致。"""
    out = []
    for m in re.finditer(r'CHECK \(\s*(\w+) BETWEEN (\d+) AND (\d+)\s*\)', SQL):
        col, lo, hi = m.group(1), int(m.group(2)), int(m.group(3))
        for tbl, df in MAP.items():
            props = defs[df]['properties']
            if col in props:
                p = props[col]
                if p.get('minimum') != lo or p.get('maximum') != hi:
                    out.append('%s: DDL %d~%d vs schema min=%s max=%s' % (col, lo, hi, p.get('minimum'), p.get('maximum')))
    for m in re.finditer(r'CHECK \(\s*(\w+) = 0 OR \w+ >= (\d+)\s*\)', SQL):
        col, lo = m.group(1), int(m.group(2))
        for tbl, df in MAP.items():
            props = defs[df]['properties']
            if col in props:
                p = props[col]
                alts = p.get('oneOf', [])
                has_zero = any(a.get('const') == 0 for a in alts)
                has_min = any(a.get('minimum') == lo for a in alts)
                if not (has_zero and has_min):
                    out.append('%s: DDL "=0 OR >=%d" 未在 schema 表达为 oneOf' % (col, lo))
    return out


DEFAULTS_DOC = io.open('spec/schema/defaults.md', encoding='utf-8').read()

def _table_blocks(sql):
    """把 v1.sql 切成 {表名: 该表 DDL 文本}，避免同名列跨表误判。"""
    out, cur, buf = {}, None, []
    for line in sql.split('\n'):
        m = re.match(r'CREATE TABLE (\w+)', line)
        if m:
            if cur: out[cur] = '\n'.join(buf)
            cur, buf = m.group(1), [line]
        elif cur:
            buf.append(line)
    if cur: out[cur] = '\n'.join(buf)
    return out

def check_enums(con, defs):
    """TEXT 列的 CHECK (x IN (...)) 必须与导出 schema 的 enum 集合相等。"""
    out = []
    blocks = _table_blocks(SQL)
    for tbl, df in MAP.items():
        block = blocks.get(tbl)
        if not block: continue
        props = defs[df]['properties']
        for m in re.finditer(r"CHECK \(\s*(\w+)\s+IN\s*\(([^)]*)\)\s*\)", block):
            col, vals = m.group(1), m.group(2)
            parsed = re.findall(r"'([^']*)'", vals)
            if not parsed: continue                      # 数值型 (0,1) 不比对
            if col not in props or 'enum' not in props[col]: continue
            senum = [v for v in props[col]['enum'] if v is not None]
            if set(senum) != set(parsed):
                out.append('%s.%s: DDL IN %s vs schema enum %s' % (tbl, col, sorted(parsed), sorted(senum)))
    return out

def check_defaults(con, defs, doc=None):
    """defaults.md 的每一行必须等于 DDL 的 DEFAULT，且双向覆盖。"""
    doc = DEFAULTS_DOC if doc is None else doc
    declared = {}
    for t, c, v in re.findall(r'^\| (\w+) \| (\w+) \| `([^`]*)` \|$', doc, re.M):
        declared[(t, c)] = v
    actual = {}
    for tbl, block in _table_blocks(SQL).items():
        for line in block.split('\n'):
            for m in re.finditer(r"(\w+)\s+(?:INTEGER|TEXT|REAL)[^,\n]*?DEFAULT\s+('?[\w.\-]+'?)", line):
                actual[(tbl, m.group(1))] = m.group(2).strip("'")
    out = []
    for k in sorted(set(declared) | set(actual)):
        if declared.get(k) != actual.get(k):
            out.append('%s.%s: defaults.md=%s DDL=%s' % (k[0], k[1], declared.get(k), actual.get(k)))
    return out

CHECKS = [('DDL 列都在导出 properties', check_missing_props),
          ('NOT NULL 都在 required', check_missing_required),
          ('required 无幻影列', check_phantom_required),
          ('类型对齐', check_types),
          ('可空性双向对齐', check_nullability),
          ('CHECK 范围对齐', check_ranges),
          ('CHECK 枚举对齐', check_enums),
          ('DEFAULT 与 defaults.md 对齐', check_defaults)]

# ---------------- 1) 真实库：必须全空 ----------------
con = sqlite3.connect(':memory:'); con.executescript(SQL)
for name, fn in CHECKS:
    problems = fn(con, DEFS)
    ok(name) if not problems else bad('%s: %s' % (name, problems[:6]))

# ---------------- 2) 自证：改坏副本，同一函数必须报错 ----------------
def mutated(mutator):
    d = copy.deepcopy(DEFS)
    mutator(d)
    return d

proofs = []

# 2a 缺列：删掉 provider.billing_url
proofs.append(('缺列', check_missing_props, mutated(lambda d: d['provider']['properties'].pop('billing_url'))))
# 2b 缺 required：把 message_part.truncated 从 required 去掉
def m2(d): d['message_part']['required'].remove('truncated')
proofs.append(('缺 required', check_missing_required, mutated(m2)))
# 2c 类型错：first_byte_timeout_ms 写成 string
def m3(d): d['provider']['properties']['first_byte_timeout_ms'] = {'type': 'string'}
proofs.append(('类型错', check_types, mutated(m3)))
# 2d 可空性错：org_id 改成不可空
def m4(d): d['provider']['properties']['org_id'] = {'type': 'string'}
proofs.append(('可空性错', check_nullability, mutated(m4)))
# 2e 范围错：first_byte 的 minimum 改成 1
def m5(d): d['provider']['properties']['first_byte_timeout_ms']['minimum'] = 1
proofs.append(('范围错', check_ranges, mutated(m5)))
# 2f 枚举漂移：给 provider.protocol 的 schema enum 加一个 DDL 没有的值
def m6(d): d['provider']['properties']['protocol']['enum'].append('bedrock')
proofs.append(('枚举漂移', check_enums, mutated(m6)))
# 2g 默认值漂移：改 defaults.md 里的一个数（模拟只改文档不同步 DDL）
proofs.append(('默认值漂移', 'defaults', DEFAULTS_DOC.replace('| provider | concurrency_limit | `4` |', '| provider | concurrency_limit | `8` |')))
# 2h DDL 多一列：ALTER TABLE 加列
con_mut = sqlite3.connect(':memory:'); con_mut.executescript(SQL)
con_mut.execute('ALTER TABLE provider ADD COLUMN selftest_col INTEGER NOT NULL DEFAULT 0')
proofs.append(('DDL 多列', check_missing_props, DEFS))

for label, fn, defs in proofs:
    if label == 'DDL 多列':
        problems = fn(con_mut, defs)
    elif fn == 'defaults':
        problems = check_defaults(con, DEFS, defs)
    else:
        problems = fn(con, defs)
    ok('自证[%s] 能被检出' % label) if problems else bad('自证[%s] 失败：检查对该缺陷不敏感' % label)

# ---------------- 3) 非 schema 类检查 ----------------
CS = io.open('spec/conformance/CASES.md', encoding='utf-8').read()
n_cases = len(re.findall(r'^\| (FRAME|OAI|RESP|ANT|GEM|ORT|ERR|CAN|RENDER|EXPORT)-\d+ \|', CS, re.M))
hdr = re.search(r'用例总数：\*\*(\d+)\*\*', CS)
ok('CASES %d == header' % n_cases) if hdr and int(hdr.group(1)) == n_cases else bad('CASES %d vs %s' % (n_cases, hdr.group(1) if hdr else None))

PS = io.open('docs/PROJECT_STATE.md', encoding='utf-8').read()
vbad = []
for f in ['spec/stream-events.md','spec/errors.md','spec/providers.md','spec/render-model.md','spec/conformance/CASES.md']:
    v = re.search(r'- 版本：\*\*([0-9A-Za-z.\-]+)\*\*', io.open(f, encoding='utf-8').read()).group(1)
    row = re.search(r'^\| `%s` \| ([^|]+) \|' % re.escape(f), PS, re.M)
    if not row or row.group(1).strip() != v:
        vbad.append('%s spec=%s ps=%s' % (f, v, row.group(1).strip() if row else 'missing'))
ok('版本一致') if not vbad else bad('版本不一致: %s' % vbad)

ER = io.open('spec/errors.md', encoding='utf-8').read()
sec4 = ER.split('## 4. 操作')[1].split('## 5.')[0]
actions = {a for a in re.findall(r'^\| `([a-z_]+)` \|', sec4, re.M) if a != 'a'}
codes_block = ER.split('## 3. 错误码表')[1].split('## 4.')[0]
used, no_action = set(), []
for line in codes_block.split('\n'):
    if line.startswith('| `') and line.count('|') >= 7:
        code = re.match(r'^\| `([A-Z_]+)`', line)
        cell = line.split('|')[6]
        used |= set(re.findall(r'`([a-z_]+)`', cell))
        if code and not re.findall(r'`([a-z_]+)`', cell): no_action.append(code.group(1))
ok('errors action 闭集自洽 (%d)' % len(actions)) if not (used - actions) else bad('未定义 action: %s' % (used - actions))
ok('每个 code 至少一个 action') if not no_action else bad('无 action: %s' % no_action)

SE = io.open('spec/stream-events.md', encoding='utf-8').read()
ok('无"缺失→stop"残留') if '缺失→`stop`' not in SE else bad('finish_reason 缺失仍映射 stop')
ok('单行上限已统一') if '8 MiB' not in SE else bad('仍有 8 MiB 单行上限')

NT = io.open('spec/schema/export-v1.notes.md', encoding='utf-8').read()
ok('导出大小口径统一') if '2GB' not in NT else bad('notes 仍写 2GB')

forbidden = ['基本完成','体验良好','性能优秀','已优化','应该没问题','差不多','问题不大']
hits = []
for f in glob.glob('spec/**/*.md', recursive=True) + glob.glob('docs/**/*.md', recursive=True):
    for line in io.open(f, encoding='utf-8').read().splitlines():
        if any(k in line for k in ('禁语','不要写','禁止','不得使用')): continue
        for w in forbidden:
            if w in line: hits.append((f, w))
ok('禁语扫描 hits=0') if not hits else bad('禁语: %s' % hits)

print('')
print('=== RESULT:', 'ALL PASS' if not fail else '%d FAILURES' % len(fail), '===')
sys.exit(0 if not fail else 1)
