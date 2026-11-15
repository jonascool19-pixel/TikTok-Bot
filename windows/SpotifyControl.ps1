param([ValidateSet('Status','Toggle','Next','Previous')][string]$Action = 'Status', [ValidateSet('Spotify','AppleMusic','Deezer','AmazonMusic')][string]$Provider = 'Spotify')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation' + [char]96 + '1'
} | Select-Object -First 1
function Await-Operation($operation, [type]$resultType) {
    $asTask.MakeGenericMethod($resultType).Invoke($null, @($operation)).GetAwaiter().GetResult()
}
try {
    $manager = Await-Operation ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
    $pattern = switch ($Provider) {
        Spotify { '(?i)^(?:SpotifyAB\.SpotifyMusic_[a-z0-9]+!Spotify|Spotify(?:\.exe)?|Spotify\.Spotify)$|[\\/]Spotify\.exe$' }
        AppleMusic { '(?i)^(?:AppleInc\.AppleMusicWin_[a-z0-9]+![a-z0-9.]+|AppleMusic(?:\.exe)?)$|[\\/]AppleMusic\.exe$' }
        Deezer { '(?i)^(?:Deezer(?:\.exe)?|Deezer\.[a-z0-9_.]+(?:![a-z0-9.]+)?|com\.squirrel\.deezer\.Deezer)$|[\\/]Deezer\.exe$' }
        AmazonMusic { '(?i)^(?:AmazonMusic(?:\.exe)?|Amazon(?:MobileLLC|\.com)\.AmazonMusic_[a-z0-9]+![a-z0-9.]+)$|[\\/]AmazonMusic\.exe$' }
    }
    $session = $manager.GetSessions() | Where-Object {
        $_.SourceAppUserModelId -match $pattern
    } | Select-Object -First 1
    if (!$session) { exit 2 }
    $accepted = switch ($Action) {
        Toggle { Await-Operation ($session.TryTogglePlayPauseAsync()) ([bool]) }
        Next { Await-Operation ($session.TrySkipNextAsync()) ([bool]) }
        Previous { Await-Operation ($session.TrySkipPreviousAsync()) ([bool]) }
        Status { $true }
    }
    if (!$accepted) { exit 3 }
} catch { exit 4 }
