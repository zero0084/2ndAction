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
# 2026-11-02: 後片付けが長いパスの Gradle の出力で止まり、ProjectSettings を直せなかった → 1段ずつたどり、1つ失敗しても続ける。
#  未処理があれば最後に一覧を出して終了コード 2(成果物の確認の結果とは別)
#  また、パスワードは Unity のビルドの計画(Library\Bee)と Gradle の翻訳済みスクリプト(~\.gradle\caches)にも写るので、それも消す
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

# 後片付けで処理できなかった物(最後にまとめて表示する)
$cleanupIssues = New-Object Collections.Generic.List[string]
function Add-CleanupIssue([string]$m) { $cleanupIssues.Add($m); Write-Host "  後片付けの未処理: $m" -ForegroundColor Yellow }

# Unity が書いた build.gradle を探す。2026-11-02: Get-ChildItem -Recurse は Gradle の出力の中の長いパス(248文字以上)で
# 「パスの一部が見つかりません」と止まった(フォルダは実在)→ 自分で1段ずつたどる。
#  ・Gradle の出力(build / .gradle / .cxx)には入らない(Unity が書く build.gradle は浅い所だけ)
#  ・長いパスも読めるように \\?\ を付けて開く
#  ・探している間にフォルダが消えても、読めないフォルダがあっても、残りを続ける(読めなかった物は未処理として記録)
function Find-GradleFiles([string]$top) {
    $found = New-Object Collections.Generic.List[string]
    $stack = New-Object Collections.Generic.Stack[string]
    $stack.Push($top)
    while ($stack.Count -gt 0) {
        $d = $stack.Pop()
        $long = if ($d.StartsWith('\\?\')) { $d } else { '\\?\' + $d }
        try {
            foreach ($f in [IO.Directory]::GetFiles($long, 'build.gradle*')) { $found.Add($f) } # build.gradle と、Unity の控えの build.gradle.xml
            foreach ($s in [IO.Directory]::GetDirectories($long)) {
                if ([IO.Path]::GetFileName($s) -notin 'build', '.gradle', '.cxx') { $stack.Push($s) }
            }
        }
        catch [IO.DirectoryNotFoundException] { } # 探している間に消えた(Gradle の片付けなど)= 消す物も無い
        catch { Add-CleanupIssue "フォルダを読めませんでした: $($d -replace '^\\\\\?\\', '') ($($_.Exception.GetType().Name))" }
    }
    return , $found
}

# ログと Gradle のファイルからパスワードを消す(1つ失敗しても残りを続ける)
function Clear-Secrets {
    try {
        if (Test-Path -LiteralPath $log) {
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
    }
    catch { Add-CleanupIssue "ログのパスワードの行を消せませんでした: $log ($($_.Exception.GetType().Name))" }
    # Gradle のファイル: Prj(Gradle が読む物)と artifacts\Android\Gradle(Unity の控え。.xml にも同じ値)
    $n = 0
    foreach ($top in (Join-Path $project 'Library\Bee\Android\Prj'), (Join-Path $project 'Library\Bee\artifacts\Android\Gradle')) {
        if (-not (Test-Path -LiteralPath $top)) { continue } # 無い = 消す物も無い
        foreach ($f in (Find-GradleFiles $top)) {
            try { if (Edit-Bytes $f { param($s) $rxGradle.Replace($s, '$1$3') }) { $n++ } }
            catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { } # 消えた
            catch { Add-CleanupIssue "Gradle のファイルのパスワードを消せませんでした: $($f -replace '^\\\\\?\\', '') ($($_.Exception.GetType().Name))" }
        }
    }
    Write-Host "Gradle のファイルからパスワードを消しました: $n 件"
    Clear-BeeGraphSecrets
    Clear-GradleScriptCache
}

# 2026-11-02 使い捨ての鍵の試験で分かった、パスワードが写される所(値はそのまま入る):
#  ・Library\Bee の Unity のビルドの計画(<名前>-inputdata.json / <名前>.dag* の "KeystorePassword":"…" "KeystoreAliasPassword":"…")、
#    tundra.log.json、artifacts\csharpactions\*.info
#  ・~\.gradle\caches の、build.gradle を Gradle が翻訳した class(groovy-dsl\<hash>、jars-9\<hash>)
# 値を知らなくても形で見つかるので、空にするか、作り直せる物は消す(次のビルドで Unity / Gradle が作り直す。ゲームの中身は変わらない)
$latin1 = [Text.Encoding]::GetEncoding(28591) # 1バイト = 1文字(読んで書き戻してもバイトが変わらない)
$rxGradle = New-Object Text.RegularExpressions.Regex "((?:storePassword|keyPassword)\s+')([^'\r\n&]+)(')"
$rxBee = New-Object Text.RegularExpressions.Regex '(Keystore(?:Alias)?Password\\{0,3}"\s*:\s*\\{0,3}")((?:[^"\\]|\\[^"])+)(\\{0,3}")'
$rxClass = New-Object Text.RegularExpressions.Regex "\x01([\x00-\xff]{2})([^\x00-\x08]{1,128}?)\x08[\x00-\xff]{2}\x01\x00(?:\x0dstorePassword|\x0bkeyPassword)"

# ファイルを1バイト=1文字で読んで $edit で書き換え、変わった時だけ書き戻す(変わったら $true)
function Edit-Bytes([string]$f, [scriptblock]$edit) {
    $p = if ($f.StartsWith('\\?\')) { $f } else { '\\?\' + $f }
    $s = $latin1.GetString([IO.File]::ReadAllBytes($p))
    $t = & $edit $s
    if ($t -ceq $s) { return $false }
    [IO.File]::WriteAllBytes($p, $latin1.GetBytes($t))
    return $true
}

function Clear-BeeGraphSecrets {
    $bee = Join-Path $project 'Library\Bee'
    if (-not (Test-Path -LiteralPath $bee)) { return }
    $files = @()
    try { $files += [IO.Directory]::GetFiles($bee) } catch { Add-CleanupIssue "フォルダを読めませんでした: $bee ($($_.Exception.GetType().Name))" }
    $ca = Join-Path $bee 'artifacts\csharpactions'
    try { if (Test-Path -LiteralPath $ca) { $files += [IO.Directory]::GetFiles($ca, '*.info') } } catch { Add-CleanupIssue "フォルダを読めませんでした: $ca ($($_.Exception.GetType().Name))" }
    $graphs = New-Object Collections.Generic.HashSet[string]
    $nEdit = 0; $nDel = 0
    foreach ($f in $files) {
        try {
            $name = [IO.Path]::GetFileName($f)
            $g = [regex]::Match($name, '^(.+?)(?:-inputdata\.json|\.dag.*)$')
            if ($g.Success) {
                # ビルドの計画は中身が2進数を含むので書き換えず、計画ごと消す(次のビルドで作り直す)
                if ($rxBee.IsMatch($latin1.GetString([IO.File]::ReadAllBytes($f)))) { [void]$graphs.Add($g.Groups[1].Value) }
            }
            elseif ($f -like '*\csharpactions\*.info') {
                if ($rxBee.IsMatch($latin1.GetString([IO.File]::ReadAllBytes($f)))) { [IO.File]::Delete($f); $nDel++ }
            }
            elseif ((Get-Item -LiteralPath $f).Length -lt 200MB -and (Edit-Bytes $f { param($s) $rxBee.Replace($s, '$1$3') })) { $nEdit++ } # tundra.log.json など
        }
        catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { }
        catch { Add-CleanupIssue "Unity のビルドの記録のパスワードを消せませんでした: $f ($($_.Exception.GetType().Name))" }
    }
    foreach ($g in $graphs) {
        foreach ($f in [IO.Directory]::GetFiles($bee, "$g*")) {
            if ([IO.Path]::GetFileName($f) -notmatch ('^' + [regex]::Escape($g) + '(?:-inputdata\.json|\.dag.*)$')) { continue }
            try { [IO.File]::Delete($f); $nDel++ }
            catch { Add-CleanupIssue "Unity のビルドの計画を消せませんでした: $f ($($_.Exception.GetType().Name))" }
        }
    }
    Write-Host "Unity のビルドの記録からパスワードを消しました: 書き換え $nEdit 件 / 削除 $nDel 件(計画 $($graphs.Count) 組)"
}

function Clear-GradleScriptCache {
    $caches = Join-Path $env:USERPROFILE '.gradle\caches'
    if (-not (Test-Path -LiteralPath $caches)) { return }
    # Gradle のデーモンが動いている間は消さない(使用中の物を消すと次のビルドが壊れることがある)
    $daemon = Get-CimInstance Win32_Process -Filter "Name='java.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match 'GradleDaemon' }
    if ($daemon) { Add-CleanupIssue "Gradle のデーモンが動いているので ~\.gradle\caches の翻訳済みスクリプトを消していません(PID $(($daemon.ProcessId) -join ',')。終わってからもう一度この後片付けを)"; return }
    $dirs = @()
    foreach ($kind in (Get-ChildItem -LiteralPath $caches -Directory -ErrorAction SilentlyContinue)) {
        # caches\<版>\groovy-dsl\<hash> と caches\jars-<n>\<hash>
        if ($kind.Name -match '^jars-\d+$') { $dirs += Get-ChildItem -LiteralPath $kind.FullName -Directory -ErrorAction SilentlyContinue }
        $gd = Join-Path $kind.FullName 'groovy-dsl'
        if (Test-Path -LiteralPath $gd) { $dirs += Get-ChildItem -LiteralPath $gd -Directory -ErrorAction SilentlyContinue }
    }
    $n = 0
    foreach ($d in $dirs) {
        try {
            $hit = $false
            foreach ($c in [IO.Directory]::GetFiles('\\?\' + $d.FullName, '*.class', 'AllDirectories')) {
                $s = $latin1.GetString([IO.File]::ReadAllBytes($c))
                if (-not $s.Contains('Password')) { continue }
                foreach ($m in $rxClass.Matches($s)) {
                    $len = [int]$m.Groups[1].Value[0] * 256 + [int]$m.Groups[1].Value[1]
                    if ($len -eq $m.Groups[2].Value.Length) { $hit = $true; break }
                }
                if ($hit) { break }
            }
            if ($hit) { [IO.Directory]::Delete('\\?\' + $d.FullName, $true); $n++ }
        }
        catch [IO.DirectoryNotFoundException] { }
        catch { Add-CleanupIssue "Gradle の翻訳済みスクリプトを消せませんでした: $($d.FullName) ($($_.Exception.GetType().Name))" }
    }
    Write-Host "Gradle の翻訳済みスクリプトのうち、パスワードを含む物を消しました: $n 件"
}

# 鍵の場所を空へ戻すと Unity は ProjectSettings に '{inproject}: ' と書く → 空に直す(AndroidKeystoreName は空のまま、の決まり)
function Restore-ProjectSettings {
    $ps = Join-Path $project 'ProjectSettings\ProjectSettings.asset'
    try {
        $t = [IO.File]::ReadAllText($ps)
        $u = $t -replace "(?m)^(  AndroidKeystoreName:) '\{inproject\}: '(\r?)$", '$1 $2'
        if ($u -ne $t) { [IO.File]::WriteAllText($ps, $u, (New-Object Text.UTF8Encoding($false))) }
        $line = ([IO.File]::ReadAllLines($ps) | Where-Object { $_ -match '^  AndroidKeystoreName:' } | Select-Object -First 1)
        if ($line -match '^  AndroidKeystoreName: ?$') { Write-Host 'ProjectSettings: 鍵の場所は空です(OK)' }
        else { Add-CleanupIssue "ProjectSettings の AndroidKeystoreName が空ではありません: $ps" }
    }
    catch { Add-CleanupIssue "ProjectSettings を直せませんでした: $ps ($($_.Exception.GetType().Name))" }
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
    # この PowerShell の中の環境変数も消す。後片付けは1つ失敗しても次へ進む(ProjectSettings も必ず直す)
    $ErrorActionPreference = 'Continue'
    Remove-Item Env:OMM_UPLOAD_KEYSTORE_PASS, Env:OMM_UPLOAD_KEY_PASS, Env:OMM_UPLOAD_KEYSTORE, Env:OMM_UPLOAD_KEY_ALIAS -ErrorAction SilentlyContinue
    try { Clear-Secrets } catch { Add-CleanupIssue "パスワードの消去が途中で止まりました ($($_.Exception.GetType().Name))" }
    Restore-ProjectSettings
    if ($cleanupIssues.Count -eq 0) { Write-Host '後片付け: すべて完了' -ForegroundColor Green }
    else { Write-Host "後片付け: 未処理が $($cleanupIssues.Count) 件あります(上の「後片付けの未処理」を確認してください)" -ForegroundColor Red }
}
$ErrorActionPreference = 'Continue'
if (Test-Path -LiteralPath $log) {
    Select-String -Path $log -Pattern 'BuildAndroid: (closed test|no upload|the AdMob|cleared|could not)|ReleaseBuildGuard\]|error CS' | ForEach-Object { $_.Line }
    # Gradle が失敗した時は、最初の原因の行を見せる
    $gradle = Select-String -Path $log -Pattern '^\* What went wrong:' -Context 0, 4 | Select-Object -First 1
    if ($gradle) { Write-Host "`n--- Gradle の失敗 ---"; $gradle.Context.PostContext | ForEach-Object { Write-Host $_ } }
}
$info = Get-ChildItem (Join-Path $project 'Builds\ClosedTest\*_build.txt') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
if ($info) { Write-Host "`n--- $($info.Name) ---"; Get-Content $info.FullName }
$verified = 1
if ($info) {
    $code = [regex]::Match((Get-Content $info.FullName -Raw), 'versionCode=(\d+)').Groups[1].Value
    & (Join-Path $PSScriptRoot 'verify_closedtest.ps1') -VersionCode $code -Keystore $Keystore -Alias $Alias
    $verified = $LASTEXITCODE
}
if ($cleanupIssues.Count -gt 0) { exit 2 }
exit $verified
