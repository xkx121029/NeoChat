@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.Dp
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Radii

/**
 * 用户气泡（§2.1 Y11–Y22）：右对齐、宽度上限 82%、内边距 16/8、lg 圆角、`A06` 底色 + `L09` 文字。
 * 气泡内只用主文字色：三级文字与主色在这块底上不过 AA（`user_bubble.dart`:21-22）。
 */
@Composable
public fun UserBubble(text: String, columnWidth: Dp, modifier: Modifier = Modifier) {
    val colors = LocalNeoColors.current
    Row(modifier = modifier, horizontalArrangement = Arrangement.End) {
        Surface(
            color = colors.userBubble,
            shape = RoundedCornerShape(Radii.lg),
            modifier = Modifier.widthIn(max = columnWidth * Metrics.userBubbleWidthFraction),
        ) {
            Text(
                text = text,
                style = MaterialTheme.typography.bodyLarge.copy(color = colors.onUserBubble),
                modifier = Modifier.padding(
                    horizontal = Metrics.userBubblePaddingH,
                    vertical = Metrics.userBubblePaddingV,
                ),
            )
        }
    }
}
