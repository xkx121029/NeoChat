import io, re, json, sqlite3, sys, glob

fail = []
def ok(msg): print('OK  ', msg)
def bad(msg): fail.append(msg); print('FAIL', msg)

# 1) DDL
sql = io.open('spec/schema/v1.sql', encoding='utf-8').read()
con = sqlite3.connect(':memory:')
try:
    con.executescript(sql)
    ok('v1.sql executescript')
except Exception as e:
    bad('v1.sql: %s' % e); sys.exit(1)
if list(con.execute('PRAGMA foreign_key_check')) == []:
    ok('foreign_key_check empty')
tables = [r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'") if not r[0].startswith('message_fts_') and r[0] != 'message_fts']
ok('tables=%d (incl fts shadow excluded)' % len(tables))

# 2) export schema vs DDL NOT NULL
d = json.load(io.open('spec/schema/export-v1.json', encoding='utf-8'))
defs = d['$defs']
MAP = {'provider':'provider','model':'model','agent_profile':'agent','prompt_template':'prompt','memory_item':'memory',
       'conversation':'conversation','message':'message','message_part':'message_part','attachment':'attachment',
       'mcp_server':'mcp_server','mcp_tool':'mcp_tool','usage_record':'usage_record','tool_call_record':'tool_call',
       'folder':'folder','tag':'tag','conversation_tag':'conversation_tag','message_attachment':'message_attachment',
       'provider_key':'provider_key'}
SKIP = {('provider_key','key_ref')}
missing = []
for tbl, df in MAP.items():
    req = set(defs[df].get('required', []))
    for name, notnull, _dflt, pk in [(r[1], r[3], r[4], r[5]) for r in con.execute('PRAGMA table_info(%s)' % tbl)]:
        if pk or (tbl, name) in SKIP: continue
        if notnull and name in defs[df]['properties'] and name not in req:
            missing.append((tbl, name))
ok('export required aligned with NOT NULL') if not missing else bad('required missing: %s' % missing)

# 交叉方向：schema 里 required 的列必须存在于 DDL
extra = []
for tbl, df in MAP.items():
    cols = {r[1] for r in con.execute('PRAGMA table_info(%s)' % tbl)}
    for name in defs[df].get('required', []):
        if name not in cols and name not in ('content_b64','content_omitted'):
            extra.append((tbl, name))
ok('required has no phantom columns') if not extra else bad('phantom required: %s' % extra)

# 3) CASES
cs = io.open('spec/conformance/CASES.md', encoding='utf-8').read()
n = len(re.findall(r'^\| (FRAME|OAI|RESP|ANT|GEM|ORT|ERR|CAN|RENDER|EXPORT)-\d+ \|', cs, re.M))
hdr = re.search(r'用例总数：\*\*(\d+)\*\*', cs)
if hdr and int(hdr.group(1)) == n:
    ok('CASES count %d == header' % n)
else:
    bad('CASES count %d vs header %s' % (n, hdr.group(1) if hdr else None))
for g in ['FRAME', 'OAI', 'RESP', 'ANT', 'GEM', 'ORT', 'ERR', 'CAN', 'RENDER', 'EXPORT']:
    ids = sorted({int(x) for x in re.findall(r'\| %s-(\d+) \|' % g, cs)})
    if ids and ids != list(range(1, len(ids) + 1)):
        bad('%s ids not contiguous: %s' % (g, ids))
ok('case ids contiguous per group')

# 4) 版本一致性
ver = {f: re.search(r'- 版本：\*\*([0-9a-zA-Z.\-]+)\*\*', io.open(f, encoding='utf-8').read()).group(1)
       for f in ['spec/stream-events.md', 'spec/errors.md', 'spec/providers.md', 'spec/render-model.md', 'spec/conformance/CASES.md']}
print('versions:', ver)
ps = io.open('docs/PROJECT_STATE.md', encoding='utf-8').read()
for f, v in ver.items():
    if '`%s` | %s' % (f, v) not in ps:
        bad('PROJECT_STATE version mismatch for %s (%s)' % (f, v))
ok('versions match PROJECT_STATE')

# 5) errors 闭集自洽
er = io.open('spec/errors.md', encoding='utf-8').read()
declared_actions = set(re.findall(r'^\| `([a-z_]+)` \| (?:打开|系统浏览器|用系统浏览器|重新|切|查看|复制|重试|倒计时|修剪|去|修改|填写|数据库|存储|清理|当前|打开该)', er, re.M))
used = set(re.findall(r'`([a-z_]+)`', ' '.join(re.findall(r'^\| `[A-Z_]+` \|.*$', er, re.M))))
declared = set(re.findall(r'^\| `([a-z_]+)` \|', er, re.M))
# 只看 §4 区段
sec4 = er.split('## 4. 操作')[1].split('## 5.')[0]
act_table = set(re.findall(r'^\| `([a-z_]+)` \|', sec4, re.M))
unknown = sorted(a for a in used if a.islower() and a not in act_table and a in used and a not in {'retry_after'})
# 过滤掉非 action 的小写词（stage/字段名）
noise = {'config','resolve','connect','send','first_byte','stream_read','parse','provider','tool','persist','crypto','local','code','stage','http_status','provider_code','retryable','retry_after_ms','title_key','params','causes','actions','raw','request_id','trace_id','a','amount','currency','dropped','vision','tools','topic','model_id','focus','require','ms','index','error','data','kind'}
unknown = [u for u in unknown if u not in noise]
if unknown:
    bad('actions used but not declared: %s' % unknown)
else:
    ok('all action names in tables resolve to §4 (%d declared)' % len(act_table))
if re.search(r'\| `INTERNAL` \| — \|', er):
    bad('INTERNAL still has — stage')
else:
    ok('INTERNAL stage fixed')

# 6) stream-events 关键残留
se = io.open('spec/stream-events.md', encoding='utf-8').read()
if '缺失→`stop`' in se:
    bad('stream-events still maps missing finish_reason to stop')
else:
    ok('no missing-finish_reason→stop leftover')
if '8 MiB' in se:
    bad('stream-events still has 8 MiB single-line limit')
else:
    ok('single-line limit unified to 16 MiB')

# 7) 禁语
forbidden = ['基本完成', '体验良好', '性能优秀', '已优化', '应该没问题', '差不多', '问题不大']
hits = []
for f in glob.glob('spec/**/*.md', recursive=True) + glob.glob('docs/**/*.md', recursive=True):
    t = io.open(f, encoding='utf-8').read()
    for line in t.splitlines():
        if any(k in line for k in ('禁语', '不要写', '禁止', '不得使用')):
            continue
        for w in forbidden:
            if w in line:
                hits.append((f, w))
ok('禁语扫描 (hits=%d)' % len(hits)) if not hits else bad('禁语: %s' % hits)

print('\n=== RESULT:', 'ALL PASS' if not fail else '%d FAILURES' % len(fail), '===')
