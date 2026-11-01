# クローズドテスト用の製品版(テスト広告)を、マスターのアップロード鍵で署名して作る(2026-10-26)。
# マスターが自分の PC の PowerShell で実行する。パスワードはこの画面で入力し、この1回の Unity のビルドにだけ渡す
# (ファイル・ログ・チャットには残らない。Unity の ProjectSettings にも残さない = BuildAndroid.BuildClosedTest がビルド後に戻す)。
#
# 使い方(Unity のエディタは閉じておく):
#   powershell -ExecutionPolicy Bypass -File Tools\ads\build_closedtest.ps1 -Keystore "D:\keys\onemoremile-upload.jks" -Alias upload
# 出力: Unity\2ndAction\Builds\ClosedTest\OneMoreMile_ct<番号>.aab(Play へ上げる)/ .apk(端末で直接確かめる)/ _build.txt(使った設定)
#
# 2026-11-01 追加:
#  ・ビルドの前に、入力したパスワードで鍵を開けるかを keytool で確かめる(違えばその場で入力し直し。30分待って失敗しない)
#    keytool へは -storepass:env / -keypass:env で渡す(コマンドラインにパスワードを出さない)
#  ・Gradle が失敗した時に Unity がログへ環境変数を書き出すので、ビルド後にログからパスワードの行を消す
#  ・Library の Gradle の build.gradle に Unity が書いたパスワードも消す(BuildAndroid 側でも消すが、Unity が途中で落ちた時の分)
#  ・最後に Tools\ads\verify_closedtest.ps1 で 署名・Target API・versionCode を確かめる(パスワード不要)
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
$editorDir = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor'
$unity = Join-Path $editorDir 'Unity.exe'
$keytool = Join-Path $editorDir 'Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe'
$log = Join-Path $env:TEMP 'omm_closedtest_build.log'
$secretNames = 'OMM_UPLOAD_KEYSTORE_PASS', 'OMM_UPLOAD_KEY_PASS'

# keytool で鍵を開けるか(パスワードは環境変数から読ませる。出力は捨てる)
function Test-KeyAccess([string]$ks, [string]$alias) {
    $tmp = Join-Path $env:TEMP ('omm_ct_check_' + [Guid]::NewGuid().ToString('N') + '.csr')
    $ErrorActionPreference = 'Continue' # keytool のエラー出力で止まらないように(PowerShell 5.1)
    try {
        & $keytool -list -keystore $ks -alias $alias -storepass:env OMM_UPLOAD_KEYSTORE_PASS *> $null
        if ($LASTEXITCODE -ne 0) { return 'キーストアのパスワードが違うか、別名(エイリアス)が見つかりません' }
        # 署名用の鍵そのものを使えるか(証明書の要求を一時ファイルへ作るだけ。鍵もキーストアも変わらない)
        & $keytool -certreq -keystore $ks -alias $alias -storepass:env OMM_UPLOAD_KEYSTORE_PASS -keypass:env OMM_UPLOAD_KEY_PASS -file $tmp *> $null
        if ($LASTEXITCODE -ne 0) { return '鍵(エイリアス)のパスワードが違います' }
        return $null
    }
    finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

# ログと Gradle のファイルからパスワードを消す
function Clear-Secrets {
    if (Test-Path $log) {
        $lines = [IO.File]::ReadAllLines($log)
        $changed = $false
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match '^(OMM_UPLOAD_KEYSTORE_PASS|OMM_UPLOAD_KEY_PASS)\s*=' -or $lines[$i] -match '^\s*(storePassword|keyPassword)\s+[''"].+[''"]\s*$') {
                $lines[$i] = ($lines[$i] -replace '=.*$', '= <removed>') -replace '(storePassword|keyPassword)\s+.*$', '$1 <removed>'
                $changed = $true
            }
        }
        if ($changed) { [IO.File]::WriteAllLines($log, $lines, (New-Object Text.UTF8Encoding($false))) }
    }
    $prj = Join-Path $project 'Library\Bee\Android\Prj'
    if (Test-Path $prj) {
        Get-ChildItem $prj -Recurse -Filter build.gradle | ForEach-Object {
            $s = [IO.File]::ReadAllText($_.FullName)
            $t = [Text.RegularExpressions.Regex]::Replace($s, '(?m)^(\s*(?:storePassword|keyPassword)\s+)([''"]).*?\2\s*$', '$1''''')
            if ($t -ne $s) { [IO.File]::WriteAllText($_.FullName, $t, (New-Object Text.UTF8Encoding($false))) }
        }
    }
}

$env:OMM_UPLOAD_KEYSTORE = (Resolve-Path $Keystore).Path
$env:OMM_UPLOAD_KEY_ALIAS = $Alias
try {
    $ok = $false
    for ($try = 1; $try -le 3 -and -not $ok; $try++) {
        $env:OMM_UPLOAD_KEYSTORE_PASS = Read-Secret 'キーストアのパスワード'
        $env:OMM_UPLOAD_KEY_PASS = Read-Secret '鍵(エイリアス)のパスワード(キーストアと同じなら同じものを入力)'
        $err = Test-KeyAccess $env:OMM_UPLOAD_KEYSTORE $Alias
        if ($err) { Write-Host "鍵を開けませんでした: $err(もう一度入力してください。$try/3)" -ForegroundColor Yellow }
        else { $ok = $true; Write-Host '鍵を開けることを確認しました。' }
    }
    if (-not $ok) { Write-Host '3回とも鍵を開けなかったので、ビルドしません。鍵を作った時のパスワードを確かめてください。'; exit 1 }

    Write-Host 'ビルド中です(30分ほどかかります)...'
    $p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'BuildAndroid.BuildClosedTest', '-quit', '-logFile', "`"$log`"") -Wait -PassThru
    Write-Host "Unity の終了コード: $($p.ExitCode)"
}
finally {
    # この PowerShell の中の環境変数も消す
    Remove-Item Env:OMM_UPLOAD_KEYSTORE_PASS, Env:OMM_UPLOAD_KEY_PASS, Env:OMM_UPLOAD_KEYSTORE, Env:OMM_UPLOAD_KEY_ALIAS -ErrorAction SilentlyContinue
    Clear-Secrets
    # 鍵の場所を空へ戻すと Unity は ProjectSettings に '{inproject}: ' と書く → 空に直す(AndroidKeystoreName は空のまま、の決まり)
    $ps = Join-Path $project 'ProjectSettings\ProjectSettings.asset'
    $t = [IO.File]::ReadAllText($ps)
    $u = $t -replace "(?m)^(  AndroidKeystoreName:) '\{inproject\}: '(\r?)$", '$1 $2'
    if ($u -ne $t) { [IO.File]::WriteAllText($ps, $u, (New-Object Text.UTF8Encoding($false))) }
}
Select-String -Path $log -Pattern 'BuildAndroid: (closed test|no upload|the AdMob|cleared)|ReleaseBuildGuard\]|error CS' | ForEach-Object { $_.Line }
# Gradle が失敗した時は、最初の原因の行を見せる
$gradle = Select-String -Path $log -Pattern '^\* What went wrong:' -Context 0, 4 | Select-Object -First 1
if ($gradle) { Write-Host "`n--- Gradle の失敗 ---"; $gradle.Context.PostContext | ForEach-Object { Write-Host $_ } }
$info = Get-ChildItem (Join-Path $project 'Builds\ClosedTest\*_build.txt') | Sort-Object LastWriteTime | Select-Object -Last 1
if ($info) { Write-Host "`n--- $($info.Name) ---"; Get-Content $info.FullName }
if ($info) {
    $code = [regex]::Match((Get-Content $info.FullName -Raw), 'versionCode=(\d+)').Groups[1].Value
    & (Join-Path $PSScriptRoot 'verify_closedtest.ps1') -VersionCode $code -Keystore $Keystore -Alias $Alias
}
