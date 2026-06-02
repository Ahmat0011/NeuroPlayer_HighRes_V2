param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$TargetDir
)

$ErrorActionPreference = "Stop"

Write-Output "--------------------------------------------------"
Write-Output "Starting binary signing script..."
Write-Output "Target Directory: $TargetDir"
Write-Output "--------------------------------------------------"

if (-not (Test-Path -Path $TargetDir -PathType Container)) {
    Write-Error "Target directory '$TargetDir' does not exist."
    exit 1
}

# 1. Find the certificate
Write-Output "Searching for certificate with subject 'CN=NeuroPlayer Dev Cert'..."
$cert = Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like '*CN=NeuroPlayer Dev Cert*' } |
        Select-Object -First 1

if (-not $cert) {
    Write-Error "Certificate with subject 'CN=NeuroPlayer Dev Cert' not found."
    exit 1
}

Write-Output "Found certificate: $($cert.Subject) ($($cert.Thumbprint))"

# 2. Get all executable and DLL files in TargetDir
Write-Output "Finding all executable (*.exe) and DLL (*.dll) files in target directory..."
$filesToSign = Get-ChildItem -Path $TargetDir -Recurse -File | Where-Object { $_.Extension -eq ".exe" -or $_.Extension -eq ".dll" }

if (-not $filesToSign -or $filesToSign.Count -eq 0) {
    Write-Output "No executable or DLL files found in target directory to sign."
} else {
    Write-Output "Found $($filesToSign.Count) file(s) to sign in target directory."
}

# 3. Sign files in TargetDir
foreach ($file in $filesToSign) {
    Write-Output "Signing target file: $($file.FullName)"
    $status = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert
    Write-Output "Status: $($status.StatusMessage)"
}

# 4. Copy and sign in TuneUp directory if it exists
$tuneUpDir = "D:\sahma\Documents\TuneUp\net10.0-windows"
if (Test-Path -Path $tuneUpDir -PathType Container) {
    Write-Output "--------------------------------------------------"
    Write-Output "TuneUp directory exists: $tuneUpDir"
    Write-Output "Copying and signing files in TuneUp directory..."
    Write-Output "--------------------------------------------------"
    
    foreach ($file in $filesToSign) {
        # Determine relative path from TargetDir to preserve folder structure (like runtimes/)
        $normalizedTargetDir = (Resolve-Path $TargetDir).Path.TrimEnd('\')
        $normalizedFilePath = $file.FullName
        
        if ($normalizedFilePath.StartsWith($normalizedTargetDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relativePath = $normalizedFilePath.Substring($normalizedTargetDir.Length).TrimStart('\')
        } else {
            $relativePath = $file.Name
        }
        
        $destPath = Join-Path -Path $tuneUpDir -ChildPath $relativePath
        $destDir = Split-Path -Path $destPath -Parent
        
        if (-not (Test-Path -Path $destDir -PathType Container)) {
            New-Item -Path $destDir -ItemType Directory -Force | Out-Null
        }
        
        Write-Output "Copying to: $destPath"
        Copy-Item -Path $file.FullName -Destination $destPath -Force
        
        Write-Output "Signing copied file: $destPath"
        $status = Set-AuthenticodeSignature -FilePath $destPath -Certificate $cert
        Write-Output "Status: $($status.StatusMessage)"
    }
} else {
    Write-Output "TuneUp directory '$tuneUpDir' does not exist. Skipping copy/sign."
}

Write-Output "--------------------------------------------------"
Write-Output "Binary signing script completed successfully."
Write-Output "--------------------------------------------------"
