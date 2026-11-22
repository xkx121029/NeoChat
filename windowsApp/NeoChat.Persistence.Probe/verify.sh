#!/usr/bin/env bash
# M1-WIN-004 验收脚本：会话与消息落盘、重启恢复、崩溃前落盘、schema 一致性、令牌集中化。
# 用法：bash windowsApp/NeoChat.Persistence.Probe/verify.sh
# 依赖：dotnet9 SDK、python3（tools/schema-diff）、git-bash 的 cygpath。
#
# 脚本本身跑在 git-bash 里（相对路径相对仓库根），而 dotnet.exe / python3.exe 是 Windows 程序，
# 不认 /e/NeoChat 这种 MSYS 路径，所以凡是要交给它们的路径都先过 cygpath -w。
set -uo pipefail

DOTNET="$HOME/dotnet9/sdk/dotnet.exe"
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
cd "$ROOT" || exit 1
PROBE=$(cygpath -w "$ROOT/windowsApp/NeoChat.Persistence.Probe/bin/Debug/net9.0/NeoChat.Persistence.Probe.dll")
WORK=$(mktemp -d)
DB=$(cygpath -w "$WORK/neochat.db")

section() { printf '\n===== %s =====\n' "$1"; }

section "1. 第一次进程：发一条消息并落盘，然后退出"
"$DOTNET" "$PROBE" seed "$DB"

section "2. 第二次进程（全新启动）：从库里读出会话与消息"
"$DOTNET" "$PROBE" read "$DB"

section "3. 流式中途 taskkill /F"
# hold 模式永不结束流：每 200ms 追加一段文本，每一段都必须已经落盘。
"$DOTNET" "$PROBE" hold "$DB" > "$WORK/hold.log" 2>&1 &
BGPID=$!
WINPID=$(cat "/proc/$BGPID/winpid")
sleep 4
MSYS_NO_PATHCONV=1 taskkill /F /PID "$WINPID"
wait "$BGPID" 2>/dev/null
echo "--- 被杀进程临死前自己打印的库状态 ---"
cat "$WORK/hold.log"

section "3b. 重启（read 触发启动扫描）后再查库"
"$DOTNET" "$PROBE" read "$DB" | head -9
"$DOTNET" "$PROBE" status "$DB"

section "4. schema-diff：应用建出的库 vs spec/schema/v1.sql"
"$DOTNET" "$PROBE" schema "$DB" "$(cygpath -w "$WORK/app-schema.json")"
PYTHONIOENCODING=utf-8 python3 "$(cygpath -w tools/schema-diff/diff_schema.py)" \
    --sql "$(cygpath -w spec/schema/v1.sql)" \
    --json "$(cygpath -w "$WORK/app-schema.json")"

section "5. 令牌集中化：会话列表与聊天空态里没有裸尺寸/裸色值"
XAML="windowsApp/NeoChat.Windows/Views/ChatPage.xaml"
RAIL=$(sed -n '/会话列表（M1-WIN-004）/,/G15 页面光晕/p' "$XAML")
EMPTY=$(sed -n '/§3.3 空态/,/Y09 回到底部/p' "$XAML")
BARE=$(printf '%s\n%s\n' "$RAIL" "$EMPTY" | grep -E '(Width|Height|MinHeight|MaxWidth|Margin|Padding|FontSize|LineHeight|CornerRadius|Spacing)="[0-9]|="#')
if [ -n "$BARE" ]; then
    echo "$BARE"
else
    echo "(no matches: 会话列表 + 空态 0 处裸值，覆盖 $(printf '%s\n%s\n' "$RAIL" "$EMPTY" | wc -l) 行)"
fi

section "5b. 新增代码里没有色值/尺寸字面量（Tokens.cs 是唯一出处）"
LITERALS=$(
    grep -nE '0x[0-9A-Fa-f]{8}|new Thickness\([0-9]|new CornerRadius\([0-9]' \
        windowsApp/NeoChat.Windows/Core/Chat/ConversationListViewModel.cs \
        windowsApp/NeoChat.Windows/Core/Chat/ConversationTitle.cs
    git diff -U0 --relative -- windowsApp/NeoChat.Windows/Views/ChatPage.xaml.cs \
        | grep -E '^\+' | grep -vE '^\+\+\+' \
        | grep -E '0x[0-9A-Fa-f]{8}|new Thickness\([0-9]|new CornerRadius\([0-9]'
)
if [ -n "$LITERALS" ]; then
    echo "$LITERALS"
else
    echo "(no matches: 0 处)"
fi

rm -rf "$WORK"
