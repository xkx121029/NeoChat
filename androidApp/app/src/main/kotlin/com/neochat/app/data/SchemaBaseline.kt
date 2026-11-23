package com.neochat.app.data

/**
 * 契约基线。`SPEC_SHA256` 是 `spec/schema/v1.sql` 的 SHA-256，写进 `migration.checksum`，
 * 由 `SchemaSnapshotTest` 对真实文件复算——改契约不改这里会被测试挡住。
 */
internal object SchemaBaseline {

    const val VERSION: Int = 1

    const val SPEC_SHA256: String = "f921e007733bf868104487f07578d8a4c0ff5bd13e517b0a2920838c92d3b2f9"
}
