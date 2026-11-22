<#
.SYNOPSIS
  录制 NeoChat Windows 的"逐 token 生长 + Markdown 成形"演示，产出 GIF。

.DESCRIPTION
  流水线：启动本地 mock SSE（若端口未监听）→ 启动应用（指向 mock）→ 用 UI Automation
  在输入框填一句话并点发送 → 立即每 150ms 用 PrintWindow(PW_RENDERFULLCONTENT) 抓一帧
  → 抓满指定帧数 → 关闭应用 → 调 build_demo_gif.py 拼成 GIF。

  为什么用 PrintWindow 而不是桌面截屏：
  1) 本机没有 Capy 桌面通道，系统级录屏 API 不可用；
  2) PrintWindow 让窗口自己绘制到内存 DC，不依赖窗口是否在最前，也不会被其它窗口遮挡。

  为什么帧先留在内存、抓完再落盘：
  实测（1536x798）PrintWindow 约 107ms/帧，Bitmap.Save(BMP) 约 136ms/帧；边抓边存是 440ms/帧，
  会彻底破坏 150ms 节奏。先缓冲 40 张 Bitmap（约 230MB），抓完再统一存 BMP，实测稳定 150ms/帧。

  注意：本机不同显示器 DPI 不同（96 或 120）。PrintWindow 按 DIP 尺寸绘制内容，所以 125% 缩放
  下窗口矩形（1920x998）右下会留纯黑。裁剪由 build_demo_gif.py 按非黑包围盒统一处理，
  抓帧侧不假设窗口尺寸，也不改窗口大小（改尺寸反而会让内容区和窗口区错位）。

.PARAMETER Port
  mock SSE 端口。默认 8799，避免和别的会话占用的 8787 冲突。

.PARAMETER TokenDelay
  mock 每两个 token 的间隔（秒）。默认 0.62：8 个 chunk 约 5s 长完，40 帧里绝大部分都在长，尾段留几帧看渲染结果。

.PARAMETER Prompt
  通过 UI Automation 填进输入框的那句话。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\demo\capture_demo.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 8799,
    [double]$TokenDelay = 0.62,
    [int]$Frames = 40,
    [int]$IntervalMs = 150,
    [string]$Prompt = '介绍一下你自己，顺便说说流式渲染',
    [string]$OutDir = '',
    [string]$GifPath = '',
    [switch]$KeepFrames
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $repoRoot 'build\demo-frames' }
if ([string]::IsNullOrWhiteSpace($GifPath)) { $GifPath = Join-Path $repoRoot 'docs\reviews\media\M1-WIN-demo.gif' }
$exe = Join-Path $repoRoot 'windowsApp\NeoChat.Windows\bin\Release\net9.0-windows10.0.26100.0\win-x64\NeoChat.Windows.exe'
$mockScript = Join-Path $repoRoot 'tools\demo\mock_openai_sse.py'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NeoChatCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
"@

# ---- 0. 前置检查 ------------------------------------------------------------
if (-not (Test-Path $exe)) {
    throw "应用可执行文件不存在：$exe —— 先构建 windowsApp/NeoChat.Windows/NeoChat.Windows.csproj -c Release -p:EnableCoreMrtTooling=false"
}
if (-not (Test-Path $mockScript)) { throw "mock 脚本不存在：$mockScript" }

$python = Get-Command python3 -ErrorAction SilentlyContinue
if ($null -eq $python) { $python = Get-Command python -ErrorAction SilentlyContinue }
if ($null -eq $python) { throw '找不到 python3/python：无法启动 mock，也无法拼 GIF' }

# ---- 1. 启动 mock（端口已在监听就复用） -------------------------------------
function Test-Port([int]$candidate) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $pending = $client.BeginConnect('127.0.0.1', $candidate, $null, $null)
        if (-not $pending.AsyncWaitHandle.WaitOne(400)) { return $false }
        $client.EndConnect($pending)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

$mockProcess = $null
if (Test-Port $Port) {
    Write-Output "mock: 端口 $Port 已在监听，复用"
} else {
    $mockProcess = Start-Process -FilePath $python.Source -ArgumentList @($mockScript, "$Port", "$TokenDelay") -PassThru -WindowStyle Hidden
    for ($i = 0; $i -lt 50 -and -not (Test-Port $Port); $i++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Port $Port)) { throw "mock 在端口 $Port 启动失败" }
    Write-Output ("mock: 已启动 pid=" + $mockProcess.Id + " http://127.0.0.1:$Port/v1 tokenDelay=" + $TokenDelay + "s")
}

# ---- 2. 启动应用（指向 mock） ----------------------------------------------
Get-Process NeoChat.Windows -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$env:NEOCHAT_BASE_URL = "http://127.0.0.1:$Port/v1"
$env:NEOCHAT_MODEL = 'mock-chat'
$env:NEOCHAT_API_KEY = 'mock-no-auth'
$app = Start-Process -FilePath $exe -PassThru

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 200; $i++) {
    Start-Sleep -Milliseconds 100
    $app.Refresh()
    if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $app.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { throw "应用窗口 20s 内没有出现（pid=$($app.Id)）" }
Start-Sleep -Milliseconds 800   # 等 OnLoaded 的历史恢复与焦点落定
Write-Output ("app: pid=" + $app.Id + " hwnd=" + $hwnd)

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$descendants = [System.Windows.Automation.TreeScope]::Descendants
function Find-ById([string]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst($descendants, $condition)
}

# ---- 3. 开一段新对话（避免上一次会话的画面干扰） ----------------------------
$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$clickedNew = $false
foreach ($button in $root.FindAll($descendants, $buttonCondition)) {
    if ($button.Current.Name -eq '新对话') {
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $clickedNew = $true
        break
    }
}
Write-Output ("ui: 新对话按钮 " + $(if ($clickedNew) { '已点击' } else { '未找到，沿用当前会话' }))
Start-Sleep -Milliseconds 600

# ---- 4. UI Automation 填输入框并检查发送键 ----------------------------------
$composer = Find-ById 'ComposerInput'
if ($null -eq $composer) { throw '找不到输入框 ComposerInput（UI Automation 树变了？）' }
$composer.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Prompt)
Start-Sleep -Milliseconds 400
$sendKey = Find-ById 'SendKey'
if ($null -eq $sendKey) { throw '找不到发送键 SendKey' }
if (-not $sendKey.Current.IsEnabled) { throw '发送键仍是禁用状态：输入框内容没有进到 ViewModel' }
Write-Output ("ui: 已填入 '" + $Prompt + "'，发送键 IsEnabled=" + $sendKey.Current.IsEnabled)

$rect = New-Object NeoChatCapture+RECT
[void][NeoChatCapture]::GetWindowRect($hwnd, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
Write-Output ("window: " + $width + "x" + $height)

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# ---- 5. 发送，并立刻按固定节奏抓帧（帧先留在内存） --------------------------
# 位图全部预分配：单张分配约 30ms，放在发送之后会挤掉 150ms 档期
$buffers = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $Frames; $i++) { [void]$buffers.Add((New-Object System.Drawing.Bitmap($width, $height))) }
Write-Output ("frames: 已预分配 " + $Frames + " 张 " + $width + "x" + $height + " 位图")

$sendKey.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$marks = New-Object int[] $Frames
for ($i = 0; $i -lt $Frames; $i++) {
    $bitmap = $buffers[$i]
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    # 0x2 = PW_RENDERFULLCONTENT：缺了这个标志，DirectComposition 内容抓出来是全黑
    $ok = [NeoChatCapture]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    if (-not $ok) { throw "第 $i 帧 PrintWindow 返回 false" }
    $marks[$i] = [int]$clock.ElapsedMilliseconds
    $due = ($i + 1) * $IntervalMs
    $sleep = $due - $clock.ElapsedMilliseconds
    if ($sleep -gt 0) { Start-Sleep -Milliseconds $sleep }
}
$elapsed = $clock.ElapsedMilliseconds
$gaps = 0
for ($i = 1; $i -lt $Frames; $i++) { if (($marks[$i] - $marks[$i - 1]) -gt $gaps) { $gaps = $marks[$i] - $marks[$i - 1] } }
Write-Output ("capture: " + $Frames + " 帧，用时 " + $elapsed + "ms，平均 " + [math]::Round($elapsed / $Frames, 1) + "ms/帧，最大帧间隔 " + $gaps + "ms")

# ---- 6. 落盘、关闭应用 ------------------------------------------------------
$writeClock = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt $Frames; $i++) {
    $framePath = Join-Path $OutDir ('frame{0:d3}.bmp' -f $i)
    $buffers[$i].Save($framePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $buffers[$i].Dispose()
}
$buffers.Clear()
Write-Output ("frames: " + $Frames + " 张 BMP 已写入 " + $OutDir + "（落盘 " + $writeClock.ElapsedMilliseconds + "ms）")

Get-Process NeoChat.Windows -ErrorAction SilentlyContinue | Stop-Process -Force
if ($null -ne $mockProcess -and -not $mockProcess.HasExited) {
    $mockProcess | Stop-Process -Force
    Write-Output 'mock: 已停止'
}
Write-Output 'app: 已关闭'

# ---- 7. 拼 GIF --------------------------------------------------------------
$builder = Join-Path $PSScriptRoot 'build_demo_gif.py'
$builderArgs = @($builder, '--frames', $OutDir, '--out', $GifPath, '--duration-ms', "$IntervalMs")
if (-not $KeepFrames) { $builderArgs += '--prune' }
& $python.Source @builderArgs
if ($LASTEXITCODE -ne 0) { throw "build_demo_gif.py 退出码 $LASTEXITCODE" }
Write-Output ("gif: " + $GifPath)
