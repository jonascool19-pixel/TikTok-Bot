#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$WebView2Sdk,
    [string]$Room,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or
    [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Starte den Prototyp mit Windows PowerShell 5.1 und -STA.'
}

function Get-LiveUri([string]$InputValue) {
    $value = $InputValue.Trim()
    if ($value -match '\A@([a-zA-Z0-9_.]{1,64})\z') {
        return [uri]('https://www.tiktok.com/@' + $Matches[1] + '/live')
    }

    $uri = $null
    if ([uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -and
        $uri.Scheme -eq 'https' -and $uri.IsDefaultPort -and !$uri.UserInfo -and
        $uri.Host -in @('www.tiktok.com', 'tiktok.com') -and
        $uri.AbsolutePath -match '\A/@([a-zA-Z0-9_.]{1,64})/live/?\z') {
        return [uri]('https://www.tiktok.com/@' + $Matches[1] + '/live')
    }
    return $null
}

if ($SmokeTest) {
    $expected = 'https://www.tiktok.com/@creator.name/live'
    foreach ($inputValue in @(' @creator.name ', 'https://tiktok.com/@creator.name/live/?share=123#chat')) {
        if ((Get-LiveUri $inputValue).AbsoluteUri -cne $expected) { throw "Unerwartete LIVE-Adresse: $inputValue" }
    }
    foreach ($inputValue in @('', 'http://www.tiktok.com/@a/live', 'https://www.tiktok.com.evil.test/@a/live',
        'https://me@www.tiktok.com/@a/live', 'https://www.tiktok.com:444/@a/live', 'https://www.tiktok.com/@a')) {
        if (Get-LiveUri $inputValue) { throw "Ungültige LIVE-Adresse akzeptiert: $inputValue" }
    }
}

$sdk = (Resolve-Path -LiteralPath $WebView2Sdk).Path
$architecture = switch ($env:PROCESSOR_ARCHITECTURE) {
    AMD64 { 'x64' }
    ARM64 { 'arm64' }
    x86 { 'x86' }
    default { throw 'Für diese Prozessorarchitektur ist kein WebView2-Loader eingerichtet.' }
}
$loader = Join-Path $sdk "runtimes\win-$architecture\native"
$env:PATH = "$loader;$env:PATH"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.Core.dll')
Add-Type -Path (Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.WinForms.dll')
[Windows.Forms.Application]::EnableVisualStyles()

$dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TikTok-Bot\WindowsPrototype'
if ($SmokeTest) { $dataRoot = Join-Path ([IO.Path]::GetTempPath()) ('TikTokBot-Smoke-' + [guid]::NewGuid()) }
$profilePath = Join-Path $dataRoot 'BotProfile'
$logPath = Join-Path $dataRoot 'connection.log'
[IO.Directory]::CreateDirectory($dataRoot) | Out-Null

function Write-ConnectionError([string]$Message) {
    try {
        [IO.File]::AppendAllText($logPath, ('{0:O} {1}{2}' -f [DateTime]::UtcNow, $Message, [Environment]::NewLine))
    } catch {
        Write-Warning 'Das Fehlerprotokoll konnte nicht gespeichert werden.'
    }
}

$form = New-Object Windows.Forms.Form
$form.Text = 'TikTok-Bot · Windows-Prototyp'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object Drawing.Size(1220, 850)
$form.MinimumSize = New-Object Drawing.Size(790, 510)
$form.Font = New-Object Drawing.Font('Segoe UI', 10)

$layout = New-Object Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'
$layout.ColumnCount = 1
$layout.RowCount = 3
$layout.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle('Percent', 100))) | Out-Null
$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle('AutoSize'))) | Out-Null
$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle('Percent', 100))) | Out-Null
$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle('AutoSize'))) | Out-Null
$form.Controls.Add($layout)

$toolbar = New-Object Windows.Forms.FlowLayoutPanel
$toolbar.AutoSize = $true
$toolbar.WrapContents = $false
$toolbar.Dock = 'Fill'
$toolbar.Padding = New-Object Windows.Forms.Padding(9, 6, 9, 6)
$layout.Controls.Add($toolbar, 0, 0)

$roomLabel = New-Object Windows.Forms.Label
$roomLabel.Text = '@Name oder LIVE-Link'
$roomLabel.AutoSize = $true
$roomLabel.Margin = New-Object Windows.Forms.Padding(3, 9, 7, 0)
$toolbar.Controls.Add($roomLabel)

$roomInput = New-Object Windows.Forms.TextBox
$roomInput.AccessibleName = 'TikTok @Name oder LIVE-Link'
$roomInput.Width = 275
$roomInput.MaxLength = 2048
$roomInput.Margin = New-Object Windows.Forms.Padding(3, 6, 3, 0)
$roomInput.Text = $Room
$toolbar.Controls.Add($roomInput)

$openButton = New-Object Windows.Forms.Button
$openButton.Text = 'LIVE öffnen'
$openButton.AutoSize = $true
$openButton.Enabled = $false
$toolbar.Controls.Add($openButton)
$form.AcceptButton = $openButton

$loginButton = New-Object Windows.Forms.Button
$loginButton.Text = 'Bot-Konto anmelden'
$loginButton.AutoSize = $true
$loginButton.Enabled = $false
$toolbar.Controls.Add($loginButton)

$view = New-Object Microsoft.Web.WebView2.WinForms.WebView2
$view.Dock = 'Fill'
$view.CreationProperties = New-Object Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties
$view.CreationProperties.UserDataFolder = $profilePath
$layout.Controls.Add($view, 0, 1)

$status = New-Object Windows.Forms.Label
$status.Dock = 'Fill'
$status.AutoSize = $true
$status.Padding = New-Object Windows.Forms.Padding(12, 6, 12, 8)
$status.Text = 'TikTok-Ansicht wird gestartet …'
$layout.Controls.Add($status, 0, 2)

$openButton.add_Click({
    $target = Get-LiveUri $roomInput.Text
    if (!$target) {
        $status.Text = 'Gib einen @Namen oder einen vollständigen TikTok-LIVE-Link ein.'
        $roomInput.Focus() | Out-Null
        return
    }
    $view.CoreWebView2.Navigate($target.AbsoluteUri)
})
$loginButton.add_Click({ $view.CoreWebView2.Navigate('https://www.tiktok.com/login') })

$script:smokePassed = $false
$timer = New-Object Windows.Forms.Timer
$timer.Interval = 30000
$timer.add_Tick({
    $timer.Stop()
    $form.Close()
})

$view.add_CoreWebView2InitializationCompleted({
    param($sender, $eventArgs)
    if (!$eventArgs.IsSuccess) {
        $status.Text = 'WebView2 konnte nicht gestartet werden. Prüfe die installierte WebView2 Runtime.'
        Write-ConnectionError ('WebView2-Start fehlgeschlagen: ' + $eventArgs.InitializationException.GetType().Name)
        if ($SmokeTest) { $form.Close() }
        return
    }

    $core = $sender.CoreWebView2
    $core.Settings.IsWebMessageEnabled = $false
    $core.Settings.AreHostObjectsAllowed = $false
    $core.add_NavigationStarting({
        param($browser, $navigation)
        $status.Text = 'Seite wird geladen …'
    })
    $core.add_NavigationCompleted({
        param($browser, $navigation)
        if ($SmokeTest) {
            $script:smokePassed = $navigation.IsSuccess -and $browser.DocumentTitle -eq 'TikTok-Bot Smoke' -and
                [IO.Directory]::Exists($profilePath)
            $form.Close()
            return
        }

        if (!$navigation.IsSuccess -or $navigation.HttpStatusCode -ge 400) {
            $status.Text = 'Seite konnte nicht geladen werden.'
            Write-ConnectionError ('Navigation fehlgeschlagen: {0}, HTTP {1}' -f $navigation.WebErrorStatus, $navigation.HttpStatusCode)
        } else {
            $status.Text = 'Seite geladen. Anmeldung und Chat stehen in der TikTok-Ansicht.'
        }
    })
    $core.add_ProcessFailed({
        param($browser, $failure)
        $status.Text = 'Die Browseransicht ist ausgefallen. Starte den Prototyp neu.'
        Write-ConnectionError ('WebView2-Prozessfehler: ' + $failure.ProcessFailedKind)
    })
    $openButton.Enabled = $true
    $loginButton.Enabled = $true
    $status.Text = 'Öffne deinen LIVE oder melde das Bot-Konto an.'
    if ($SmokeTest) {
        $core.NavigateToString('<!doctype html><title>TikTok-Bot Smoke</title><p>WebView2 bereit</p>')
    } elseif ($Room) {
        $openButton.PerformClick()
    }
})

$form.add_Shown({
    if ($SmokeTest) { $timer.Start() }
    $view.EnsureCoreWebView2Async() | Out-Null
})
try {
    [Windows.Forms.Application]::Run($form)
} finally {
    $timer.Dispose()
    $form.Dispose()
}
if ($SmokeTest) {
    if (!$script:smokePassed) { throw 'Der WebView2-Smoke-Test ist fehlgeschlagen.' }
    Write-Output 'LIVE-Adressprüfung, WebView2-Navigation und separates Profil erfolgreich.'
}
