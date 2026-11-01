# クローズドテスト用の AAB / APK を確かめる(2026-11-01)。パスワードは要らない。
#  ・ファイルがあるか / 大きさ
#  ・署名: AAB は jarsigner、APK は apksigner で検証し、証明書の SHA-256 を鍵のファイルの証明書と比べる
#  ・Target API(targetSdkVersion)/ minSdk / versionCode / versionName / debuggable
# 使い方:
#   powershell -ExecutionPolicy Bypass -File Tools\ads\verify_closedtest.ps1 -VersionCode 417 -Keystore "C:\GameProject\OneMoreMileKeys\onemoremile-upload.jks" -Alias upload
#   (-Keystore を省くと、証明書の比較はせずに指紋だけ表示)
param(
    [Parameter(Mandatory = $true)][string]$VersionCode,
    [string]$Keystore,
    [string]$Alias = 'upload',
    [string]$Dir,
    [int]$TargetApi = 36
)
$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Dir) { $Dir = Join-Path $root 'Unity\2ndAction\Builds\ClosedTest' }
$and = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer'
$java = Join-Path $and 'OpenJDK\bin\java.exe'
$keytool = Join-Path $and 'OpenJDK\bin\keytool.exe'
$jarsigner = Join-Path $and 'OpenJDK\bin\jarsigner.exe'
$bt = Join-Path $and 'SDK\build-tools\36.0.0'
$bundletool = Join-Path $and 'Tools\bundletool-all-1.17.2.jar'
$env:JAVA_HOME = Join-Path $and 'OpenJDK'

$aab = Join-Path $Dir "OneMoreMile_ct$VersionCode.aab"
$apk = Join-Path $Dir "OneMoreMile_ct$VersionCode.apk"
$fail = 0
function Pass([string]$m) { Write-Host "  OK   $m" -ForegroundColor Green }
function Fail([string]$m) { Write-Host "  NG   $m" -ForegroundColor Red; $script:fail++ }
function Sha256Of([string[]]$lines) {
    $l = $lines | Where-Object { $_ -match 'SHA256:\s*([0-9A-F:]{95})' } | Select-Object -First 1
    if ($l -and $l -match 'SHA256:\s*([0-9A-F:]{95})') { return $Matches[1] } else { return $null }
}

$expected = $null
if ($Keystore) {
    # パスワードなしで証明書の指紋だけ読む(JKS は中身の検証をせずに一覧を出せる。鍵そのものは読めない)
    # 入力は NUL から(PowerShell から '' を渡すと空でないパスワードとして扱われて失敗する)
    $expected = Sha256Of (cmd /c "`"$keytool`" -J-Duser.language=en -list -v -keystore `"$((Resolve-Path $Keystore).Path)`" -alias $Alias < NUL 2>&1")
    if ($expected) { Write-Host "鍵の証明書 SHA-256: $expected" } else { Write-Host '鍵の証明書の指紋を読めませんでした(比較なし)' }
}

foreach ($f in @($aab, $apk)) {
    Write-Host "`n== $(Split-Path $f -Leaf)"
    if (-not (Test-Path $f)) { Fail 'ファイルがありません'; continue }
    Pass ('{0:N1} MB' -f ((Get-Item $f).Length / 1MB))
    $isAab = $f.EndsWith('.aab')
    if ($isAab) {
        $v = & $jarsigner '-J-Duser.language=en' -verify -certs -verbose $f 2>&1 | ForEach-Object { "$_" }
        if ($v -match 'jar verified') { Pass '署名の検証(jarsigner): jar verified' } else { Fail '署名の検証(jarsigner)に失敗' }
        $cert = Sha256Of (& $keytool '-J-Duser.language=en' -printcert -jarfile $f 2>&1 | ForEach-Object { "$_" })
        $man = (& $java -jar $bundletool dump manifest --bundle $f 2>&1 | ForEach-Object { "$_" }) -join "`n"
    }
    else {
        $v = & (Join-Path $bt 'apksigner.bat') verify --verbose --print-certs $f 2>&1 | ForEach-Object { "$_" }
        if ($v -match '^Verifies') { Pass ('署名の検証(apksigner): ' + (($v | Where-Object { $_ -match 'Verified using v\d.*true' }) -replace 'Verified using ', '' -join ', ')) } else { Fail '署名の検証(apksigner)に失敗' }
        $c = $v | Where-Object { $_ -match 'certificate SHA-256 digest:\s*([0-9a-f]{64})' } | Select-Object -First 1
        $cert = if ($c -and $c -match 'digest:\s*([0-9a-f]{64})') { (($Matches[1].ToUpper() -split '(..)' | Where-Object { $_ }) -join ':') } else { $null }
        $man = (& (Join-Path $bt 'aapt2.exe') dump badging $f 2>&1 | ForEach-Object { "$_" }) -join "`n"
    }
    if ($cert) {
        if (-not $expected) { Pass "証明書 SHA-256: $cert" }
        elseif ($cert -eq $expected) { Pass '証明書はアップロード鍵と一致' }
        else { Fail "証明書がアップロード鍵と違います: $cert" }
    }
    else { Fail '証明書を読めませんでした' }
    if ($isAab) {
        $vc = [regex]::Match($man, 'versionCode="(\d+)"').Groups[1].Value
        $vn = [regex]::Match($man, 'versionName="([^"]*)"').Groups[1].Value
        $ts = [regex]::Match($man, 'targetSdkVersion="(\d+)"').Groups[1].Value
        $ms = [regex]::Match($man, 'minSdkVersion="(\d+)"').Groups[1].Value
        $dbg = $man -match 'debuggable="true"'
    }
    else {
        $vc = [regex]::Match($man, "versionCode='(\d+)'").Groups[1].Value
        $vn = [regex]::Match($man, "versionName='([^']*)'").Groups[1].Value
        $ts = [regex]::Match($man, "targetSdkVersion:'(\d+)'").Groups[1].Value
        $ms = [regex]::Match($man, "(?m)^(?:min)?[sS]dkVersion:'(\d+)'").Groups[1].Value
        $dbg = $man -match "application-debuggable"
    }
    if ($vc -eq $VersionCode) { Pass "versionCode $vc (versionName $vn)" } else { Fail "versionCode が $vc(期待 $VersionCode)" }
    if ($ts -eq "$TargetApi") { Pass "Target API $ts (minSdk $ms)" } else { Fail "Target API が $ts(期待 $TargetApi)" }
    if (-not $dbg) { Pass 'debuggable なし' } else { Fail 'debuggable が付いています' }
}
Write-Host ''
if ($fail -eq 0) { Write-Host 'すべて OK' -ForegroundColor Green; exit 0 } else { Write-Host "NG が $fail 件あります" -ForegroundColor Red; exit 1 }
