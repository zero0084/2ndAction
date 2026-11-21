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
    [string]$Prefix = 'OneMoreMile_ct', # 2026-11-15: Play 配信用は -Dir (Builds の Production) -Prefix OneMoreMile_v
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

$aab = Join-Path $Dir "$Prefix$VersionCode.aab"
$apk = Join-Path $Dir "$Prefix$VersionCode.apk"
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
    # AdMob のアプリ ID(本番。テスト広告の版も同じ ID)
    $manAll = if ($isAab) { $man } else { $man + "`n" + ((& (Join-Path $bt 'aapt2.exe') dump xmltree $f --file AndroidManifest.xml 2>&1 | ForEach-Object { "$_" }) -join "`n") } # badging には meta-data が出ない
    if ($manAll -match 'ca-app-pub-2863190967263764~2717237567') { Pass 'AdMob のアプリ ID(本番の ID)が入っている' } else { Fail 'AdMob のアプリ ID(本番の ID)が見つかりません' }
    if ($manAll -match 'ca-app-pub-3940256099942544~') { Fail 'Google のテスト用アプリ ID が入っています' }
    # 2026-11-21: Wi-Fi を必須にしない(LAN の権限から Play が「必須」と推定するのを、AndroidLanPermissions が required="false" で打ち消す)
    if ($isAab) {
        $wifiDecl = [regex]::Match($man, '<uses-feature[^>]*"android\.hardware\.wifi"[^>]*>').Value
        if ($wifiDecl -match 'android:required="false"') { Pass 'Wi-Fi は必須ではない(uses-feature android.hardware.wifi required="false")' }
        elseif ($wifiDecl) { Fail "Wi-Fi が必須として申告されています: $wifiDecl" }
        else { Fail 'Wi-Fi の uses-feature がありません(Wi-Fi の権限から Play が「必須」と推定します)' }
    }
    else {
        if ($man -match "uses-feature-not-required: name='android\.hardware\.wifi'" -and $man -notmatch "uses-implied-feature: name='android\.hardware\.wifi'" -and $man -notmatch "(?m)^\s*uses-feature: name='android\.hardware\.wifi'") { Pass 'Wi-Fi は必須ではない(aapt2: uses-feature-not-required)' }
        else { Fail 'Wi-Fi が必須として扱われています(aapt2 の uses-feature / uses-implied-feature を確認)' }
    }
}
# 2026-11-15: Play 配信用(OneMoreMile_v)は、ビルドの記録で本番広告・パスの欄・署名を確かめる
if ($Prefix -eq 'OneMoreMile_v') {
    $bt2 = Join-Path $Dir "$Prefix${VersionCode}_build.txt"
    Write-Host "`n== $(Split-Path $bt2 -Leaf)"
    if (-not (Test-Path $bt2)) { Fail 'ビルドの記録がありません' }
    else {
        $t = Get-Content $bt2 -Raw
        if ($t -match 'adsProfile=PRODUCTION') { Pass '本番広告の版(OMM_ADS_PROD はこのビルドの間だけ)' } else { Fail '本番広告の版ではありません' }
        if ($t -match 'interstitialUnit=ca-app-pub-2863190967263764/\d+ rewardedUnit=ca-app-pub-2863190967263764/\d+') { Pass '広告ユニットは本番の ID' } else { Fail '広告ユニットが本番の ID ではありません' }
        if ($t -match 'noAdsPass=hidden') { Pass '広告なしパスの購入欄は出さない(既存の権利は保つ)' } else { Fail '広告なしパスの購入欄の設定が想定と違います' }
        if ($t -match 'signing=upload key') { Pass 'アップロード鍵で署名' } else { Fail 'アップロード鍵の署名ではありません' }
        if ($t -match 'developmentBuild=false debuggable=false') { Pass '開発版ではない' } else { Fail '開発版の設定です' }
    }
}
Write-Host ''
if ($fail -eq 0) { Write-Host 'すべて OK' -ForegroundColor Green; exit 0 } else { Write-Host "NG が $fail 件あります" -ForegroundColor Red; exit 1 }
