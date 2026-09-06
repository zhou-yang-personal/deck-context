param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [string]$TestFixtureDirectory
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-VerifiedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,

        [Parameter(Mandatory = $true)]
        [string]$Sha256
    )

    $parent = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Invoke-WebRequest -Uri $Uri -OutFile $DestinationPath
    $actualHash = (Get-FileHash -Path $DestinationPath -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($actualHash -ne $Sha256) {
        throw "SHA-256 mismatch for '$Uri'. Expected '$Sha256', got '$actualHash'."
    }
}

$modelCommit = "87416418657359cb625c412a48b6e1d6d41c29bd"
$modelBaseUri = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$modelCommit"
$models = @(
    @{ Name = "chi_sim.traineddata"; Sha256 = "a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730" },
    @{ Name = "eng.traineddata"; Sha256 = "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2" },
    @{ Name = "spa.traineddata"; Sha256 = "6f2e04d02774a18f01bed44b1111f2cd7f3ba7ac9dc4373cd3f898a40ea6b464" }
)

foreach ($model in $models) {
    Get-VerifiedFile `
        -Uri "$modelBaseUri/$($model.Name)" `
        -DestinationPath (Join-Path $DestinationDirectory $model.Name) `
        -Sha256 $model.Sha256
}

$ocrRoot = Split-Path -Parent $DestinationDirectory
$licenseDirectory = Join-Path $ocrRoot "licenses"
Get-VerifiedFile `
    -Uri "https://raw.githubusercontent.com/tesseract-ocr/tesseract/fb87a84b6e3384b424b2169c76de9031046861e9/LICENSE" `
    -DestinationPath (Join-Path $licenseDirectory "Tesseract-Apache-2.0.txt") `
    -Sha256 "cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30"
Get-VerifiedFile `
    -Uri "https://raw.githubusercontent.com/DanBloomberg/leptonica/1.85.0/leptonica-license.txt" `
    -DestinationPath (Join-Path $licenseDirectory "Leptonica-License.txt") `
    -Sha256 "87829abb5bbb00b55a107365da89e9a33f86c4250169e5a1e5588505be7d5806"
Get-VerifiedFile `
    -Uri "https://raw.githubusercontent.com/Sicos1977/TesseractOCR/787ebdb488184f47df3dcad7fe687b0b95d0d98c/README.md" `
    -DestinationPath (Join-Path $licenseDirectory "TesseractOCR-README.md") `
    -Sha256 "d0d328d89653f858029d42ed5da3962dc376ec5e975c3b2c2d4f9737271968cf"

if (-not [string]::IsNullOrWhiteSpace($TestFixtureDirectory)) {
    Get-VerifiedFile `
        -Uri "https://raw.githubusercontent.com/charlesw/tesseract-samples/ef1d3bcae017b5bceb4fd3bfd6f0a0d9d5e75d93/src/Tesseract.ConsoleDemo/phototest.tif" `
        -DestinationPath (Join-Path $TestFixtureDirectory "phototest.tif") `
        -Sha256 "d2241a1eb6d6cd2eb6544b8e228c2862c852b7467d16ed636caa32179b780692"
}
