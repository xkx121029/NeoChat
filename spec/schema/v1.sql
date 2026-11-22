-- spec/schema/v1.sql — NeoChat 数据库 DDL v1.0.0-draft
-- 状态：DRAFT（待独立评审）
-- 起草：2026-11-22
--
-- 双端共用这一份 DDL：
--   Android 用 Room，但 Room 生成的 schema 必须与本文件等价（由 M0-QE-001 的
--   schema-diff 检查：导出 Room 的 createSql 与本文件归一化后逐句比对）。
--   Windows 用 Microsoft.Data.Sqlite，直接执行本文件。
--
-- 通用约定：
--   * 主键 id 为 UUIDv7 的 36 字符小写文本，按时间有序，便于分页与同步。
--   * 所有时间戳为 UTC epoch 毫秒（INTEGER）。
--   * 所有布尔为 INTEGER 0/1，带 CHECK。
--   * 软删除统一用 deleted_at（NULL = 未删除）。回收站保留 30 天。
--   * 版本向量字段：sync_version（单调递增计数器）、rev（每行唯一修订串）、
--     origin_device（写入设备 id）。本期不实现同步，但字段必须先有。
--   * 字符串一律 TEXT；金额为 REAL（估算值，非账务数据），货币为 ISO 4217。

-- ---------------------------------------------------------------- 运行时设置
-- 下面这些 PRAGMA 不在本 DDL 内执行，但应用启动时必须设置，缺一即为缺陷：
--   PRAGMA journal_mode = WAL;
--   PRAGMA foreign_keys = ON;
--   PRAGMA synchronous = NORMAL;
--   PRAGMA busy_timeout = 5000;
--   PRAGMA temp_store = MEMORY;

-- ---------------------------------------------------------------- 元数据

CREATE TABLE schema_meta (
  key    TEXT PRIMARY KEY,
  value  TEXT NOT NULL
);

CREATE TABLE migration (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  name          TEXT    NOT NULL UNIQUE,
  checksum      TEXT    NOT NULL,
  applied_at    INTEGER NOT NULL,
  snapshot_path TEXT,                 -- 迁移前自动快照（四件套之一）
  integrity_ok  INTEGER NOT NULL DEFAULT 0 CHECK (integrity_ok IN (0,1))
);

-- ---------------------------------------------------------------- Provider 与密钥

CREATE TABLE provider (
  id                    TEXT PRIMARY KEY,
  name                  TEXT NOT NULL,
  protocol              TEXT NOT NULL CHECK (protocol IN
                          ('openai_chat','openai_responses','anthropic','gemini','openrouter','openai_compatible')),
  base_url              TEXT NOT NULL,
  org_id                TEXT,
  proxy_mode            TEXT NOT NULL DEFAULT 'system' CHECK (proxy_mode IN ('system','none','http','socks5')),
  proxy_url             TEXT,
  connect_timeout_ms    INTEGER NOT NULL DEFAULT 10000 CHECK (connect_timeout_ms BETWEEN 1000 AND 120000),
  read_idle_timeout_ms  INTEGER NOT NULL DEFAULT 180000 CHECK (read_idle_timeout_ms BETWEEN 30000 AND 900000),
  first_byte_timeout_ms INTEGER NOT NULL DEFAULT 60000 CHECK (first_byte_timeout_ms BETWEEN 5000 AND 300000),
  total_timeout_ms      INTEGER NOT NULL DEFAULT 0 CHECK (total_timeout_ms = 0 OR total_timeout_ms >= 30000),
  max_retries           INTEGER NOT NULL DEFAULT 2 CHECK (max_retries BETWEEN 0 AND 5),
  concurrency_limit     INTEGER NOT NULL DEFAULT 4 CHECK (concurrency_limit BETWEEN 1 AND 32),
  extra_headers_json    TEXT,
  extra_query_json      TEXT,
  extra_body_json       TEXT,
  pass_through          INTEGER NOT NULL DEFAULT 0 CHECK (pass_through IN (0,1)),
  billing_url           TEXT,     -- errors.md 的 top_up 用；NULL 表示该 action 不出现
  status_url            TEXT,     -- errors.md 的 view_service_status 用；NULL 表示不出现
  sort_order            INTEGER NOT NULL DEFAULT 0,
  sync_version          INTEGER NOT NULL DEFAULT 1,
  rev                   TEXT NOT NULL,
  origin_device         TEXT,
  created_at            INTEGER NOT NULL,
  updated_at            INTEGER NOT NULL,
  deleted_at            INTEGER
);
CREATE INDEX idx_provider_alive ON provider(deleted_at) WHERE deleted_at IS NULL;

-- 密钥本体不入库：key_ref 指向平台密钥库（Keystore / DPAPI）中的条目标识。
CREATE TABLE provider_key (
  id                 TEXT PRIMARY KEY,
  provider_id        TEXT NOT NULL REFERENCES provider(id) ON DELETE CASCADE,
  label              TEXT NOT NULL DEFAULT '',
  key_ref            TEXT NOT NULL,          -- 密钥库条目标识，不含密钥内容
  hint_head          TEXT NOT NULL,          -- 首 4 位（明文展示用途）
  hint_tail          TEXT NOT NULL,          -- 尾 4 位
  health_score       REAL NOT NULL DEFAULT 1.0 CHECK (health_score BETWEEN 0.0 AND 1.0),
  cooldown_until     INTEGER,
  rpm_limit          INTEGER,
  tpm_limit          INTEGER,
  daily_request_limit INTEGER,
  failure_count      INTEGER NOT NULL DEFAULT 0,
  enabled            INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0,1)),
  last_used_at       INTEGER,
  created_at         INTEGER NOT NULL,
  updated_at         INTEGER NOT NULL,
  deleted_at         INTEGER,
  UNIQUE (provider_id, key_ref)
);
CREATE INDEX idx_provider_key_lookup ON provider_key(provider_id, enabled) WHERE deleted_at IS NULL;

-- ---------------------------------------------------------------- 模型

CREATE TABLE model (
  id                TEXT PRIMARY KEY,
  provider_id       TEXT NOT NULL REFERENCES provider(id) ON DELETE CASCADE,
  model_key         TEXT NOT NULL,           -- 线上模型 id
  display_name      TEXT NOT NULL,
  context_window    INTEGER,
  max_output        INTEGER,
  supports_vision   INTEGER NOT NULL DEFAULT 0 CHECK (supports_vision IN (0,1)),
  supports_tools    INTEGER NOT NULL DEFAULT 0 CHECK (supports_tools IN (0,1)),
  supports_thinking INTEGER NOT NULL DEFAULT 0 CHECK (supports_thinking IN (0,1)),
  supports_cache    INTEGER NOT NULL DEFAULT 0 CHECK (supports_cache IN (0,1)),
  supports_json_schema INTEGER NOT NULL DEFAULT 0 CHECK (supports_json_schema IN (0,1)),
  max_tokens_param  TEXT NOT NULL DEFAULT 'max_tokens' CHECK (max_tokens_param IN ('max_tokens','max_completion_tokens')),
  price_input       REAL,                    -- 每百万 token
  price_output      REAL,
  price_cache_read  REAL,
  price_cache_write REAL,
  currency          TEXT NOT NULL DEFAULT 'USD',
  thinking_budget_max INTEGER,
  is_custom         INTEGER NOT NULL DEFAULT 0 CHECK (is_custom IN (0,1)),
  pinned_by_user    INTEGER NOT NULL DEFAULT 0 CHECK (pinned_by_user IN (0,1)),
  extra_json        TEXT,
  sync_version      INTEGER NOT NULL DEFAULT 1,
  rev               TEXT NOT NULL,
  origin_device     TEXT,
  created_at        INTEGER NOT NULL,
  updated_at        INTEGER NOT NULL,
  deleted_at        INTEGER,
  UNIQUE (provider_id, model_key)
);
CREATE INDEX idx_model_provider ON model(provider_id) WHERE deleted_at IS NULL;

-- ---------------------------------------------------------------- Agent / 提示词 / 记忆

CREATE TABLE agent_profile (
  id                  TEXT PRIMARY KEY,
  name                TEXT NOT NULL,
  avatar_ref          TEXT,                  -- 本地附件 id 或内置头像 key
  system_prompt       TEXT NOT NULL DEFAULT '',
  default_model_id    TEXT REFERENCES model(id) ON DELETE SET NULL,
  sampling_json       TEXT,                  -- 采样参数默认值
  tool_allow_json     TEXT,                  -- 工具白名单
  mcp_server_ids_json TEXT,
  greeting            TEXT,
  context_policy_json TEXT,                  -- 修剪策略
  memory_scope        TEXT NOT NULL DEFAULT 'agent' CHECK (memory_scope IN ('agent','global','off')),
  sort_order          INTEGER NOT NULL DEFAULT 0,
  sync_version        INTEGER NOT NULL DEFAULT 1,
  rev                 TEXT NOT NULL,
  origin_device       TEXT,
  created_at          INTEGER NOT NULL,
  updated_at          INTEGER NOT NULL,
  deleted_at          INTEGER
);

CREATE TABLE prompt_template (
  id             TEXT PRIMARY KEY,
  title          TEXT NOT NULL,
  category       TEXT,
  body           TEXT NOT NULL,
  variables_json TEXT,                       -- 声明的变量列表，用于校验 {{...}}
  sort_order     INTEGER NOT NULL DEFAULT 0,
  sync_version   INTEGER NOT NULL DEFAULT 1,
  rev            TEXT NOT NULL,
  origin_device  TEXT,
  created_at     INTEGER NOT NULL,
  updated_at     INTEGER NOT NULL,
  deleted_at     INTEGER
);
CREATE INDEX idx_prompt_category ON prompt_template(category) WHERE deleted_at IS NULL;

CREATE TABLE memory_item (
  id                     TEXT PRIMARY KEY,
  scope                  TEXT NOT NULL CHECK (scope IN ('agent','global')),
  agent_id               TEXT REFERENCES agent_profile(id) ON DELETE CASCADE,
  content                TEXT NOT NULL,
  keywords               TEXT,               -- 召回用（分词后空格分隔）
  pinned                 INTEGER NOT NULL DEFAULT 0 CHECK (pinned IN (0,1)),
  source_conversation_id TEXT,
  source_message_id      TEXT,
  sync_version           INTEGER NOT NULL DEFAULT 1,
  rev                    TEXT NOT NULL,
  origin_device          TEXT,
  created_at             INTEGER NOT NULL,
  updated_at             INTEGER NOT NULL,
  deleted_at             INTEGER,
  CHECK (scope = 'global' OR agent_id IS NOT NULL)
);
CREATE INDEX idx_memory_scope ON memory_item(scope, agent_id) WHERE deleted_at IS NULL;

-- ---------------------------------------------------------------- 会话与消息树

CREATE TABLE folder (
  id         TEXT PRIMARY KEY,
  name       TEXT NOT NULL,
  parent_id  TEXT REFERENCES folder(id) ON DELETE CASCADE,
  sort_order INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  deleted_at INTEGER
);

CREATE TABLE tag (
  id         TEXT PRIMARY KEY,
  name       TEXT NOT NULL UNIQUE,
  created_at INTEGER NOT NULL
);

CREATE TABLE conversation (
  id                    TEXT PRIMARY KEY,
  title                 TEXT NOT NULL DEFAULT '',
  title_auto            INTEGER NOT NULL DEFAULT 1 CHECK (title_auto IN (0,1)),
  agent_id              TEXT REFERENCES agent_profile(id) ON DELETE SET NULL,
  folder_id             TEXT REFERENCES folder(id) ON DELETE SET NULL,
  pinned                INTEGER NOT NULL DEFAULT 0 CHECK (pinned IN (0,1)),
  archived              INTEGER NOT NULL DEFAULT 0 CHECK (archived IN (0,1)),
  active_leaf_message_id TEXT,               -- 不加 FK：与 message.conversation_id 构成环
  settings_json         TEXT,                -- 本会话的模型与采样覆盖
  last_message_at       INTEGER,
  message_count         INTEGER NOT NULL DEFAULT 0,
  sync_version          INTEGER NOT NULL DEFAULT 1,
  rev                   TEXT NOT NULL,
  origin_device         TEXT,
  created_at            INTEGER NOT NULL,
  updated_at            INTEGER NOT NULL,
  deleted_at            INTEGER
);
CREATE INDEX idx_conv_list ON conversation(archived, pinned, last_message_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_conv_folder ON conversation(folder_id) WHERE deleted_at IS NULL;

CREATE TABLE message (
  id              TEXT PRIMARY KEY,
  conversation_id TEXT NOT NULL REFERENCES conversation(id) ON DELETE CASCADE,
  parent_id       TEXT REFERENCES message(id) ON DELETE CASCADE,
  role            TEXT NOT NULL CHECK (role IN ('system','user','assistant','tool')),
  sibling_index   INTEGER NOT NULL DEFAULT 0,   -- 同一 parent 下的顺序（分支切换用）
  status          TEXT NOT NULL DEFAULT 'complete'
                    CHECK (status IN ('streaming','complete','failed','aborted')),
  finish_reason   TEXT,
  error_json      TEXT,                          -- errors.md §2 的错误对象
  provider_id     TEXT,
  model_id        TEXT,
  truncated       INTEGER NOT NULL DEFAULT 0 CHECK (truncated IN (0,1)),
  sync_version    INTEGER NOT NULL DEFAULT 1,
  rev             TEXT NOT NULL,
  origin_device   TEXT,
  created_at      INTEGER NOT NULL,
  updated_at      INTEGER NOT NULL,
  deleted_at      INTEGER
);
CREATE INDEX idx_msg_tree   ON message(conversation_id, parent_id, sibling_index) WHERE deleted_at IS NULL;
CREATE INDEX idx_msg_recent ON message(conversation_id, created_at DESC) WHERE deleted_at IS NULL;

CREATE TABLE message_part (
  id         TEXT PRIMARY KEY,
  message_id TEXT NOT NULL REFERENCES message(id) ON DELETE CASCADE,
  part_index INTEGER NOT NULL,
  kind       TEXT NOT NULL CHECK (kind IN
               ('text','reasoning','tool_call','tool_result','image','attachment_ref','notice')),
  body       TEXT,
  meta_json  TEXT,
  truncated  INTEGER NOT NULL DEFAULT 0 CHECK (truncated IN (0,1)),  -- 该 part 本身被截断（ADR-003 §2.3）
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  UNIQUE (message_id, part_index)
);
CREATE INDEX idx_part_message ON message_part(message_id, part_index);

-- ---------------------------------------------------------------- 附件

CREATE TABLE attachment (
  id             TEXT PRIMARY KEY,
  sha256         TEXT NOT NULL UNIQUE,       -- 内容哈希去重
  size_bytes     INTEGER NOT NULL,
  mime           TEXT NOT NULL,
  kind           TEXT NOT NULL CHECK (kind IN ('image','pdf','doc','text','code','other')),
  local_path     TEXT NOT NULL,              -- 相对应用数据目录，禁止绝对路径
  page_count     INTEGER,
  extracted_text TEXT,                       -- 本地抽取结果（PDF/DOCX）
  ref_count      INTEGER NOT NULL DEFAULT 0, -- 引用计数，删会话不误删共享附件
  created_at     INTEGER NOT NULL,
  deleted_at     INTEGER
);

CREATE TABLE message_attachment (
  message_id    TEXT NOT NULL REFERENCES message(id) ON DELETE CASCADE,
  attachment_id TEXT NOT NULL REFERENCES attachment(id) ON DELETE CASCADE,
  part_index    INTEGER NOT NULL DEFAULT 0,
  role_hint     TEXT CHECK (role_hint IN ('user_input','model_output','reference')),
  PRIMARY KEY (message_id, attachment_id, part_index)
);

-- ---------------------------------------------------------------- 用量与成本

CREATE TABLE usage_record (
  id                TEXT PRIMARY KEY,
  message_id        TEXT REFERENCES message(id) ON DELETE SET NULL,
  provider_id       TEXT,
  model_id          TEXT,
  conversation_id   TEXT,
  input_tokens      INTEGER NOT NULL DEFAULT 0,
  output_tokens     INTEGER NOT NULL DEFAULT 0,
  cached_tokens     INTEGER NOT NULL DEFAULT 0,
  reasoning_tokens  INTEGER NOT NULL DEFAULT 0,
  cost_estimate     REAL,                    -- 本地按单价估算
  provider_cost     REAL,                    -- provider 明确回报（OpenRouter）
  currency          TEXT NOT NULL DEFAULT 'USD',
  price_snapshot_json TEXT,
  is_estimate       INTEGER NOT NULL DEFAULT 0 CHECK (is_estimate IN (0,1)),
  error_bound_pct   REAL,                    -- 估算误差上界（如 15.0 表示 ±15%）
  created_at        INTEGER NOT NULL
);
CREATE INDEX idx_usage_time  ON usage_record(created_at);
CREATE INDEX idx_usage_model ON usage_record(provider_id, model_id, created_at);

-- ---------------------------------------------------------------- 工具与 MCP

CREATE TABLE mcp_server (
  id             TEXT PRIMARY KEY,
  name           TEXT NOT NULL,
  transport      TEXT NOT NULL CHECK (transport IN ('http','stdio')),
  url            TEXT,                        -- http
  command        TEXT,                        -- stdio
  args_json      TEXT,
  env_json       TEXT,                        -- 值为密钥库引用，不含明文
  headers_json   TEXT,                        -- 值可为密钥库引用
  enabled        INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0,1)),
  last_check_at  INTEGER,
  last_check_ok  INTEGER CHECK (last_check_ok IN (0,1)),
  created_at     INTEGER NOT NULL,
  updated_at     INTEGER NOT NULL,
  deleted_at     INTEGER,
  CHECK ((transport = 'http' AND url IS NOT NULL) OR (transport = 'stdio' AND command IS NOT NULL))
);

CREATE TABLE mcp_tool (
  id                TEXT PRIMARY KEY,
  server_id         TEXT NOT NULL REFERENCES mcp_server(id) ON DELETE CASCADE,
  name              TEXT NOT NULL,
  description       TEXT,
  input_schema_json TEXT,
  enabled           INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0,1)),
  danger_level      TEXT NOT NULL DEFAULT 'safe' CHECK (danger_level IN ('safe','confirm','dangerous')),
  created_at        INTEGER NOT NULL,
  UNIQUE (server_id, name)
);

CREATE TABLE tool_call_record (
  id          TEXT PRIMARY KEY,
  message_id  TEXT NOT NULL REFERENCES message(id) ON DELETE CASCADE,
  call_index  INTEGER NOT NULL,              -- 对应 StreamEvent 的 index
  name        TEXT NOT NULL,
  args_json   TEXT,
  result_json TEXT,
  status      TEXT NOT NULL DEFAULT 'running'
                CHECK (status IN ('running','ok','error','cancelled','awaiting_approval')),
  error_json  TEXT,
  started_at  INTEGER NOT NULL,
  finished_at INTEGER,
  UNIQUE (message_id, call_index)
);

-- ---------------------------------------------------------------- 其它

CREATE TABLE conversation_tag (
  conversation_id TEXT NOT NULL REFERENCES conversation(id) ON DELETE CASCADE,
  tag_id          TEXT NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
  PRIMARY KEY (conversation_id, tag_id)
);

CREATE TABLE app_setting (
  key        TEXT PRIMARY KEY,
  value_json TEXT NOT NULL,
  updated_at INTEGER NOT NULL
);

-- 同步预留：硬删除墓碑。本期只写不读，为将来的 SyncProvider 留出"删除可传播"。
CREATE TABLE tombstone (
  entity     TEXT NOT NULL,
  entity_id  TEXT NOT NULL,
  deleted_at INTEGER NOT NULL,
  rev        TEXT NOT NULL,
  PRIMARY KEY (entity, entity_id)
);

-- ---------------------------------------------------------------- 全文搜索

-- 分词方案：FTS5 + unicode61，索引的是**已分词文本**（中文用 ICU/WinRT 词语切分器
-- 在写入时切成空格分隔的 token）。理由：unicode61 不切中文；trigram 无法支持
-- 2 字查询（"会议"），而 2 字查询是中文用户的常见输入。
-- 风险：Android ICU BreakIterator 与 WinRT WordsSegmenter 的分词结果可能不同。
-- 缓解：分词一致性由 conformance 金语料强制（偏差 >2% 则改 trigram + LIKE 兜底）；
-- 该取舍必须在 M3 前由 ADR-004 定案。
--
-- FTS 维护契约（不使用触发器，因为分词必须在应用层做）：
--   1. 仓库层在**同一事务**内写 message_part 与 message_fts。
--   2. 只索引 kind='text' 的 part，且父 message 的 deleted_at IS NULL。
--   3. 每日自检：COUNT(message_fts) 与 COUNT(符合上述条件的 part) 必须相等，
--      不等则触发 rebuild（记入 diagnostics）。
CREATE VIRTUAL TABLE message_fts USING fts5(
  text,
  message_id      UNINDEXED,
  conversation_id UNINDEXED,
  part_index      UNINDEXED,
  tokenize = 'unicode61 remove_diacritics 2'
);

-- 回收站：30 天后物理清理（含 FTS 行、附件引用计数、孤立附件）。
-- 清理顺序固定：FTS → message_part → message → conversation → attachment 引用计数。
-- 附件 ref_count 归零且 deleted_at 非空超过 30 天 → 删除文件与行。

-- ---------------------------------------------------------------- 初始数据

INSERT INTO schema_meta(key, value) VALUES ('schema_version', '1');
INSERT INTO schema_meta(key, value) VALUES ('created_at', CAST(strftime('%s','now') AS TEXT) || '000');
INSERT INTO app_setting(key, value_json, updated_at)
  VALUES ('ui.locale', '"zh-CN"', CAST(strftime('%s','now') AS INTEGER) * 1000);
INSERT INTO app_setting(key, value_json, updated_at)
  VALUES ('privacy.telemetry', 'false', CAST(strftime('%s','now') AS INTEGER) * 1000);
