Imports System.IO
Imports System.Net
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions

Public Class WurmShare_Overlay
    Public _partyGrid As PartyGridPanel
    Private Const CooldownTickMs As Integer = 50
    Private WithEvents _cooldownTimer As New System.Windows.Forms.Timer() With {.Interval = CooldownTickMs}

    '########## LOG-PARSING LIBS / VARS ##########
    Dim currentdate As Date = DateAndTime.Now
    Dim EventLogfile As String
    Private _EventLastOffset As Long
    Dim CombatLogfile As String
    Private _CombatLastOffset As Long

    '########## GUI/MISC VARS ##########
    Dim Go As Boolean
    Dim LeftSet As Boolean
    Dim TopSet As Boolean
    Dim HoldLeft As Integer
    Dim HoldTop As Integer
    Dim OffLeft As Integer
    Dim OffTop As Integer
    Dim HoldWidth As Integer
    Dim HoldHeight As Integer

    Dim API_Client_Version As String = "1.0.2"
    Dim API_Available_Version As String
    Dim API_Update_Required As Boolean = False

    Dim PassesWithoutCombat As Integer = 0

    Dim NetworkClient As New SecureChannelClient("https://wurmmaps.xyz/WurmShare")

    ' ── Self-state (this client's own PartyMemberData) ────────────────────────
    ' _selfData is the source of truth for what we broadcast over the channel.
    ' It is NOT mirrored in the party grid: the player's own bars are already
    ' rendered by the game itself, and showing them again in the overlay would
    ' waste screen space. The party grid only contains REMOTE party members.
    Private _selfData As PartyMemberData
    Dim SelfSubPanelDisplay As PartySubPanel

    ' Re-entrancy guard for NetworkTimer_Tick (Async Sub can be re-entered
    ' before its previous invocation has finished awaiting).
    Private _networkTickBusy As Boolean = False

    ' ── Self bar-capture bindings ─────────────────────────────────────────────
    ' Each bar that we screen-scrape from the game gets its own bound PictureBox
    ' overlay. Health and Stamina deliberately share a PictureBox: in the game's
    ' UI they occupy a single compound bar.
    '
    ' We deliberately do NOT store a fill colour per bar: the analyser detects
    ' "filled" by channel spread, which works for any saturated fill colour
    ' against the standard dark-neutral background. Same algorithm, same
    ' settings, every bar.
    '
    ' The four sub-PictureBoxes themselves live inside a CaptureSurfacePanel
    ' (created in InitPartySystem) which enforces their pixel-exact layout
    ' and the height-locked / width-resizable container behaviour.
    Private _captureSurface As CaptureSurfacePanel
    Private _hpStamCaptureBox As PictureBox
    Private _waterCaptureBox As PictureBox
    Private _foodCaptureBox As PictureBox
    Private _favorCaptureBox As PictureBox

    ''' <summary>
    ''' Minimum max(R,G,B) - min(R,G,B) for a pixel to count as "filled" rather
    ''' than background. Tune in one place if a UI theme has unusually muted
    ''' fills or unusually tinted backgrounds.
    ''' </summary>
    Private Const CaptureChannelSpread As Integer = 22

    ''' <summary>
    ''' Minimum brightness (max R/G/B channel) for a pixel to count as a bar
    ''' fill, applied alongside CaptureChannelSpread. Rejects dim background
    ''' pixels that happen to carry a small colour tint - their spread might
    ''' clear the (lowered) spread threshold, but their low brightness does
    ''' not clear this floor. Tuned so the dimmest observed food-bar fill
    ''' (~max 69) passes while the brightest observed background (~max 83 but
    ''' low spread) is rejected on spread.
    ''' </summary>
    Private Const CaptureAbsoluteFloor As Integer = 65

    '############################## DLL Imports ##############################
    <DllImport("kernel32")>
    Private Shared Function GetPrivateProfileString(ByVal section As String, ByVal key As String, ByVal def As String, ByVal retVal As StringBuilder, ByVal size As Integer, ByVal filePath As String) As Integer
    End Function

    <DllImport("kernel32")>
    Private Shared Function WritePrivateProfileString(ByVal lpSectionName As String, ByVal lpKeyName As String, ByVal lpString As String, ByVal lpFileName As String) As Long
    End Function

#Region "Form Initialization Functions"
    Public Sub WurmShare_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Me.StartPosition = FormStartPosition.Manual
        Me.Bounds = SystemInformation.VirtualScreen
        MasterPanelTitle.Text = "WurmShare - v" & API_Client_Version
        Randomize()
        InitPartySystem()
        ' Wire the four self-bar capture overlays. They are READY once bound;
        ' actual sampling happens on each LogScanTick.Tick (which the user
        ' starts from the log-scan button).
        '
        ' The analyser uses channel-spread detection, so no fill colour needs
        ' to be specified - the same algorithm handles every coloured fill
        ' against the standard neutral game background.
        '
        ' The four sub-PictureBoxes live inside _captureSurface (created in
        ' SetupSelfCaptureSurface). Health and Stamina deliberately share
        ' the same physical sub-box because in the game UI they sit on a
        ' single compound bar.
        BindHealthStaminaCapture(_captureSurface.HealthStaminaBox)
        BindFavorCapture(_captureSurface.FavorBox)
        BindWaterCapture(_captureSurface.WaterBox)
        BindFoodCapture(_captureSurface.FoodBox)
        SetDefaultPanelPositions()
        LoadAllUserSettings()
    End Sub

    Public Sub WurmShare_Close(sender As Object, e As EventArgs) Handles MyBase.Closed
        Application.Exit()
        End
    End Sub

    Public Sub SetDefaultPanelPositions()
        Dim primary As Rectangle = Screen.PrimaryScreen.Bounds
        Dim screenCenter As New Point(primary.X + primary.Width \ 2, primary.Y + primary.Height \ 2)
        Dim parentCenter As Point = MasterPanel.Parent.PointToClient(screenCenter)
        MasterPanel.Location = New Point(parentCenter.X - MasterPanel.Width \ 2, parentCenter.Y - MasterPanel.Height \ 2)
        parentCenter = SettingsPanel.Parent.PointToClient(screenCenter)
        SettingsPanel.Location = New Point(parentCenter.X - SettingsPanel.Width \ 2, parentCenter.Y - SettingsPanel.Height \ 2)
        parentCenter = DebugPanel.Parent.PointToClient(screenCenter)
        DebugPanel.Location = New Point(parentCenter.X - DebugPanel.Width \ 2, parentCenter.Y - DebugPanel.Height \ 2)
        parentCenter = QuickStartGuidePanel.Parent.PointToClient(screenCenter)
        QuickStartGuidePanel.Location = New Point(parentCenter.X - QuickStartGuidePanel.Width \ 2, parentCenter.Y - QuickStartGuidePanel.Height \ 2)
        parentCenter = UpdateNotifPanel.Parent.PointToClient(screenCenter)
        UpdateNotifPanel.Location = New Point(parentCenter.X - UpdateNotifPanel.Width \ 2, parentCenter.Y - UpdateNotifPanel.Height \ 2)
        parentCenter = _captureSurface.Parent.PointToClient(screenCenter)
        _captureSurface.Location = New Point((parentCenter.X - _captureSurface.Width \ 2) - 400, parentCenter.Y - _captureSurface.Height \ 2)
    End Sub

    Private Sub InitPartySystem()

        _partyGrid = New PartyGridPanel()
        _partyGrid.Columns = 1
        _partyGrid.Dock = DockStyle.Fill

        pnlPartyHost.Controls.Add(_partyGrid)

        _cooldownTimer.Start()

        SetupSelf()
        SetupSelfCaptureSurface()
        'SetupDemoMembers()
        SelfSubPanelDisplay = AddPartyMember("Self")
        SelfSubPanelDisplay.SetData(_selfData)
    End Sub

    ''' <summary>
    ''' Create the CaptureSurfacePanel that hosts the four self-capture overlays.
    ''' Added directly to the form at a default location; the user can move it
    ''' or re-parent it programmatically (e.g. into MasterPanel) afterwards.
    '''
    ''' Drag-to-move via the existing Movable_MouseDown / Up / Move handlers is
    ''' wired up here too: those handlers walk via <c>sender.Parent</c>, so a
    ''' mousedown on any sub-PictureBox drags the whole CaptureSurfacePanel.
    ''' </summary>
    Private Sub SetupSelfCaptureSurface()
        _captureSurface = New CaptureSurfacePanel()
        _captureSurface.Location = New Point(10, 10)
        _captureSurface.Width = 200
        Me.Controls.Add(_captureSurface)
        _captureSurface.BringToFront()
        _captureSurface.Visible = False

        ' Wire each sub-PictureBox to the existing drag-to-move handlers so the
        ' container can be repositioned the same way the old standalone
        ' PictureBoxes were.
        Dim grippable As PictureBox() = New PictureBox() {
            _captureSurface.HealthStaminaBox,
            _captureSurface.FavorBox,
            _captureSurface.FoodBox,
            _captureSurface.WaterBox
        }
        For Each pb As PictureBox In grippable
            AddHandler pb.MouseDown, AddressOf Movable_MouseDown
            AddHandler pb.MouseUp, AddressOf Movable_MouseUp
            AddHandler pb.MouseMove, AddressOf Movable_MouseMove
        Next
    End Sub

    Private Sub SetupDemoMembers()
        'Dim alice As PartySubPanel = AddPartyMember("EpicPhail")
        'alice.UpdateHealthStamina(0.85, 0.6)
        'alice.UpdateSustenance(0.7, 0.5)
        'alice.SelectNumpadCell(5)
        'alice.UpdateCombatStance(1)
        'alice.UpdateShelterDirection(1)
        'alice.UpdateFavorVisibility(True)
        'alice.UpdateFavorPercent(0.68)
        'alice.UpdateCombatStatus(True)

        'Dim bob As PartySubPanel = AddPartyMember("Feilnas")
        'bob.UpdateHealthStamina(0.4, 0.4)
        'bob.UpdateSustenance(0.2, 0.8)
        'bob.ShowActionBar("Favor", 0.3)
        'bob.UpdateCombatStance(2)
        'bob.UpdateShelterDirection(2)
        'bob.UpdateFavorVisibility(True)
        'bob.UpdateFavorPercent(0.68)
        'bob.UpdateStunnedStatus(True)

        'Dim carol As PartySubPanel = AddPartyMember("Failnas")
        'carol.UpdateHealthStamina(1.0, 0.9)
        'carol.UpdateSustenance(0.95, 0.9)
        'carol.TriggerCooldown(20.0)
        'carol.UpdateCombatStance(3)
        'carol.UpdateShelterDirection(4)
        'carol.UpdateEncumberedStatus(True)

        Dim deadjim As PartySubPanel = AddPartyMember("Felinas")
        deadjim.UpdateHealthStamina(1.0, 0)
        deadjim.UpdateSustenance(0, 0)
        deadjim.UpdateFavorVisibility(True)
        deadjim.UpdateFavorPercent(0)
        deadjim.TriggerCooldown(20.0)
        deadjim.UpdateCombatStance(0)
        deadjim.UpdateShelterDirection(0)
    End Sub

    ''' <summary>
    ''' Create the local user's PartyMemberData instance. We deliberately do NOT
    ''' add a display panel for ourselves to the party grid: the game already
    ''' renders the player's own bars, so duplicating them in the overlay would
    ''' waste screen real estate. <c>_selfData</c> exists purely as the source
    ''' of truth for outgoing network broadcasts and as the sink for our own
    ''' screen-capture bar bindings.
    ''' </summary>
    Private Sub SetupSelf()
        _selfData = New PartyMemberData()
        _selfData.PlayerName = ResolveInitialSelfName()
    End Sub

    ''' <summary>
    ''' Pick an initial name for the self panel. Priority:
    '''   1. The CharacterNameTextBox text, if the designer pre-populated it.
    '''   2. The Windows user name.
    '''   3. The literal "Self".
    ''' </summary>
    Private Function ResolveInitialSelfName() As String
        Try
            If CharacterNameTextBox IsNot Nothing AndAlso
               Not String.IsNullOrWhiteSpace(CharacterNameTextBox.Text) Then
                Return CharacterNameTextBox.Text.Trim()
            End If
        Catch
            ' CharacterNameTextBox may not be initialised yet during very early load
        End Try
        Try
            If Not String.IsNullOrWhiteSpace(Environment.UserName) Then
                Return Environment.UserName
            End If
        Catch
        End Try
        Return "Self"
    End Function

#End Region

#Region "Configuration File Functions"
    Public Function GetIniValue(section As String, key As String, filename As String, Optional defaultValue As String = "") As String
        Dim sb As New StringBuilder(500)
        If GetPrivateProfileString(section, key, defaultValue, sb, sb.Capacity, filename) > 0 Then
            Return sb.ToString
        Else
            Return defaultValue
        End If
    End Function

    Private Function SetIniValue(section As String, key As String, filename As String, Optional defaultValue As String = "") As String
        Dim sb As New StringBuilder(500)
        Try
            WritePrivateProfileString(section, key, defaultValue, filename)
            Return True
            Exit Try
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub LoadAllUserSettings()
        If File.Exists(My.Application.Info.DirectoryPath & "\UserSettings.ini") Then
            Try
                Dim US_server As String = ""
                Dim US_password As String = ""
                Dim US_gamever As String = ""
                Dim US_gamepath As String = ""
                Dim US_character As String = ""
                Dim US_favor As String = ""
                Dim US_columns As String = ""

                Dim PL_Main_X As Integer = 0
                Dim PL_Main_Y As Integer = 0
                Dim PS_Main_X As Integer = 0
                Dim PS_Main_Y As Integer = 0

                Dim PL_Settings_X As Integer = 0
                Dim PL_Settings_Y As Integer = 0

                Dim PL_Debug_X As Integer = 0
                Dim PL_Debug_Y As Integer = 0

                Dim PL_QuickStart_X As Integer = 0
                Dim PL_QuickStart_Y As Integer = 0

                Dim PL_CaptureSurface_X As Integer = 0
                Dim PL_CaptureSurface_Y As Integer = 0
                Dim PS_CaptureSurface_X As Integer = 0
                Dim PS_CaptureSurface_Y As Integer = 0

                US_server = GetIniValue("Application", "Server", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                US_password = GetIniValue("Application", "Password", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                US_gamever = GetIniValue("Application", "GameVer", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                US_gamepath = GetIniValue("Application", "GamePath", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                US_character = GetIniValue("Application", "Character", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                US_favor = GetIniValue("Application", "Favor", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                US_columns = GetIniValue("Application", "DisplayColumns", My.Application.Info.DirectoryPath & "\UserSettings.ini")

                PL_Main_X = GetIniValue("Application", "PL_Main_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_Main_Y = GetIniValue("Application", "PL_Main_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PS_Main_X = GetIniValue("Application", "PS_Main_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PS_Main_Y = GetIniValue("Application", "PS_Main_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_Settings_X = GetIniValue("Application", "PL_Settings_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_Settings_Y = GetIniValue("Application", "PL_Settings_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_Debug_X = GetIniValue("Application", "PL_Debug_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_Debug_Y = GetIniValue("Application", "PL_Debug_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_QuickStart_X = GetIniValue("Application", "PL_QuickStart_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_QuickStart_Y = GetIniValue("Application", "PL_QuickStart_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_CaptureSurface_X = GetIniValue("Application", "PL_CaptureSurface_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PL_CaptureSurface_Y = GetIniValue("Application", "PL_CaptureSurface_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PS_CaptureSurface_X = GetIniValue("Application", "PS_CaptureSurface_X", My.Application.Info.DirectoryPath & "\UserSettings.ini")
                PS_CaptureSurface_Y = GetIniValue("Application", "PS_CaptureSurface_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini")

                ServerAddressBox.Text = US_server
                txtPassword.Text = US_password
                If US_gamever = "Standalone" Then
                    RadioButton1.Checked = True
                    RadioButton2.Checked = False
                Else
                    RadioButton1.Checked = False
                    RadioButton2.Checked = True
                End If
                GamePathTextBox.Text = US_gamepath
                CharacterNameTextBox.Text = US_character
                If US_favor = "Enabled" Then
                    CheckBox1.Checked = True
                    _captureSurface.FavorVisible = True
                Else
                    CheckBox1.Checked = False
                    _captureSurface.FavorVisible = False
                End If
                NumericUpDown1.Value = CInt(US_columns)
                If PL_Main_X > 0 And PL_Main_Y > 0 Then
                    MasterPanel.Location = New Point(PL_Main_X, PL_Main_Y)
                End If
                If PS_Main_X > 0 And PS_Main_Y > 0 Then
                    MasterPanel.Size = New Size(PS_Main_X, PS_Main_Y)
                End If
                If PL_Settings_X > 0 And PL_Settings_Y > 0 Then
                    SettingsPanel.Location = New Point(PL_Settings_X, PL_Settings_Y)
                End If
                If PL_Debug_X > 0 And PL_Debug_Y > 0 Then
                    DebugPanel.Location = New Point(PL_Debug_X, PL_Debug_Y)
                End If
                If PL_QuickStart_X > 0 And PL_QuickStart_Y > 0 Then
                    QuickStartGuidePanel.Location = New Point(PL_QuickStart_X, PL_QuickStart_Y)
                End If
                If PL_CaptureSurface_X > 0 And PL_CaptureSurface_Y > 0 Then
                    _captureSurface.Location = New Point(PL_CaptureSurface_X, PL_CaptureSurface_Y)
                End If
                If PS_CaptureSurface_X > 0 And PS_CaptureSurface_Y > 0 Then
                    _captureSurface.Size = New Size(PS_CaptureSurface_X, PS_CaptureSurface_Y)
                End If
            Catch ex As Exception
                SaveAllUserSettings()
            End Try
        Else
            SaveAllUserSettings()
        End If
    End Sub

    Private Sub SaveAllUserSettings()
        Dim US_server As String = ServerAddressBox.Text
        Dim US_password As String = txtPassword.Text
        Dim US_gamever As String = ""
        If RadioButton1.Checked = True Then
            US_gamever = "Standalone"
        Else
            US_gamever = "Steam"
        End If
        Dim US_gamepath As String = GamePathTextBox.Text
        Dim US_character As String = CharacterNameTextBox.Text
        Dim US_favor As String = ""
        If CheckBox1.Checked = True Then
            US_favor = "Enabled"
        Else
            US_favor = "Disabled"
        End If
        Dim US_columns As String = CStr(NumericUpDown1.Value)

        Dim PL_Main_X As Integer = MasterPanel.Location.X
        Dim PL_Main_Y As Integer = MasterPanel.Location.Y
        Dim PS_Main_X As Integer = MasterPanel.Size.Width
        Dim PS_Main_Y As Integer = MasterPanel.Size.Height

        Dim PL_Settings_X As Integer = SettingsPanel.Location.X
        Dim PL_Settings_Y As Integer = SettingsPanel.Location.Y

        Dim PL_Debug_X As Integer = DebugPanel.Location.X
        Dim PL_Debug_Y As Integer = DebugPanel.Location.Y

        Dim PL_QuickStart_X As Integer = QuickStartGuidePanel.Location.X
        Dim PL_QuickStart_Y As Integer = QuickStartGuidePanel.Location.Y

        Dim PL_CaptureSurface_X As Integer = _captureSurface.Location.X
        Dim PL_CaptureSurface_Y As Integer = _captureSurface.Location.Y
        Dim PS_CaptureSurface_X As Integer = _captureSurface.Size.Width
        Dim PS_CaptureSurface_Y As Integer = _captureSurface.Size.Height

        SetIniValue("Application", "Server", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_server)
        SetIniValue("Application", "Password", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_password)
        SetIniValue("Application", "GameVer", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_gamever)
        SetIniValue("Application", "GamePath", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_gamepath)
        SetIniValue("Application", "Character", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_character)
        SetIniValue("Application", "Favor", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_favor)
        SetIniValue("Application", "DisplayColumns", My.Application.Info.DirectoryPath & "\UserSettings.ini", US_columns)

        SetIniValue("Application", "PL_Main_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_Main_X)
        SetIniValue("Application", "PL_Main_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_Main_Y)
        SetIniValue("Application", "PS_Main_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PS_Main_X)
        SetIniValue("Application", "PS_Main_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PS_Main_Y)
        SetIniValue("Application", "PL_Settings_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_Settings_X)
        SetIniValue("Application", "PL_Settings_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_Settings_Y)
        SetIniValue("Application", "PL_Debug_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_Debug_X)
        SetIniValue("Application", "PL_Debug_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_Debug_Y)
        SetIniValue("Application", "PL_QuickStart_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_QuickStart_X)
        SetIniValue("Application", "PL_QuickStart_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_QuickStart_Y)

        SetIniValue("Application", "PL_CaptureSurface_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_CaptureSurface_X)
        SetIniValue("Application", "PL_CaptureSurface_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PL_CaptureSurface_Y)
        SetIniValue("Application", "PS_CaptureSurface_X", My.Application.Info.DirectoryPath & "\UserSettings.ini", PS_CaptureSurface_X)
        SetIniValue("Application", "PS_CaptureSurface_Y", My.Application.Info.DirectoryPath & "\UserSettings.ini", PS_CaptureSurface_Y)
    End Sub
#End Region

#Region "Update Functions"
    Private Sub CheckForUpdates(server_url As String)
        Dim UpdateCheckCurrent As String = API_Request("ver", server_url).Replace("""", "").Trim()
        If String.IsNullOrEmpty(UpdateCheckCurrent) Then
            ' Couldn't reach the version endpoint - skip the check this run
            ' rather than fall through to the "update available" UI with no
            ' actual version string to show.
            Return
        End If
        If UpdateCheckCurrent = API_Client_Version Then
            'Do nothing, we are up to date.
        Else
            'Current version and running version do not match.
            API_Available_Version = UpdateCheckCurrent
            'We need to now get the minimum supported client version from the server, and check if the running version is greater than that.
            Dim forceupdflag As Boolean = False
            Dim UpdateCheckMinimum As String = API_Request("minver", server_url).Replace("""", "").Trim()

            ' Defensive parse: server might return junk, an empty body, or a
            ' version with fewer than 3 segments (or extras like "1.0.2-beta").
            ' Integer.TryParse leaves the target at 0 on failure, Length-guarded
            ' indexing handles short arrays, so neither FormatException nor
            ' IndexOutOfRangeException can reach the caller. The previous
            ' CInt(versubs(N)) on raw split parts was the unhandled exception
            ' reported by end users on Host / Join.
            Dim minMajor As Integer = 0, minMinor As Integer = 0, minPatch As Integer = 0
            Dim selfMajor As Integer = 0, selfMinor As Integer = 0, selfPatch As Integer = 0
            Dim minParts() As String = UpdateCheckMinimum.Split("."c)
            Dim selfParts() As String = API_Client_Version.Split("."c)
            If minParts.Length > 0 Then Integer.TryParse(minParts(0), minMajor)
            If minParts.Length > 1 Then Integer.TryParse(minParts(1), minMinor)
            If minParts.Length > 2 Then Integer.TryParse(minParts(2), minPatch)
            If selfParts.Length > 0 Then Integer.TryParse(selfParts(0), selfMajor)
            If selfParts.Length > 1 Then Integer.TryParse(selfParts(1), selfMinor)
            If selfParts.Length > 2 Then Integer.TryParse(selfParts(2), selfPatch)

            If minMajor > selfMajor Then
                forceupdflag = True
            End If
            If minMinor > selfMinor And minMajor >= selfMajor Then
                forceupdflag = True
            End If
            If minPatch > selfPatch And minMinor >= selfMinor Then
                forceupdflag = True
            End If
            If forceupdflag = True Then
                RequireUpdate()
            Else
                'Running version is above minimum server requirements, but is still not the latest.
                UpdateNotifPanel.Location = New Point((Me.Size.Width / 2) - (UpdateNotifPanel.Size.Width / 2), (Me.Size.Height / 2) - (UpdateNotifPanel.Size.Height / 2))
                UpdateButton1.Visible = True
                UpdateButton2.Visible = True
                UpdateNotifText.Text = "An update (Version " & API_Available_Version & ") for WurmShare is available, but not required. Download?"
                UpdateNotifPanel.Visible = True
            End If
        End If
    End Sub

    Private Sub RequireUpdate()
        API_Update_Required = True
        UpdateNotifSubPanel.BackColor = Color.FromArgb(48, 16, 16)
        UpdateNotifPanel.Location = New Point((Me.Size.Width / 2) - (UpdateNotifPanel.Size.Width / 2), (Me.Size.Height / 2) - (UpdateNotifPanel.Size.Height / 2))
        UpdateButton1.Visible = True
        UpdateButton2.Visible = False
        UpdateNotifText.Text = "An update (Version " & API_Available_Version & ") for WurmShare is required. Download?"
        UpdateNotifPanel.Visible = True
    End Sub

    Public Function GetURLDataBin(ByVal URL As String, Optional ByRef UserName As String = "", Optional ByRef Password As String = "") As Byte()
        Dim Req As HttpWebRequest
        Dim SourceStream As System.IO.Stream
        Dim Response As HttpWebResponse
        Try
            Req = HttpWebRequest.Create(URL)
            Response = Req.GetResponse()
            SourceStream = Response.GetResponseStream()
            Dim Buffer(4096) As Byte, BlockSize As Integer
            Dim TempStream As New MemoryStream
            Do
                BlockSize = SourceStream.Read(Buffer, 0, 4096)
                If BlockSize > 0 Then TempStream.Write(Buffer, 0, BlockSize)
            Loop While BlockSize > 0
            Return TempStream.ToArray()
        Catch ex As Exception
            AddEvent("[net] Failed to download binary: " & ex.Message)
        Finally
            SourceStream.Close()
            Response.Close()
        End Try
    End Function

    Private Sub UpdateButton1_Click(sender As Object, e As EventArgs) Handles UpdateButton1.Click
        'Simple self-update subroutine. First we'll get the binary contents from github, and save it to a temporary text file.
        'If the download isnt interrupted and succeeds, we will need to close the current instance of the program and launch the new one after renaming it to an exe.
        'The easiest way to do this is with a simple batch script, which is created via the streamwriter.
        'In batch, there's no simple command to wait a period of time without requiring the user to press a key...
        'To get around this we can use "Ping localhost -n X >NUL" where X is the number of seconds we want to wait, +1.
        'This is necessary to make sure the download and disk operations are complete before proceeding to the next command in the batch file.
        'May not be adequate on slower systems. Testing required.
        If API_Available_Version = "" Or API_Available_Version = Nothing Then
            AddEvent("[net] Failed to get newest version number from server.")
        Else
            My.Computer.FileSystem.WriteAllBytes(My.Application.Info.DirectoryPath & "\WurmShare.temp", GetURLDataBin("https://github.com/Jason-Bloomer/WurmShare/releases/download/v" & API_Available_Version & "/WurmShare.exe"), False)
            Dim My_Process As New Process()
            Dim My_Process_Info As New ProcessStartInfo()
            Dim strPath As String = My.Application.Info.DirectoryPath & "\WurmShare-update.bat"
            Dim swDestruct As StreamWriter = New StreamWriter(strPath)
            swDestruct.WriteLine("PING localhost -n 3 >NUL && taskkill /F /IM ""WurmShare.exe"" && PING localhost -n 6 >NUL && del """ & My.Application.Info.DirectoryPath & "\WurmShare.exe"" && ren """ & My.Application.Info.DirectoryPath & "\WurmShare.temp"" """ & "WurmShare.exe"" && del """ & strPath & """ && PING localhost -n 3 >NUL  && """ & My.Application.Info.DirectoryPath & "\WurmShare.exe""")
            swDestruct.Close()
            My_Process_Info.FileName = strPath
            My_Process_Info.CreateNoWindow = True
            My_Process_Info.UseShellExecute = False
            My_Process.EnableRaisingEvents = False
            My_Process.StartInfo = My_Process_Info
            My_Process.Start()
        End If
    End Sub

    Private Sub UpdateButton2_Click(sender As Object, e As EventArgs) Handles UpdateButton2.Click
        Dim manualdownload As Process = Process.Start("https://github.com/Jason-Bloomer/WurmShare/releases/")
    End Sub

    '############################## - Version API Request Constructor - ##############################
    Private Function API_Request(ByVal reqtype As String, ByVal addr As String) As String
        Try
            Dim requestUrl As String = Nothing
            If reqtype = "ver" Then
                requestUrl = addr & "/version.php/version?"
            End If
            If reqtype = "minver" Then
                requestUrl = addr & "/version.php/minversion?"
            End If
            Dim request As WebRequest = WebRequest.Create(requestUrl)
            Dim response As WebResponse = request.GetResponse()
            Dim dataStream As Stream = response.GetResponseStream()
            Dim reader As StreamReader = New StreamReader(dataStream)
            Dim TempResponse As String = reader.ReadToEnd()
            response.Close()
            Return TempResponse
        Catch ex As Exception
            AddEvent("[net] Error when making API request: " & ex.Message)
            Return ""
        End Try
    End Function
#End Region

#Region "Party Member Management"

    ''' <summary>Add a new party member and return their sub-panel.</summary>
    Public Function AddPartyMember(name As String) As PartySubPanel
        Return _partyGrid.AddMember(name)
    End Function

    ''' <summary>Remove a party member by index.</summary>
    Public Sub RemovePartyMember(index As Integer)
        _partyGrid.RemoveMemberAt(index)
    End Sub

    ''' <summary>Remove all party members.</summary>
    Public Sub ClearParty()
        _partyGrid.ClearMembers()
    End Sub

    ''' <summary>Get a party member's sub-panel by index.</summary>
    Public Function GetMemberPanel(index As Integer) As PartySubPanel
        Return _partyGrid.GetPanel(index)
    End Function

    ''' <summary>Total number of current party members.</summary>
    Public ReadOnly Property PartySize As Integer
        Get
            Return _partyGrid.MemberCount
        End Get
    End Property

#End Region

#Region "PictureBox UI Control Click&Drag Functions"
    Private Sub Movable_MouseUp(ByVal sender As Object, ByVal e As MouseEventArgs) Handles MainButton.MouseUp, MasterPanelResizeGrabber.MouseUp, Button3.MouseUp, Button4.MouseUp, Button5.MouseUp, Button8.MouseUp
        Go = False
        LeftSet = False
        TopSet = False
    End Sub

    Private Sub Movable_MouseDown(ByVal sender As Object, ByVal e As MouseEventArgs) Handles MainButton.MouseDown, MasterPanelResizeGrabber.MouseDown, Button3.MouseDown, Button4.MouseDown, Button5.MouseDown, Button8.MouseDown
        Go = True
    End Sub

    Private Sub Movable_MouseMove(ByVal sender As Object, ByVal e As MouseEventArgs)
        ' Check if the mouse is down
        If Go = True Then

            ' Set the mouse position
            HoldLeft = (Control.MousePosition.X - Me.Left)
            HoldTop = (Control.MousePosition.Y - Me.Top)

            ' Find where the mouse was clicked ONE TIME
            If TopSet = False Then
                OffTop = HoldTop - sender.parent.Top
                ' Once the position is held, flip the switch
                ' so that it doesn't keep trying to find the position
                TopSet = True
            End If
            If LeftSet = False Then
                OffLeft = HoldLeft - sender.parent.Left
                ' Once the position is held, flip the switch
                ' so that it doesn't keep trying to find the position
                LeftSet = True
            End If

            ' Set the position of the object
            sender.parent.Left = HoldLeft - OffLeft
            sender.parent.Top = HoldTop - OffTop
        End If
    End Sub

    Private Sub Movable_MouseMove2(ByVal sender As Object, ByVal e As MouseEventArgs) Handles Button3.MouseMove, Button4.MouseMove, Button5.MouseMove, Button8.MouseMove
        ' Check if the mouse is down
        If Go = True Then

            ' Set the mouse position
            HoldLeft = (Control.MousePosition.X - Me.Left)
            HoldTop = (Control.MousePosition.Y - Me.Top)

            ' Find where the mouse was clicked ONE TIME
            If TopSet = False Then
                OffTop = HoldTop - sender.parent.parent.Top
                ' Once the position is held, flip the switch
                ' so that it doesn't keep trying to find the position
                TopSet = True
            End If
            If LeftSet = False Then
                OffLeft = HoldLeft - sender.parent.parent.Left
                ' Once the position is held, flip the switch
                ' so that it doesn't keep trying to find the position
                LeftSet = True
            End If

            ' Set the position of the object
            sender.parent.parent.Left = HoldLeft - OffLeft
            sender.parent.parent.Top = HoldTop - OffTop
        End If
    End Sub

    Private Sub Movable_Panel_Drag(ByVal sender As Object, ByVal e As MouseEventArgs) Handles MainButton.MouseMove
        ' Check if the mouse is down
        If Go = True Then

            ' Set the mouse position
            HoldLeft = (Control.MousePosition.X - Me.Left)
            HoldTop = (Control.MousePosition.Y - Me.Top)

            ' Find where the mouse was clicked ONE TIME
            If TopSet = False Then
                OffTop = HoldTop - MasterPanel.Top
                ' Once the position is held, flip the switch
                ' so that it doesn't keep trying to find the position
                TopSet = True
            End If
            If LeftSet = False Then
                OffLeft = HoldLeft - MasterPanel.Left
                ' Once the position is held, flip the switch
                ' so that it doesn't keep trying to find the position
                LeftSet = True
            End If

            ' Set the position of the object
            MasterPanel.Left = HoldLeft - OffLeft
            MasterPanel.Top = HoldTop - OffTop
        End If
    End Sub

    Private Sub Movable_Panel_Resize(ByVal sender As Object, ByVal e As MouseEventArgs) Handles MasterPanelResizeGrabber.MouseMove
        If Go = True Then
            HoldWidth = (Control.MousePosition.X - MasterPanel.Left)
            HoldHeight = (Control.MousePosition.Y - MasterPanel.Top)
            If TopSet = False Then
                TopSet = True
            End If
            If LeftSet = False Then
                LeftSet = True
            End If
            MasterPanel.Width = HoldWidth
            MasterPanel.Height = HoldHeight
            MasterPanel.Refresh()
        End If
    End Sub
#End Region

#Region "Screen Capture Binding"

    ' Capture model
    ' ─────────────────────────────────────────────────────────────────────────
    ' Each Bind*Capture call wires one PictureBox (positioned by the user to
    ' overlay the corresponding in-game bar) to one stat on _selfData. On every
    ' LogScanTick.Tick, SampleSelfBars() captures the pixels behind each bound
    ' PictureBox and uses ScreenCaptureHelper.AnalyseBarFill to derive a fill
    ' fraction. That fraction overwrites the relevant field on _selfData and is
    ' picked up by the next BroadcastSelfAsync.
    '
    ' AnalyseBarFill is colour-agnostic: it identifies "filled" pixels by their
    ' channel spread (one or two RGB channels significantly higher than the
    ' others), so the same single configuration handles HP, stamina, water,
    ' food, and any other saturated-fill bar against a neutral background.
    '
    ' Captures NEVER write to a remote party member's data: those values flow
    ' in the opposite direction (network -> ApplyIncomingMemberData -> panel).

    ''' <summary>
    ''' Bind a PictureBox overlay to the local player's COMPOUND Health/Stamina
    ''' bar. In games like Wurm Online, health and stamina share a single bar:
    ''' green stamina grows rightward from the left edge, red damage grows
    ''' leftward from the right edge, and health caps maximum stamina so the
    ''' two regions can meet anywhere along the bar.
    '''
    ''' On every LogScanTick the bar is captured ONCE and run through
    ''' <see cref="ScreenCaptureHelper.AnalyseHpStamBar"/>, which detects each
    ''' colour's extent independently and yields both stats from the same
    ''' frame.
    ''' </summary>
    Public Sub BindHealthStaminaCapture(pb As PictureBox)
        _hpStamCaptureBox = pb
    End Sub

    ''' <summary>Bind a PictureBox overlay to the local player's Water bar.</summary>
    Public Sub BindWaterCapture(pb As PictureBox)
        _waterCaptureBox = pb
    End Sub

    ''' <summary>Bind a PictureBox overlay to the local player's Food bar.</summary>
    Public Sub BindFoodCapture(pb As PictureBox)
        _foodCaptureBox = pb
    End Sub

    ''' <summary>
    ''' Bind a PictureBox overlay to the local player's Favor bar. Toggling
    ''' the Favor row off on the CaptureSurfacePanel makes its PictureBox
    ''' invisible, at which point CaptureBarFraction skips it cleanly — so
    ''' the existing <c>_selfData.FavorPercent</c> stops being overwritten
    ''' rather than getting clobbered with zero.
    ''' </summary>
    Public Sub BindFavorCapture(pb As PictureBox)
        _favorCaptureBox = pb
    End Sub

    ''' <summary>
    ''' Sample every bound capture overlay once and push the results into
    ''' <c>_selfData</c>. Driven by LogScanTick.Tick. Bindings that haven't
    ''' been set up (or that fail to capture) are simply skipped — the
    ''' previous value remains in place for that stat.
    ''' </summary>
    Private Sub SampleSelfBars()
        If _selfData Is Nothing Then Return

        ' HP / Stamina come from the SAME capture: a single compound bar is
        ' analysed for both colours' extents in one pass.
        SampleHpStamCompound()

        Dim v As Double

        v = CaptureBarFraction(_favorCaptureBox)
        If v >= 0.0 Then _selfData.FavorPercent = v

        v = CaptureBarFraction(_waterCaptureBox)
        If v >= 0.0 Then _selfData.WaterPercent = v

        v = CaptureBarFraction(_foodCaptureBox)
        If v >= 0.0 Then _selfData.FoodPercent = v
    End Sub

    ''' <summary>
    ''' Capture the compound HP/Stamina bar once and unpack both readings
    ''' into <c>_selfData</c>. Stamina is green-from-left as an absolute
    ''' fraction of the bar width; HealthPercent is 1 - red-from-right, also
    ''' absolute. These are exactly the semantics
    ''' <see cref="PartySubPanel.DrawHealthStaminaBar"/> expects, so no
    ''' rescaling is required at this layer.
    ''' Skips cleanly when the box isn't bound, isn't visible, or capture
    ''' fails — leaving the previous broadcast values intact rather than
    ''' clobbering them with zero.
    ''' </summary>
    Private Sub SampleHpStamCompound()
        Dim pb As PictureBox = _hpStamCaptureBox
        If pb Is Nothing Then Return
        ' NOTE: do NOT check pb.Visible. The capture surface is deliberately
        ' hidden during normal operation so it doesn't capture itself - that
        ' would make every child report Visible=False and short-circuit every
        ' capture, leaving _selfData at its default 100% values.
        If pb.Width <= 0 OrElse pb.Height <= 0 Then Return

        Try
            Using bmp As Bitmap = ScreenCaptureHelper.CaptureUnderControl(pb)
                If bmp Is Nothing OrElse bmp.Width = 0 OrElse bmp.Height = 0 Then Return
                Dim r As ScreenCaptureHelper.HpStamReading =
                    ScreenCaptureHelper.AnalyseHpStamBar(bmp, CaptureChannelSpread, , , CaptureAbsoluteFloor)
                _selfData.StaminaPercent = r.Stamina
                _selfData.HealthPercent = r.Health
            End Using
        Catch ex As Exception
            Debug.WriteLine($"SampleHpStamCompound: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Capture the pixels under <paramref name="pb"/> via CaptureUnderControl
    ''' and run them through AnalyseBarFill. Returns -1.0 when the binding is
    ''' not configured, the box has zero size (e.g. Favor row collapsed via
    ''' FavorVisible=False), or capture fails - so the caller can distinguish
    ''' "no reading" from a legitimate 0% reading and avoid clobbering the
    ''' previous value.
    '''
    ''' NOTE: We do NOT check pb.Visible. The capture surface is deliberately
    ''' hidden during normal operation so it doesn't capture its own pixels;
    ''' a Visible check would short-circuit every sample. RectangleToScreen
    ''' works on invisible controls, so the capture geometry is still correct.
    ''' </summary>
    Private Function CaptureBarFraction(pb As PictureBox) As Double
        If pb Is Nothing Then Return -1.0
        If pb.Width <= 0 OrElse pb.Height <= 0 Then Return -1.0
        Try
            Using bmp As Bitmap = ScreenCaptureHelper.CaptureUnderControl(pb)
                If bmp Is Nothing OrElse bmp.Width = 0 OrElse bmp.Height = 0 Then
                    Return -1.0
                End If
                Return ScreenCaptureHelper.AnalyseBarFill(bmp, CaptureChannelSpread, , , CaptureAbsoluteFloor)
            End Using
        Catch ex As Exception
            ' Capture errors (window minimised, control not yet placed, etc.)
            ' are silent; the next tick will retry.
            Debug.WriteLine($"SampleSelfBars: {ex.Message}")
            Return -1.0
        End Try
    End Function

    ''' <summary>
    ''' Convenience: sample the filled colour directly from a PictureBox at runtime
    ''' (the box should be over a fully-filled bar when you call this). Useful for
    ''' calibration / diagnostics only — the runtime analyser no longer needs it.
    ''' </summary>
    Public Function SampleFilledColour(pb As PictureBox) As Color
        Using bmp As Bitmap = ScreenCaptureHelper.CaptureUnderControl(pb)
            Return ScreenCaptureHelper.AutoDetectFilledColour(bmp)
        End Using
    End Function

#End Region

#Region "Log-Parsing Functions"
    '########## EVENT LOGGER ##########
    Private Sub EvtLogStreamer(UseLogFile As String, Parent As Object)
        If File.Exists(UseLogFile) Then
            Dim fs As New FileStream(UseLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Dim sr As New StreamReader(fs)
            If _EventLastOffset < sr.BaseStream.Length Then
                If _EventLastOffset = 0 Then
                    _EventLastOffset = sr.BaseStream.Length
                Else
                    sr.BaseStream.Seek(_EventLastOffset, SeekOrigin.Begin)
                    While sr.Peek() <> -1
                        Dim read As String = sr.ReadLine()
                        If read.Length <> 0 Then
                            Parent.AppendText(Environment.NewLine & read)
                            Parent.Select(Parent.TextLength, 0)
                            Parent.ScrollToCaret()
                            EventParser(read, Parent)
                            'MacroParser(read)
                        End If
                    End While
                    _EventLastOffset = sr.BaseStream.Position
                End If
            End If
            sr.Close()
            fs.Close()
        End If
    End Sub

    Public Sub EventParser(LineIn As String, StreamParent As Object)
        'Select Last Written Line
        StreamParent.Select(StreamParent.TextLength - LineIn.Length, StreamParent.TextLength)

        '##### Event Messages #####
        If LineIn.Contains("are now encumbered") = True Then
            'Overencumbered Trigger
            _selfData.IsEncumbered = True
        ElseIf LineIn.Contains("You may now move") = True Then
            'Overencumbered Release
            _selfData.IsEncumbered = False
        ElseIf LineIn.Contains("OUCH!") = True Then
            'Thrown by Elite Creature, struck by lightning, or other injury event
        ElseIf LineIn.Contains("throws you") = True Then
            'Thrown by Elite Creature
        ElseIf LineIn.Contains("You will now fight") = True Then
            If LineIn.Contains("aggressively") = True Then
                _selfData.CombatStance = 1
            ElseIf LineIn.Contains("normally") = True Then
                _selfData.CombatStance = 2
            ElseIf LineIn.Contains("defensively") = True Then
                _selfData.CombatStance = 3
            End If
        End If
    End Sub

    '########## COMBAT LOGGER ##########
    Private Sub CbtLogStreamer(UseLogFile As String, Parent As Object)
        If File.Exists(UseLogFile) Then
            Dim fs2 As New FileStream(UseLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Dim sr2 As New StreamReader(fs2)
            If _CombatLastOffset < sr2.BaseStream.Length Then
                If _CombatLastOffset = 0 Then
                    _CombatLastOffset = sr2.BaseStream.Length
                Else
                    sr2.BaseStream.Seek(_CombatLastOffset, SeekOrigin.Begin)
                    While sr2.Peek() <> -1
                        Dim read2 As String = sr2.ReadLine()
                        If read2.Length <> 0 Then
                            Parent.AppendText(Environment.NewLine & read2)
                            Parent.Select(Parent.TextLength, 0)
                            Parent.ScrollToCaret()
                            CombatParser(read2, Parent)
                        End If
                    End While
                    _CombatLastOffset = sr2.BaseStream.Position
                End If
            End If
            sr2.Close()
            fs2.Close()
        End If
    End Sub

    Public Sub CombatParser(LineIn As String, StreamParent As Object)
        'Select Last Written Line
        StreamParent.Select(StreamParent.TextLength - LineIn.Length, StreamParent.TextLength)

        Dim AnyCombatAction As Boolean = False

        If LineIn.Contains("You aim to push") = True And LineIn.Contains("over with your shield") = True Then
            'Start Shield Bash
            AnyCombatAction = True
        ElseIf LineIn.Contains("is sprawling on the ground") = True Then
            'Finish Shield Bash - Success
            AnyCombatAction = True
            _selfData.CooldownActive = True
            _selfData.CooldownTotalMs = 20000
            _selfData.CooldownRemainingMs = 20000
        ElseIf LineIn.Contains("swiftly dodges your") = True Then
            'Finish Shield Bash - Fail
            AnyCombatAction = True
            _selfData.CooldownActive = True
            _selfData.CooldownTotalMs = 20000
            _selfData.CooldownRemainingMs = 20000
        End If

        If LineIn.Contains("You try to move into position") = True Then
            'Begin Targetting Action
            AnyCombatAction = True
        ElseIf LineIn.Contains("You move into position") = True Then
            'Finish Targetting Action - Success
            AnyCombatAction = True
            If LineIn.Contains("upper left parts") = True Then
                _selfData.SelectedNumpadKey = 7
            ElseIf LineIn.Contains("upper parts") = True Then
                _selfData.SelectedNumpadKey = 8
            ElseIf LineIn.Contains("upper right parts") = True Then
                _selfData.SelectedNumpadKey = 9
            ElseIf LineIn.Contains("left parts") = True Then
                _selfData.SelectedNumpadKey = 4
            ElseIf LineIn.Contains("center parts") = True Then
                _selfData.SelectedNumpadKey = 5
            ElseIf LineIn.Contains("right parts") = True Then
                _selfData.SelectedNumpadKey = 6
            ElseIf LineIn.Contains("lower left parts") = True Then
                _selfData.SelectedNumpadKey = 1
            ElseIf LineIn.Contains("lower parts") = True Then
                _selfData.SelectedNumpadKey = 2
            ElseIf LineIn.Contains("lower right parts") = True Then
                _selfData.SelectedNumpadKey = 3
            End If
        ElseIf LineIn.Contains("You fail to move into position") = True Then
            'Finish Targetting Action - Fail
            AnyCombatAction = True
        End If

        If LineIn.Contains("You prepare to shelter") = True Then
            'Begin Defend Action
            AnyCombatAction = True
        ElseIf LineIn.Contains("You shelter the") = True Then
            'Finish Defend Action - Success
            AnyCombatAction = True
            If LineIn.Contains("upper parts") = True Then
                _selfData.ShelterDirection = 1
            ElseIf LineIn.Contains("right parts") = True Then
                _selfData.ShelterDirection = 2
            ElseIf LineIn.Contains("lower parts") = True Then
                _selfData.ShelterDirection = 3
            ElseIf LineIn.Contains("right parts") = True Then
                _selfData.ShelterDirection = 4
            End If
        ElseIf LineIn.Contains("You still feel open") = True Then
            'Finish Defend Action - Fail
            AnyCombatAction = True
        End If

        If LineIn.Contains("You open yourself to an attack") = True Then
            'Bad Move
            AnyCombatAction = True
            'If we throw _selfData.IsStunned = True here, there is no recovery event
            'So this value will never reset.
        ElseIf LineIn.Contains("pushes you over with") = True Then
            'Shield Bashed - Stunned
            AnyCombatAction = True
            _selfData.IsStunned = True
        ElseIf LineIn.Contains("regain your bearings") = True Then
            'Shield Bashed - Recovery
            AnyCombatAction = True
            _selfData.IsStunned = False
        End If

        If LineIn.Contains("moves in to attack you") = True Then
            'New Opponent
            AnyCombatAction = True
        End If

        If LineIn.Contains("tries to move into position to target your") = True Then
            'Opponent Begin Targetting Action
            AnyCombatAction = True
        ElseIf LineIn.Contains("targets your") = True Then
            'Opponent Finish Targetting Action
            AnyCombatAction = True
        End If




        '##### Combat Message Colors #####
        'good events (inflicting damage)
        If LineIn.Contains("You cut") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You pierce") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You maul") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You hit") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You kick") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("Your weapon") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You burn") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You freeze") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You grab") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You hurt") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You paint") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You jam") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You string") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You doubly grind") = True Then
            AnyCombatAction = True
        End If
        'bad events (recieving damage)
        If LineIn.Contains("cuts you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("pierces you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("mauls you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("hits you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("kicks you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("burns you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("freezes you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("grabs you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("hurts you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("headbutts you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("breathes you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("paints you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("jams you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("strings you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("doubly grinds you") = True Then
            AnyCombatAction = True
        End If
        'up-set events (stuns / AOE)
        If LineIn.Contains("is sprawling on the ground.") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("regains his bearings") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("regains her bearings") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("regain your bearings") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("up to an easy attack") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("make a bad move") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("makes a circular powerful sweep") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("stuns you") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("pushes you over with") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You swiftly dodge a") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You aim to push") = True Then
            AnyCombatAction = True
        End If
        'Parries, blocks, misses
        If LineIn.Contains("parry with your") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("parries with a") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("but you raise your shield and parry.") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("shield and parries.") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("You miss with the") = True Then
            AnyCombatAction = True
        ElseIf LineIn.Contains("Your attack glances off") = True Then
            AnyCombatAction = True
        End If

        If AnyCombatAction = True Then
            _selfData.IsInCombat = True
        Else
            If PassesWithoutCombat >= 5 Then
                ResetCombatParameters()
            Else
                PassesWithoutCombat = PassesWithoutCombat + 1
            End If
        End If

        'Deselect all lines and reset color to white
        StreamParent.Select(StreamParent.TextLength, 0)
        StreamParent.SelectionColor = Color.White
    End Sub

    '########## UI LOG DISPLAY ##########
    Public Sub AddEvent(ByVal Log1 As String)
        NetCommsTextBox.Text = NetCommsTextBox.Text + Environment.NewLine + Log1
    End Sub
#End Region

#Region "Cooldown Timer Tick"

    ''' <summary>
    ''' Global timer that advances cooldowns on all sub-panels and on self.
    ''' Fires every CooldownTickMs milliseconds.
    ''' </summary>
    Private Sub _cooldownTimer_Tick(sender As Object, e As EventArgs) Handles _cooldownTimer.Tick
        For Each panel In _partyGrid.Panels
            If panel.Data.CooldownActive Then
                panel.TickCooldown(CooldownTickMs)
            End If
        Next

        ' Self has no display panel, so we tick its cooldown directly on _selfData.
        ' The next broadcast picks up the decremented value automatically.
        If _selfData IsNot Nothing AndAlso _selfData.CooldownActive Then
            _selfData.CooldownRemainingMs -= CooldownTickMs
            If _selfData.CooldownRemainingMs <= 0 Then
                _selfData.CooldownRemainingMs = 0
                _selfData.CooldownActive = False
            End If
        End If
    End Sub

#End Region

#Region "High-Level Convenience API"

    ' These are the methods your UI buttons / network receive handlers should call.

    ''' <summary>Push a full stat update to a party member.</summary>
    ''' <param name="memberIndex">0-based index into the party grid.</param>
    Public Sub UpdateMemberStats(memberIndex As Integer,
                                 health As Double, stamina As Double,
                                 water As Double, food As Double)
        Dim p As PartySubPanel = _partyGrid.GetPanel(memberIndex)
        If p Is Nothing Then Return
        p.UpdateHealthStamina(health, stamina)
        p.UpdateSustenance(water, food)
    End Sub

    ''' <summary>Show the action bar for a member.</summary>
    Public Sub ShowMemberAction(memberIndex As Integer, label As String,
                                Optional progress As Double = 0.0)
        _partyGrid.GetPanel(memberIndex)?.ShowActionBar(label, progress)
    End Sub

    ''' <summary>Update action bar progress for a member.</summary>
    Public Sub UpdateMemberAction(memberIndex As Integer, progress As Double)
        _partyGrid.GetPanel(memberIndex)?.UpdateActionProgress(progress)
    End Sub

    ''' <summary>Hide the action bar for a member.</summary>
    Public Sub HideMemberAction(memberIndex As Integer)
        _partyGrid.GetPanel(memberIndex)?.HideActionBar()
    End Sub

    ''' <summary>Set a specific numpad cell for a member.</summary>
    Public Sub SetMemberNumpad(memberIndex As Integer, numpadKey As Integer, value As Integer)
        _partyGrid.GetPanel(memberIndex)?.SelectNumpadCell(numpadKey)
    End Sub

    ''' <summary>Trigger cooldown for a member.</summary>
    Public Sub TriggerMemberCooldown(memberIndex As Integer, durationSeconds As Double)
        _partyGrid.GetPanel(memberIndex)?.TriggerCooldown(durationSeconds)
    End Sub

    ''' <summary>Cancel a member's cooldown immediately.</summary>
    Public Sub CancelMemberCooldown(memberIndex As Integer)
        _partyGrid.GetPanel(memberIndex)?.CancelCooldown()
    End Sub

    ''' <summary>Rename a party member.</summary>
    Public Sub RenameMember(memberIndex As Integer, newName As String)
        Dim p As PartySubPanel = _partyGrid.GetPanel(memberIndex)
        If p Is Nothing Then Return
        p.Data.PlayerName = newName
        p.Invalidate()
    End Sub

#End Region

#Region "High-Level Convenience API - By Name"

    ' These name-keyed variants are the preferred entry point for network code:
    ' indices are local to each client and can shift as people join or leave,
    ' but player names are stable identifiers that travel inside the message
    ' payload itself.

    ''' <summary>Push a full stat update to a party member identified by name.</summary>
    Public Sub UpdateMemberStatsByName(name As String,
                                       health As Double, stamina As Double,
                                       water As Double, food As Double)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p Is Nothing Then Return
        p.UpdateHealthStamina(health, stamina)
        p.UpdateSustenance(water, food)
    End Sub

    ''' <summary>Show the action bar for a member identified by name.</summary>
    Public Sub ShowMemberActionByName(name As String, label As String,
                                      Optional progress As Double = 0.0)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p IsNot Nothing Then p.ShowActionBar(label, progress)
    End Sub

    ''' <summary>Update action bar progress for a member identified by name.</summary>
    Public Sub UpdateMemberActionByName(name As String, progress As Double)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p IsNot Nothing Then p.UpdateActionProgress(progress)
    End Sub

    ''' <summary>Hide the action bar for a member identified by name.</summary>
    Public Sub HideMemberActionByName(name As String)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p IsNot Nothing Then p.HideActionBar()
    End Sub

    ''' <summary>Set a specific numpad cell for a member identified by name.</summary>
    Public Sub SetMemberNumpadByName(name As String, numpadKey As Integer)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p IsNot Nothing Then p.SelectNumpadCell(numpadKey)
    End Sub

    ''' <summary>Trigger cooldown for a member identified by name.</summary>
    Public Sub TriggerMemberCooldownByName(name As String, durationSeconds As Double)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p IsNot Nothing Then p.TriggerCooldown(durationSeconds)
    End Sub

    ''' <summary>Cancel a member's cooldown immediately, by name.</summary>
    Public Sub CancelMemberCooldownByName(name As String)
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(name)
        If p IsNot Nothing Then p.CancelCooldown()
    End Sub

    ''' <summary>
    ''' Rename a party member, looking them up by their current name.
    ''' No-op if no panel currently holds <paramref name="currentName"/>.
    ''' </summary>
    Public Sub RenameMemberByName(currentName As String, newName As String)
        If String.IsNullOrEmpty(newName) Then Return
        Dim p As PartySubPanel = _partyGrid.GetPanelByName(currentName)
        If p Is Nothing Then Return
        p.Data.PlayerName = newName
        p.Invalidate()
    End Sub

    ''' <summary>Remove a party member by name. Returns True if a panel was removed.</summary>
    Public Function RemovePartyMemberByName(name As String) As Boolean
        Return _partyGrid.RemoveMemberByName(name)
    End Function

    ''' <summary>Locate (or create) the panel for a member by name.</summary>
    Public Function GetOrAddMemberPanel(name As String) As PartySubPanel
        Return _partyGrid.FindOrAddMember(name)
    End Function

#End Region

#Region "Self-State API"

    ' These helpers all mutate _selfData in place. There is no display panel
    ' for self, so there is nothing to invalidate or resize — the new values
    ' simply ship out on the next BroadcastSelfAsync. The Bar capture pipeline
    ' uses the same backing store, so screen-scraped values and code-driven
    ' overrides interleave naturally.

    ''' <summary>
    ''' Read-only access to the local user's PartyMemberData. Mutate via the
    ''' UpdateSelf* / ShowSelfAction / TriggerSelfCooldown helpers below so
    ''' invariants (0-1 clamping, etc.) are honoured.
    ''' </summary>
    Public ReadOnly Property SelfData As PartyMemberData
        Get
            Return _selfData
        End Get
    End Property

    ''' <summary>
    ''' Change the local user's broadcast name. Picked up automatically by the
    ''' next network tick.
    ''' </summary>
    Public Sub SetSelfName(name As String)
        If _selfData Is Nothing Then Return
        If String.IsNullOrWhiteSpace(name) Then Return
        Dim trimmed As String = name.Trim()
        If String.Equals(_selfData.PlayerName, trimmed, StringComparison.Ordinal) Then Return
        _selfData.PlayerName = trimmed
    End Sub

    Public Sub UpdateSelfHealthStamina(health As Double, stamina As Double)
        If _selfData Is Nothing Then Return
        _selfData.HealthPercent = Clamp01(health)
        _selfData.StaminaPercent = Clamp01(stamina)
    End Sub

    Public Sub UpdateSelfSustenance(water As Double, food As Double)
        If _selfData Is Nothing Then Return
        _selfData.WaterPercent = Clamp01(water)
        _selfData.FoodPercent = Clamp01(food)
    End Sub

    Public Sub UpdateSelfFavor(favor As Double)
        If _selfData Is Nothing Then Return
        _selfData.FavorPercent = Clamp01(favor)
    End Sub

    Public Sub ShowSelfAction(label As String, Optional progress As Double = 0.0)
        If _selfData Is Nothing Then Return
        _selfData.ActionLabel = If(String.IsNullOrEmpty(label), "Action", label)
        _selfData.ActionPercent = Clamp01(progress)
        _selfData.ActionBarVisible = True
    End Sub

    Public Sub UpdateSelfAction(progress As Double)
        If _selfData Is Nothing Then Return
        _selfData.ActionPercent = Clamp01(progress)
    End Sub

    Public Sub HideSelfAction()
        If _selfData Is Nothing Then Return
        _selfData.ActionBarVisible = False
    End Sub

    Public Sub TriggerSelfCooldown(durationSeconds As Double)
        If _selfData Is Nothing Then Return
        _selfData.CooldownTotalMs = Math.Max(0, CInt(durationSeconds * 1000))
        _selfData.CooldownRemainingMs = _selfData.CooldownTotalMs
        _selfData.CooldownActive = _selfData.CooldownTotalMs > 0
    End Sub

    Public Sub CancelSelfCooldown()
        If _selfData Is Nothing Then Return
        _selfData.CooldownActive = False
        _selfData.CooldownRemainingMs = 0
    End Sub

    Public Sub SetSelfNumpad(numpadKey As Integer)
        If _selfData Is Nothing Then Return
        _selfData.SelectedNumpadKey = numpadKey
    End Sub

    ''' <summary>
    ''' Toggle the "highlighted" flag we broadcast about ourselves (e.g. an
    ''' in-combat / focus indicator). Other clients render the highlight on
    ''' their copy of our panel; we never see it locally because self has no
    ''' panel.
    ''' </summary>
    Public Sub SetSelfHighlighted(flag As Boolean)
        If _selfData Is Nothing Then Return
        _selfData.IsInCombat = flag
    End Sub

    Private Sub ResetCombatParameters()
        _selfData.IsInCombat = False
        _selfData.IsStunned = False
        _selfData.SelectedNumpadKey = 0
        _selfData.ShelterDirection = 0
    End Sub

    Private Shared Function Clamp01(v As Double) As Double
        If Double.IsNaN(v) Then Return 0.0
        If v < 0.0 Then Return 0.0
        If v > 1.0 Then Return 1.0
        Return v
    End Function

#End Region

#Region "UI Functionality"
    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBox1.CheckedChanged
        ' Toggle the Favor row on / off. The CaptureSurfacePanel handles the
        ' layout shift and the height change internally - food/water move up
        ' to fill the freed space and the container shrinks from 31 px to 21 px.
        If _selfData IsNot Nothing Then
            If _captureSurface IsNot Nothing Then
                _selfData.FavorEnabled = CheckBox1.Checked
                _captureSurface.FavorVisible = CheckBox1.Checked
            End If
        End If
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        If SettingsPanel.Visible = True Then
            SettingsPanel.Visible = False
            _captureSurface.Visible = False
            SaveAllUserSettings()
        Else
            SettingsPanel.Visible = True
            _captureSurface.Visible = True
        End If
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        If DebugPanel.Visible = True Then
            DebugPanel.Visible = False
        Else
            DebugPanel.Visible = True
        End If
    End Sub

    ''' <summary>
    ''' Push the current CharacterNameTextBox text into the self PartyMemberData
    ''' so outgoing broadcasts use the right name. Safe to call before the
    ''' control is fully initialised.
    ''' </summary>
    Private Sub SyncSelfNameFromTextBox()
        Try
            If CharacterNameTextBox IsNot Nothing Then
                SetSelfName(CharacterNameTextBox.Text)
            End If
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Keep <see cref="_selfData"/>'s PlayerName in sync with the textbox as
    ''' the user edits it. The next broadcast will pick up the new name.
    ''' </summary>
    Private Sub CharacterNameTextBox_TextChanged(sender As Object, e As EventArgs) _
            Handles CharacterNameTextBox.TextChanged
        If _selfData IsNot Nothing Then
            SetSelfName(CharacterNameTextBox.Text)
        End If
    End Sub

    Private Sub StartLogWatcher()
        Dim CurrYear As String = currentdate.Year
        Dim CurrMonth As String = currentdate.Month
        If Int(CurrMonth) < 10 Then
            CurrMonth = "0" & currentdate.Month
        End If
        EventLogfile = GamePathTextBox.Text & "players\" & CharacterNameTextBox.Text & "\logs\_Event." & CurrYear & "-" & CurrMonth & ".txt"
        CombatLogfile = GamePathTextBox.Text & "players\" & CharacterNameTextBox.Text & "\logs\_Combat." & CurrYear & "-" & CurrMonth & ".txt"
        LogScanTick.Start()
    End Sub

    Private Sub LogScanTick_Tick(sender As Object, e As EventArgs) Handles LogScanTick.Tick
        EvtLogStreamer(EventLogfile, EventTextBox)
        CbtLogStreamer(CombatLogfile, CombatTextBox)
        SampleSelfBars()
        SelfSubPanelDisplay.SetData(_selfData)
        SelfSubPanelDisplay.AutoResizeHeight()
        SelfSubPanelDisplay.Invalidate()
    End Sub

    Private Sub RadioButton1_CheckedChanged(sender As Object, e As EventArgs) Handles RadioButton1.CheckedChanged
        RadioButton2.Checked = False
        GamePathTextBox.Text = "C:\Program Files\Wurm Online\"
    End Sub

    Private Sub RadioButton2_CheckedChanged(sender As Object, e As EventArgs) Handles RadioButton2.CheckedChanged
        RadioButton1.Checked = False
        GamePathTextBox.Text = "C:\Program Files\Steam\steamapps\common\Wurm Online\gamedata\"
    End Sub

    Private Sub NumericUpDown1_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown1.ValueChanged
        If _partyGrid IsNot Nothing Then
            _partyGrid.Columns = NumericUpDown1.Value
        End If
    End Sub
#End Region

#Region "UI Network Button Handlers"
    Private Async Sub btnHost_Click(sender As Object, e As EventArgs) Handles btnHost.Click
        CheckForUpdates(ServerAddressBox.Text)
        If API_Update_Required = False Then
            NetworkClient = New SecureChannelClient(ServerAddressBox.Text)
            Try
                SyncSelfNameFromTextBox()
                Dim ok As Boolean = Await NetworkClient.CreateChannelAsync(txtPassword.Text)
                If ok Then
                    AddEvent($"[net] hosting channel as '{_selfData.PlayerName}'")
                    StartLogWatcher()
                    NetworkTimer.Start()
                    HandleNetworkButtonVisibility(True)
                Else
                    AddEvent("[net] channel already exists - use Join instead")
                End If
            Catch ex As Exception
                AddEvent($"[net] host failed: {ex.Message}")
            End Try
        End If
    End Sub

    ''' <summary>Called when the user clicks "Join Channel".</summary>
    Private Async Sub btnJoin_Click(sender As Object, e As EventArgs) Handles btnJoin.Click
        CheckForUpdates(ServerAddressBox.Text)
        If API_Update_Required = False Then
            NetworkClient = New SecureChannelClient(ServerAddressBox.Text)
            Try
                SyncSelfNameFromTextBox()
                Dim ok As Boolean = Await NetworkClient.JoinChannelAsync(txtPassword.Text)
                If ok Then
                    AddEvent($"[net] joined channel as '{_selfData.PlayerName}'")
                    StartLogWatcher()
                    NetworkTimer.Start()
                    HandleNetworkButtonVisibility(True)
                Else
                    AddEvent("[net] join failed - wrong password or channel does not exist")
                End If
            Catch ex As Exception
                AddEvent($"[net] join failed: {ex.Message}")
            End Try
        End If
    End Sub

    Private Async Sub btnLeave_Click(sender As Object, e As EventArgs) Handles btnLeave.Click
        NetworkTimer.Stop()
        AddEvent("[net] stopped listening")
        HandleNetworkButtonVisibility(False)
    End Sub

    Private Sub HandleNetworkButtonVisibility(state As Boolean)
        If state = True Then
            btnHost.Visible = False
            btnJoin.Visible = False
            btnLeave.Visible = True
        Else
            btnHost.Visible = True
            btnJoin.Visible = True
            btnLeave.Visible = False
        End If
    End Sub
#End Region

#Region "Network Communication"

    Private Async Sub SendNetworkMessage(msg As String)
        Await NetworkClient.PostMessageAsync(msg)
    End Sub

    ' =========================================================================
    ' NETWORK SYNC
    '
    ' Each NetworkTimer tick performs two phases:
    '   1. Pull any new messages from the SecureChannel and dispatch them.
    '      Messages that decode as a PartyMemberData JSON snapshot are applied
    '      to the matching panel (created on demand if no panel currently
    '      holds that name). Anything else is logged as a raw chat line.
    '   2. Broadcast our own PartyMemberData snapshot, so other clients see
    '      our current state.
    '
    ' Every external call is wrapped so transient network failures do not
    ' kill the timer. A re-entrancy guard prevents tick handlers from piling
    ' up if the channel is briefly slow.
    ' =========================================================================

    Private Async Sub NetworkTimer_Tick(sender As Object, e As EventArgs) Handles NetworkTimer.Tick
        ' UI-thread re-entrancy guard. NetworkTimer is a WinForms Timer so its
        ' Tick fires on the UI thread, but our handler awaits and may yield
        ' control back to the message pump before completing; a subsequent
        ' Tick could otherwise start a second concurrent network conversation.
        If _networkTickBusy Then Return
        _networkTickBusy = True
        Try
            Await ReceiveAndDispatchAsync()
            Await BroadcastSelfAsync()
        Finally
            _networkTickBusy = False
        End Try
    End Sub

    ''' <summary>
    ''' Pull any new messages from the SecureChannel and dispatch each one
    ''' through <see cref="ApplyIncomingMemberData"/> if it parses as a
    ''' PartyMemberData payload; otherwise log it raw.
    ''' </summary>
    Private Async Function ReceiveAndDispatchAsync() As Threading.Tasks.Task
        If NetworkClient Is Nothing Then Return
        If Not NetworkClient.IsJoined Then Return

        Dim messages As List(Of String) = Nothing
        Try
            messages = Await NetworkClient.GetMessagesAsync()
        Catch ex As Exception
            AddEvent($"[net] receive failed: {ex.Message}")
            Return
        End Try

        If messages Is Nothing OrElse messages.Count = 0 Then Return

        For Each msg As String In messages
            If String.IsNullOrEmpty(msg) Then Continue For

            Dim incoming As PartyMemberData = PartyMemberData.TryFromJson(msg)
            If incoming Is Nothing Then
                ' Not a PartyMemberData snapshot. Surface it so the user can
                ' see chat / debug traffic that happens to share the channel.
                AddEvent(msg)
                Continue For
            End If

            Try
                ApplyIncomingMemberData(incoming)
            Catch ex As Exception
                AddEvent($"[net] apply failed for '{incoming.PlayerName}': {ex.Message}")
            End Try
        Next
    End Function

    ''' <summary>
    ''' Apply a freshly-decoded PartyMemberData to the local UI:
    '''   * If we have no panel with that name, create one and seed it.
    '''   * If we do, update its bound data in-place and invalidate.
    '''   * Silently drop our own echo (when the channel reflects a message
    '''     we just posted, the server includes it in the next GET).
    ''' Pure UI thread: safe to mutate panels directly.
    ''' </summary>
    Private Sub ApplyIncomingMemberData(incoming As PartyMemberData)
        If incoming Is Nothing Then Return
        If String.IsNullOrWhiteSpace(incoming.PlayerName) Then Return

        ' Drop self-echo - the server replays our own posts back to us.
        If _selfData IsNot Nothing AndAlso
           String.Equals(incoming.PlayerName, _selfData.PlayerName,
                         StringComparison.OrdinalIgnoreCase) Then
            Return
        End If

        Dim panel As PartySubPanel = _partyGrid.GetPanelByName(incoming.PlayerName)
        Dim wasActionVisible As Boolean = False
        Dim wasFavorEnabled As Boolean = False
        Dim isNew As Boolean = False

        If panel Is Nothing Then
            panel = _partyGrid.AddMember(incoming.PlayerName)
            isNew = True
        Else
            wasActionVisible = panel.Data.ActionBarVisible
            wasFavorEnabled = panel.Data.FavorEnabled
        End If

        ' Copy values into the panel's existing data object so the panel's
        ' bound reference (set by AddMember / SetData) keeps pointing at the
        ' same instance that any other code might already hold.
        panel.Data.UpdateFrom(incoming)

        ' If any height-affecting flag changed, the panel's preferred height
        ' has changed too and the grid needs to reflow. Without this the new
        ' draw path can extend past the (stale) panel height and the bottom
        ' bar - cooldown - gets clipped.
        If isNew _
           OrElse panel.Data.ActionBarVisible <> wasActionVisible _
           OrElse panel.Data.FavorEnabled <> wasFavorEnabled Then
            panel.AutoResizeHeight()
        End If

        panel.Invalidate()
    End Sub

    ''' <summary>
    ''' Serialize <see cref="_selfData"/> and post it to the channel so the
    ''' other clients can update their view of us.
    ''' </summary>
    Private Async Function BroadcastSelfAsync() As Threading.Tasks.Task
        If _selfData Is Nothing Then Return
        If NetworkClient Is Nothing OrElse Not NetworkClient.IsJoined Then Return

        Dim payload As String
        Try
            payload = _selfData.ToJson()
        Catch ex As Exception
            AddEvent($"[net] serialize self failed: {ex.Message}")
            Return
        End Try

        Try
            Await NetworkClient.PostMessageAsync(payload)
        Catch ex As Exception
            AddEvent($"[net] broadcast failed: {ex.Message}")
        End Try
    End Function
#End Region

#Region "Form Closing / Dispose"
    ''' <summary>
    ''' Hook this into your Form's Closing event, or call it from your existing
    ''' override of OnFormClosing.
    ''' </summary>
    Private Sub CleanupPartySystem()
        _cooldownTimer.Stop()
    End Sub

    Private Async Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Kill Netowrk Connections
        CleanupPartySystem()
    End Sub

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles Button6.Click
        If QuickStartGuidePanel.Visible = True Then
            QuickStartGuidePanel.Visible = False
        Else
            QuickStartGuidePanel.Visible = True
        End If
    End Sub

    Private Sub Button7_Click(sender As Object, e As EventArgs) Handles Button7.Click
        Me.Close()
        Me.Dispose()
    End Sub

    Private Sub UpdateNotifDismissButton_Click(sender As Object, e As EventArgs) Handles UpdateNotifDismissButton.Click
        sender.parent.parent.Visible = False
    End Sub

    Private Sub CheckBox2_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBox2.CheckedChanged
        If SelfSubPanelDisplay Is Nothing Then Return
        SelfSubPanelDisplay.Visible = CheckBox2.Checked
        _partyGrid.Reflow()
    End Sub
#End Region
End Class