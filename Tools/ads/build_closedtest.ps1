# クローズドテスト用の製品版(テスト広告)を、マスターのアップロード鍵で署名して作る(2026-10-26)。
# マスターが自分の PC の PowerShell で実行する。パスワードはこの画面で入力し、この1回の Unity のビルドにだけ渡す
# (ファイル・ログ・チャットには残らない。Unity の ProjectSettings にも残さない = BuildAndroid.BuildClosedTest がビルド後に戻す)。
#
# 使い方(Unity のエディタは閉じておく):
#   powershell -ExecutionPolicy Bypass -File Tools\ads\build_closedtest.ps1 -Keystore "D:\keys\onemoremile-upload.jks" -Alias upload
# 出力: Unity\2ndAction\Builds\ClosedTest\OneMoreMile_ct<番号>.aab(Play へ上げる)/ .apk(端末で直接確かめる)/ _build.txt(使った設定)
param(
    [Parameter(Mandatory = $true)][string]$Keystore,
    [Parameter(Mandatory = $true)][string]$Alias
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Keystore)) { Write-Host "鍵のファイルが見つかりません: $Keystore"; exit 1 }
if (Get-Process Unity -ErrorAction SilentlyContinue) { Write-Host "Unity が起動しています。閉じてから実行してください"; exit 1 }

function Read-Secret([string]$prompt) {
    $s = Read-Host -Prompt $prompt -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $root 'Unity\2ndAction'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe'
$log = Join-Path $env:TEMP 'omm_closedtest_build.log'

$env:OMM_UPLOAD_KEYSTORE = (Resolve-Path $Keystore).Path
$env:OMM_UPLOAD_KEY_ALIAS = $Alias
$env:OMM_UPLOAD_KEYSTORE_PASS = Read-Secret 'キーストアのパスワード'
$env:OMM_UPLOAD_KEY_PASS = Read-Secret '鍵(エイリアス)のパスワード(キーストアと同じなら同じものを入力)'
try {
    Write-Host 'ビルド中です(30分ほどかかります)...'
    $p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'BuildAndroid.BuildClosedTest', '-quit', '-logFile', "`"$log`"") -Wait -PassThru
    Write-Host "Unity の終了コード: $($p.ExitCode)"
}
finally {
    # この PowerShell の中の環境変数も消す
    Remove-Item Env:OMM_UPLOAD_KEYSTORE_PASS, Env:OMM_UPLOAD_KEY_PASS, Env:OMM_UPLOAD_KEYSTORE, Env:OMM_UPLOAD_KEY_ALIAS -ErrorAction SilentlyContinue
}
Select-String -Path $log -Pattern 'BuildAndroid: (closed test|no upload|the AdMob)|ReleaseBuildGuard\]|error CS' | ForEach-Object { $_.Line }
$info = Get-ChildItem (Join-Path $project 'Builds\ClosedTest\*_build.txt') | Sort-Object LastWriteTime | Select-Object -Last 1
if ($info) { Write-Host "`n--- $($info.Name) ---"; Get-Content $info.FullName }
