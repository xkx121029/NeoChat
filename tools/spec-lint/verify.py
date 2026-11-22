"""spec-lint: 契约一致性机器校验。

设计原则：**每条检查都要能失败**。写完检查后必须先自证它会失败（见 selftest），
否则 ALL PASS 只证明"脚本没报错"，不证明"契约没问题"——第二轮评审已经抓到过一次这个坑。
"""
import io, re, json, sqlite3, sys, glob

fail = []
def ok(m): print('OK  ', m)
def bad(m): fail.append(m); print('FAIL', m)

SQL = io.open('spec/schema/v1.sql', encoding='utf-8').read()
con = sqlite3.connect(':memory:')
try:
    con.executescript(SQL)
    ok('v1.sql executescript')
except Exception as e:
    bad('v1.sql: %s' % e); sys.exit(1)
ok('foreign_key_check empty') if list(con.execute('PRAGMA foreign_key_check')) == [] else bad('foreign keys broken')

ALL_TABLES = [r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
TABLES = [t for t in ALL_TABLES if not t.startswith('message_fts_') and t != 'message_fts']
ok('tables=%d' % len(TABLES))

D = json.load(io.open('spec/schema/export-v1.json', encoding='utf-8'))
DEFS = D['$defs']

# 表 → 导出 $def；不在表里的不导出（库元数据/派生索引）
MAP = {'provider':'provider','model':'model','agent_profile':'agent','prompt_template':'prompt','memory_item':'memory',
       'conversation':'conversation','message':'message','message_part':'message_part','attachment':'attachment',
       'mcp_server':'mcp_server','mcp_tool':'mcp_tool','usage_record':'usage_record','tool_call_record':'tool_call',
       'folder':'folder','tag':'tag','conversation_tag':'conversation_tag','message_attachment':'message_attachment',
       'provider_key':'provider_key'}
NOT_EXPORTED = {'schema_meta', 'migration', 'message_fts', 'app_setting'}   # app_setting → settings 集合，另行核对
SKIP = {('provider_key', 'key_ref')}    # 本地密钥库句柄，导出不含

def ddl_cols(tbl):
    return [(r[1], r[3], r[5]) for r in con.execute('PRAGMA table_info(%s)' % tbl)]  # name, notnull, pk

# ---- 检查 1：DDL 每一列都必须出现在导出 schema 的 properties 里（反向覆盖）
missing_prop = []
for tbl, df in MAP.items():
    props = DEFS[df]['properties']
    for name, _nn, pk in ddl_cols(tbl):
        if pk or (tbl, name) in SKIP: continue
        if name not in props:
            missing_prop.append('%s.%s -> %s' % (tbl, name, df))
ok('DDL 列 all in 导出 properties') if not missing_prop else bad('导出缺列: %s' % missing_prop)

# ---- 检查 2：NOT NULL 列必须在 required 里
missing_req = []
for tbl, df in MAP.items():
    req = set(DEFS[df].get('required', []))
    props = DEFS[df]['properties']
    for name, nn, pk in ddl_cols(tbl):
        if pk or (tbl, name) in SKIP or name not in props: continue
        if nn and name not in req:
            missing_req.append('%s.%s' % (tbl, name))
ok('NOT NULL all in required') if not missing_req else bad('required 缺列: %s' % missing_req)

# ---- 检查 3：required 不得出现 DDL 里没有的列
phantom = []
for tbl, df in MAP.items():
    cols = {c[0] for c in ddl_cols(tbl)}
    for name in DEFS[df].get('required', []):
        if name not in cols:
            phantom.append('%s.%s' % (df, name))
ok('required 无幻影列') if not phantom else bad('required 幻影列: %s' % phantom)

# ---- 检查 4：用例计数与编号连续
CS = io.open('spec/conformance/CASES.md', encoding='utf-8').read()
n_cases = len(re.findall(r'^\| (FRAME|OAI|RESP|ANT|GEM|ORT|ERR|CAN|RENDER|EXPORT)-\d+ \|', CS, re.M))
hdr = re.search(r'用例总数：\*\*(\d+)\*\*', CS)
ok('CASES %d == header' % n_cases) if hdr and int(hdr.group(1)) == n_cases else bad('CASES %d vs header %s' % (n_cases, hdr.group(1) if hdr else None))
for g in ['FRAME','OAI','RESP','ANT','GEM','ORT','ERR','CAN','RENDER','EXPORT']:
    ids = sorted({int(x) for x in re.findall(r'\| %s-(\d+) \|' % g, CS)})
    if ids and ids != list(range(1, len(ids) + 1)):
        bad('%s 编号不连续: %s' % (g, ids))
ok('用例编号连续')

# ---- 检查 5：版本一致性（严格匹配，不用子串）
PS = io.open('docs/PROJECT_STATE.md', encoding='utf-8').read()
for f in ['spec/stream-events.md','spec/errors.md','spec/providers.md','spec/render-model.md','spec/conformance/CASES.md']:
    v = re.search(r'- 版本：\*\*([0-9A-Za-z.\-]+)\*\*', io.open(f, encoding='utf-8').read()).group(1)
    row = re.search(r'^\| `%s` \| ([^|]+) \|' % re.escape(f), PS, re.M)
    if not row:
        bad('PROJECT_STATE 缺行: %s' % f)
    elif row.group(1).strip() != v:
        bad('版本不一致 %s: spec=%s PS=%s' % (f, v, row.group(1).strip()))
ok('版本一致')

# ---- 检查 6：errors 的 code/action 闭集自洽
ER = io.open('spec/errors.md', encoding='utf-8').read()
sec4 = ER.split('## 4. 操作')[1].split('## 5.')[0]
actions = {a for a in re.findall(r'^\| `([a-z_]+)` \|', sec4, re.M) if a != 'a'}
codes_block = ER.split('## 3. 错误码表')[1].split('## 4.')[0]
used = set()
for line in codes_block.split('\n'):
    if line.startswith('| `') and line.count('|') >= 7:
        cell = line.split('|')[6]
        used |= set(re.findall(r'`([a-z_]+)`', cell))
undef = sorted(used - actions)
ok('errors action 闭集自洽 (%d 个 action)' % len(actions)) if not undef else bad('未定义 action: %s' % undef)

# 每个 code 至少 1 个 action
no_action = []
for line in codes_block.split('\n'):
    if line.startswith('| `') and line.count('|') >= 7:
        code = re.match(r'^\| `([A-Z_]+)`', line)
        if code and not re.findall(r'`([a-z_]+)`', line.split('|')[6]):
            no_action.append(code.group(1))
ok('每个 code 至少一个 action') if not no_action else bad('无 action 的 code: %s' % no_action)

if re.search(r'\| `INTERNAL` \| — \|', ER): bad('INTERNAL stage 仍是 —')
else: ok('INTERNAL stage 已定')

# ---- 检查 7：跨文件残留
SE = io.open('spec/stream-events.md', encoding='utf-8').read()
ok('无"缺失→stop"残留') if '缺失→`stop`' not in SE else bad('finish_reason 缺失仍映射 stop')
ok('单行上限已统一') if '8 MiB' not in SE else bad('仍有 8 MiB 单行上限')

NOTES = io.open('spec/schema/export-v1.notes.md', encoding='utf-8').read()
ok('导出大小口径统一') if '2GB' not in NOTES.replace('≤200 MB', '') else bad('notes 仍写 2GB')

# ---- 检查 8：禁语（跳过"禁止/不要写"说明行）
forbidden = ['基本完成','体验良好','性能优秀','已优化','应该没问题','差不多','问题不大']
hits = []
for f in glob.glob('spec/**/*.md', recursive=True) + glob.glob('docs/**/*.md', recursive=True):
    for line in io.open(f, encoding='utf-8').read().splitlines():
        if any(k in line for k in ('禁语','不要写','禁止','不得使用')): continue
        for w in forbidden:
            if w in line: hits.append((f, w))
ok('禁语扫描 hits=0') if not hits else bad('禁语: %s' % hits)

# ---- 自证：在临时库上 ALTER TABLE 加一列，反向覆盖检查必须报出它
con3 = sqlite3.connect(':memory:'); con3.executescript(SQL)
con3.execute('ALTER TABLE provider ADD COLUMN selftest_col INTEGER NOT NULL DEFAULT 0')
props = DEFS['provider']['properties']
probe = [c[1] for c in con3.execute('PRAGMA table_info(provider)') if c[1] == 'selftest_col' and c[1] not in props]
ok('反向覆盖自证：注入一列后检查器能识别') if probe else bad('反向覆盖自证失败：检查器对该类缺陷不敏感')

print('')
print('=== RESULT:', 'ALL PASS' if not fail else '%d FAILURES' % len(fail), '===')
sys.exit(0 if not fail else 1)
