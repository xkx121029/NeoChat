CREATE VIRTUAL TABLE IF NOT EXISTS message_fts USING fts5(
  text,
  message_id      UNINDEXED,
  conversation_id UNINDEXED,
  part_index      UNINDEXED,
  tokenize = 'unicode61 remove_diacritics 2'
);
