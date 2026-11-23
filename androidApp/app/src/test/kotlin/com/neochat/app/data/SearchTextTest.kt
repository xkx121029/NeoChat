package com.neochat.app.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * `message_fts` 分词口径的单元测试（纯 JVM，不需要 FTS5）：
 * 拉丁与数字原样成词，CJK 切成相邻两字的二元组，查询串用同一函数拼成短语。
 * 真实命中/排序在 `androidTest` 的 `MessageSearchTest` 里跑（Robolectric 的 SQLite 没编 FTS5）。
 */
class SearchTextTest {

    @Test
    fun `两个汉字切成一个二元组`() {
        assertEquals("你好", SearchText.tokens("你好"))
        assertEquals("\"你好\"", SearchText.matchExpression("你好"))
    }

    @Test
    fun `整句中文切成相邻两字的二元组且保持顺序`() {
        assertEquals("帮我 我总 总结 结这 这篇 篇文 文章", SearchText.tokens("帮我总结这篇文章"))
    }

    @Test
    fun `四字查询拼成三元组短语`() {
        assertEquals("\"文章 章要 要点\"", SearchText.matchExpression("文章要点"))
    }

    @Test
    fun `拉丁、数字与中文各自成段`() {
        assertEquals("GPT 4 支持 中文", SearchText.tokens("GPT-4 支持 中文"))
        assertEquals("\"GPT\" \"4\" \"支持\" \"中文\"", SearchText.matchExpression("GPT-4 支持 中文"))
    }

    @Test
    fun `标点与空白不进分词结果，纯符号查询返回 null`() {
        assertEquals("", SearchText.tokens("，。！？ \n\t"))
        assertNull(SearchText.matchExpression("，。！？"))
        assertNull(SearchText.matchExpression("   "))
    }

    @Test
    fun `搜索用例里的三条消息与两个查询的分词结果`() {
        assertEquals(
            "帮我 我总 总结 结这 这篇 篇文 文章 章要 要点",
            SearchText.tokens("帮我总结这篇文章要点"),
        )
        assertEquals("再总 总结 结一 一下 下今 今天 天的 的会 会议", SearchText.tokens("再总结一下今天的会议"))
        assertEquals("把下 下面 面这 这段 段话 话改 改得 得更 更简 简洁", SearchText.tokens("把下面这段话改得更简洁"))
        assertEquals("\"总结\"", SearchText.matchExpression("总结"))
        assertEquals("\"文章 章要 要点\"", SearchText.matchExpression("文章要点"))
        assertEquals("\"会议\"", SearchText.matchExpression("会议"))
        assertEquals("\"简洁\"", SearchText.matchExpression("简洁"))
    }

    @Test
    fun `单个汉字保留单字 token`() {
        assertEquals("猫", SearchText.tokens("猫"))
        assertEquals("\"猫\"", SearchText.matchExpression("猫"))
    }
}
