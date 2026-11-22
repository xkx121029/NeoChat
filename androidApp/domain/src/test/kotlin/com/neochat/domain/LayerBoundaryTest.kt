package com.neochat.domain

import java.io.File
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * 分层依赖的机器强制：领域层不得引用 UI 层、Android 平台类型或具体 provider 实现。
 * 扫描失败即架构违规，先于人工评审拦住。
 */
class LayerBoundaryTest {

    private val forbiddenPackagePrefixes = listOf(
        "android.",
        "androidx.",
        "com.neochat.app",
        "com.neochat.ui",
        "io.ktor.",
        "okhttp3.",
    )

    @Test
    fun `domain sources do not reference ui or platform packages`() {
        val root = File(System.getProperty("neochat.domain.src")!!)
        val sources = root.walkTopDown().filter { it.isFile && it.extension == "kt" }.toList()

        assertTrue(sources.isNotEmpty(), "领域层源文件数为 0，扫描无意义：$root")

        val violations = sources.flatMap { file ->
            file.readLines()
                .withIndex()
                .filter { (_, line) -> line.trimStart().startsWith("import ") }
                .mapNotNull { (index, line) ->
                    val imported = line.removePrefix("import ").trim()
                    val hit = forbiddenPackagePrefixes.firstOrNull { imported.startsWith(it) }
                    hit?.let { "${file.path}:${index + 1} 引用 $imported（禁止前缀 $it）" }
                }
        }

        assertTrue(
            violations.isEmpty(),
            "领域层禁止引用 UI/平台/具体 provider：\n" + violations.joinToString("\n"),
        )
    }
}
