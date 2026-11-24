package com.neochat.app.data

/**
 * 契约基线。`SPEC_SHA256` 是 `spec/schema/v1.sql` 的 SHA-256，写进 `migration.checksum`，
 * 由 `SchemaSnapshotTest` 对真实文件复算——改契约不改这里会被测试挡住。
 */
internal object SchemaBaseline {

    const val VERSION: Int = 1

    const val SPEC_SHA256: String = "48d55a23774bb1827a344cfbbaff4c844ad4e96426a890b07b4a4884e68136fc"
}
