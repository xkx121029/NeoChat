package com.neochat.protocol

/** 最小 JSON 值模型：数字保留原始字面量，避免浮点往返改写（`provider_cost.amount` 必须逐字节稳定）。 */
public sealed interface JsonValue {

    public data class Obj(public val fields: Map<String, JsonValue>) : JsonValue {
        public operator fun get(key: String): JsonValue? = fields[key]
    }

    public data class Arr(public val items: List<JsonValue>) : JsonValue

    public data class Str(public val value: String) : JsonValue

    public data class Num(public val raw: String) : JsonValue {
        public fun toLongOrNull(): Long? = raw.toLongOrNull() ?: raw.toDoubleOrNull()?.toLong()
        public fun toDoubleOrNull(): Double? = raw.toDoubleOrNull()
    }

    public data class Bool(public val value: Boolean) : JsonValue

    public data object Null : JsonValue
}

/** JSON 结构非法（语法错误、嵌套超限）。 */
public class JsonParseException(message: String) : Exception(message)

public fun JsonValue.asObjOrNull(): JsonValue.Obj? = this as? JsonValue.Obj

public fun JsonValue.field(key: String): JsonValue? = asObjOrNull()?.fields?.get(key)

public fun JsonValue.stringOrNull(): String? = (this as? JsonValue.Str)?.value

public fun JsonValue.longOrNull(): Long? = (this as? JsonValue.Num)?.toLongOrNull()

public fun JsonValue.doubleOrNull(): Double? = (this as? JsonValue.Num)?.toDoubleOrNull()

public fun JsonValue.numberRaw(): String? = (this as? JsonValue.Num)?.raw

public fun JsonValue.boolOrNull(): Boolean? = (this as? JsonValue.Bool)?.value

public fun JsonValue.isJsonNull(): Boolean = this is JsonValue.Null

/** 解析与序列化。序列化对对象键排序，保证同一事件模型产出同一字节串。 */
public object Json {

    private const val MAX_DEPTH: Int = 64

    public fun parse(text: String): JsonValue = Reader(text).document()

    public fun write(value: JsonValue): String = StringBuilder().also { writeValue(value, it) }.toString()

    public fun obj(vararg pairs: Pair<String, JsonValue?>): JsonValue.Obj =
        JsonValue.Obj(pairs.mapNotNull { (key, value) -> value?.let { key to it } }.toMap())

    public fun str(value: String): JsonValue = JsonValue.Str(value)

    public fun num(value: Long): JsonValue = JsonValue.Num(value.toString())

    private fun writeValue(value: JsonValue, out: StringBuilder) {
        when (value) {
            is JsonValue.Obj -> writeObject(value, out)
            is JsonValue.Arr -> writeArray(value, out)
            is JsonValue.Str -> writeString(value.value, out)
            is JsonValue.Num -> out.append(value.raw)
            is JsonValue.Bool -> out.append(if (value.value) "true" else "false")
            JsonValue.Null -> out.append("null")
        }
    }

    private fun writeObject(value: JsonValue.Obj, out: StringBuilder) {
        out.append('{')
        var first = true
        for (key in value.fields.keys.sorted()) {
            if (!first) out.append(',')
            first = false
            writeString(key, out)
            out.append(':')
            writeValue(value.fields.getValue(key), out)
        }
        out.append('}')
    }

    private fun writeArray(value: JsonValue.Arr, out: StringBuilder) {
        out.append('[')
        value.items.forEachIndexed { index, item ->
            if (index > 0) out.append(',')
            writeValue(item, out)
        }
        out.append(']')
    }

    private fun writeString(value: String, out: StringBuilder) {
        out.append('"')
        for (ch in value) {
            when {
                ch == '"' -> out.append("\\\"")
                ch == '\\' -> out.append("\\\\")
                ch == '\n' -> out.append("\\n")
                ch == '\r' -> out.append("\\r")
                ch == '\t' -> out.append("\\t")
                ch == '\b' -> out.append("\\b")
                ch == '\u000C' -> out.append("\\f")
                ch.code < 0x20 -> out.append("\\u").append(ch.code.toString(16).padStart(4, '0'))
                else -> out.append(ch)
            }
        }
        out.append('"')
    }

    private class Reader(private val text: String) {

        private var index: Int = 0

        fun document(): JsonValue {
            val value = parseValue(0)
            skipWhitespace()
            if (index != text.length) throw JsonParseException("trailing content at $index")
            return value
        }

        private fun parseValue(depth: Int): JsonValue {
            if (depth > MAX_DEPTH) throw JsonParseException("nesting deeper than $MAX_DEPTH")
            skipWhitespace()
            if (index >= text.length) throw JsonParseException("unexpected end of input")
            return when (text[index]) {
                '{' -> parseObject(depth)
                '[' -> parseArray(depth)
                '"' -> JsonValue.Str(parseString())
                't' -> parseLiteral("true", JsonValue.Bool(true))
                'f' -> parseLiteral("false", JsonValue.Bool(false))
                'n' -> parseLiteral("null", JsonValue.Null)
                else -> parseNumber()
            }
        }

        private fun parseObject(depth: Int): JsonValue.Obj {
            index++
            skipWhitespace()
            val fields = LinkedHashMap<String, JsonValue>()
            if (peek() == '}') {
                index++
                return JsonValue.Obj(fields)
            }
            while (true) {
                skipWhitespace()
                if (peek() != '"') throw JsonParseException("object key expected at $index")
                val key = parseString()
                skipWhitespace()
                expect(':')
                fields[key] = parseValue(depth + 1)
                skipWhitespace()
                when (peek()) {
                    ',' -> index++
                    '}' -> {
                        index++
                        return JsonValue.Obj(fields)
                    }
                    else -> throw JsonParseException("object separator expected at $index")
                }
            }
        }

        private fun parseArray(depth: Int): JsonValue.Arr {
            index++
            skipWhitespace()
            val items = ArrayList<JsonValue>()
            if (peek() == ']') {
                index++
                return JsonValue.Arr(items)
            }
            while (true) {
                items.add(parseValue(depth + 1))
                skipWhitespace()
                when (peek()) {
                    ',' -> index++
                    ']' -> {
                        index++
                        return JsonValue.Arr(items)
                    }
                    else -> throw JsonParseException("array separator expected at $index")
                }
            }
        }

        private fun parseString(): String {
            expect('"')
            val sb = StringBuilder()
            while (true) {
                if (index >= text.length) throw JsonParseException("unterminated string")
                val ch = text[index++]
                when {
                    ch == '"' -> return sb.toString()
                    ch == '\\' -> sb.append(parseEscape())
                    ch.code < 0x20 -> throw JsonParseException("raw control character in string")
                    else -> sb.append(ch)
                }
            }
        }

        private fun parseEscape(): Char {
            if (index >= text.length) throw JsonParseException("unterminated escape")
            return when (val esc = text[index++]) {
                '"' -> '"'
                '\\' -> '\\'
                '/' -> '/'
                'b' -> '\b'
                'f' -> '\u000C'
                'n' -> '\n'
                'r' -> '\r'
                't' -> '\t'
                'u' -> parseUnicodeEscape()
                else -> throw JsonParseException("unknown escape \\$esc")
            }
        }

        private fun parseUnicodeEscape(): Char {
            if (index + 4 > text.length) throw JsonParseException("truncated unicode escape")
            val code = text.substring(index, index + 4).toIntOrNull(16)
                ?: throw JsonParseException("bad unicode escape")
            index += 4
            return code.toChar()
        }

        private fun parseNumber(): JsonValue.Num {
            val start = index
            if (peek() == '-') index++
            while (index < text.length && text[index].isDigit()) index++
            if (index < text.length && text[index] == '.') {
                index++
                while (index < text.length && text[index].isDigit()) index++
            }
            if (index < text.length && (text[index] == 'e' || text[index] == 'E')) {
                index++
                if (index < text.length && (text[index] == '+' || text[index] == '-')) index++
                while (index < text.length && text[index].isDigit()) index++
            }
            if (start == index) throw JsonParseException("number expected at $start")
            return JsonValue.Num(text.substring(start, index))
        }

        private fun parseLiteral(literal: String, value: JsonValue): JsonValue {
            if (!text.startsWith(literal, index)) throw JsonParseException("literal $literal expected at $index")
            index += literal.length
            return value
        }

        private fun skipWhitespace() {
            while (index < text.length && text[index].let { it == ' ' || it == '\t' || it == '\n' || it == '\r' }) {
                index++
            }
        }

        private fun peek(): Char {
            if (index >= text.length) throw JsonParseException("unexpected end of input")
            return text[index]
        }

        private fun expect(ch: Char) {
            if (peek() != ch) throw JsonParseException("'$ch' expected at $index")
            index++
        }
    }
}

/** 字符串的 UTF-8 字节长度，用于 16 MiB 上限的精确计量。 */
public fun utf8ByteLength(value: CharSequence): Long {
    var total = 0L
    for (ch in value) {
        total += when {
            ch.isHighSurrogate() -> 4L
            ch.isLowSurrogate() -> 0L
            ch.code < 0x80 -> 1L
            ch.code < 0x800 -> 2L
            else -> 3L
        }
    }
    return total
}
