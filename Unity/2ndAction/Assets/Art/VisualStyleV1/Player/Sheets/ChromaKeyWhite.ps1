# Removes a solid/near-white background locally (no browser round-trip
# needed) by making near-white pixels transparent, with a soft feathered
# falloff for anti-aliased edges. Safe for these character sheets since
# the character art has no white elements (dark armor, red cape, black
# hair). Overwrites $Path in place.
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [int]$LowThreshold = 10,
    [int]$HighThreshold = 45
)

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile($Path)
$w = $img.Width
$h = $img.Height
$out = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$srcData = $img.LockBits((New-Object System.Drawing.Rectangle(0,0,$w,$h)), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$dstData = $out.LockBits((New-Object System.Drawing.Rectangle(0,0,$w,$h)), [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $srcData.Stride
$total = $stride * $h
$srcBytes = New-Object byte[] $total
[System.Runtime.InteropServices.Marshal]::Copy($srcData.Scan0, $srcBytes, 0, $total)
$dstBytes = New-Object byte[] $total

for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $o = $y * $stride + $x * 4
        $b = $srcBytes[$o]; $g = $srcBytes[$o+1]; $r = $srcBytes[$o+2]; $a = $srcBytes[$o+3]
        # "Distance from white" - the darkest channel dominates since a
        # near-white pixel has ALL channels high; any one channel dipping
        # (e.g. a reddish or dark pixel) pulls this down fast.
        $dist = 255 - [Math]::Min($r, [Math]::Min($g, $b))
        if ($a -eq 0 -or $dist -le $LowThreshold) {
            $newA = 0
        } elseif ($dist -ge $HighThreshold) {
            $newA = $a
        } else {
            $t = ($dist - $LowThreshold) / [double]($HighThreshold - $LowThreshold)
            $newA = [byte]([Math]::Round($a * $t))
        }
        $dstBytes[$o] = $b
        $dstBytes[$o+1] = $g
        $dstBytes[$o+2] = $r
        $dstBytes[$o+3] = $newA
    }
}

[System.Runtime.InteropServices.Marshal]::Copy($dstBytes, 0, $dstData.Scan0, $total)
$img.UnlockBits($srcData)
$out.UnlockBits($dstData)
$img.Dispose()

$out.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
$out.Dispose()
Write-Output "Chroma-keyed $Path"
