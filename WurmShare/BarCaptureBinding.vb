Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Enumerates which stat a capture binding is monitoring.
''' </summary>
Public Enum CapturedStat
    Health
    Stamina
    Water
    Food
    Action
    Cooldown    ' Not typically captured; here for completeness
End Enum

''' <summary>
''' Associates one PictureBox (screen overlay) with one stat slot on one PartySubPanel.
''' The main form's Timer calls Refresh() on each binding to capture and push values.
''' </summary>
Public Class BarCaptureBinding

#Region "Properties"

    ''' <summary>The invisible/transparent PictureBox overlaid on the game window.</summary>
    Public Property SourcePictureBox As PictureBox

    ''' <summary>The sub-panel that will receive the captured value.</summary>
    Public Property TargetPanel As PartySubPanel

    ''' <summary>Which stat this binding feeds.</summary>
    Public Property TargetStat As CapturedStat

    ''' <summary>Expected colour of a "filled" bar pixel.</summary>
    Public Property FilledColour As Color = Color.FromArgb(60, 180, 90)

    ''' <summary>Per-channel colour tolerance for matching (0–255).</summary>
    Public Property ColourTolerance As Integer = 30

    ''' <summary>Show the captured image inside the PictureBox for debugging.</summary>
    Public Property DebugPreview As Boolean = False

    ''' <summary>Last computed fill fraction (0.0–1.0).</summary>
    Public ReadOnly Property LastValue As Double
        Get
            Return _lastValue
        End Get
    End Property

    Private _lastValue As Double = 0.0

    ''' <summary>Set to False to pause this binding without removing it.</summary>
    Public Property IsActive As Boolean = True

#End Region

#Region "Constructor"

    Public Sub New(pb As PictureBox, panel As PartySubPanel, stat As CapturedStat,
                   filledColour As Color, Optional tolerance As Integer = 30)
        SourcePictureBox = pb
        TargetPanel = panel
        TargetStat = stat
        Me.FilledColour = filledColour
        ColourTolerance = tolerance
    End Sub

#End Region

#Region "Capture + Push"

    ''' <summary>
    ''' Capture the screen under the source PictureBox, compute the fill fraction,
    ''' and push the value to the appropriate stat on the target panel.
    ''' Call this from a Timer tick.
    ''' </summary>
    Public Sub Refresh()
        If Not IsActive OrElse SourcePictureBox Is Nothing OrElse TargetPanel Is Nothing Then
            Return
        End If

        Try
            _lastValue = ScreenCaptureHelper.CaptureAndAnalyse(
                SourcePictureBox, FilledColour, ColourTolerance, DebugPreview)

            Select Case TargetStat
                Case CapturedStat.Health
                    TargetPanel.UpdateHealthStamina(_lastValue, TargetPanel.Data.StaminaPercent)
                Case CapturedStat.Stamina
                    TargetPanel.UpdateHealthStamina(TargetPanel.Data.HealthPercent, _lastValue)
                Case CapturedStat.Water
                    TargetPanel.UpdateWater(_lastValue)
                Case CapturedStat.Food
                    TargetPanel.UpdateFood(_lastValue)
                Case CapturedStat.Action
                    TargetPanel.UpdateActionProgress(_lastValue)
            End Select
        Catch ex As Exception
            ' Swallow capture errors (e.g., game window minimised) – caller may log
            Debug.WriteLine($"BarCaptureBinding.Refresh error: {ex.Message}")
        End Try
    End Sub

#End Region

End Class

''' <summary>
''' Manages a collection of BarCaptureBindings and refreshes them all on a Timer.
''' </summary>
Public Class CaptureManager
    Implements IDisposable

    Private ReadOnly _bindings As New List(Of BarCaptureBinding)()
    Private ReadOnly _timer As New Timer()

    ''' <summary>Raised after all bindings have been refreshed in a tick.</summary>
    Public Event Refreshed As EventHandler

    Public Sub New(Optional intervalMs As Integer = 200)
        _timer.Interval = intervalMs
        AddHandler _timer.Tick, AddressOf OnTick
    End Sub

    ''' <summary>Add a binding to the managed collection.</summary>
    Public Sub AddBinding(binding As BarCaptureBinding)
        _bindings.Add(binding)
    End Sub

    ''' <summary>Remove a binding by reference.</summary>
    Public Sub RemoveBinding(binding As BarCaptureBinding)
        _bindings.Remove(binding)
    End Sub

    ''' <summary>Remove all bindings.</summary>
    Public Sub ClearBindings()
        _bindings.Clear()
    End Sub

    ''' <summary>All current bindings (read-only view).</summary>
    Public ReadOnly Property Bindings As IReadOnlyList(Of BarCaptureBinding)
        Get
            Return _bindings.AsReadOnly()
        End Get
    End Property

    ''' <summary>Start the capture timer.</summary>
    Public Sub StartCapture()
        _timer.Start()
    End Sub

    ''' <summary>Stop the capture timer.</summary>
    Public Sub StopCapture()
        _timer.Stop()
    End Sub

    ''' <summary>Change the capture interval in milliseconds.</summary>
    Public Sub SetInterval(ms As Integer)
        _timer.Interval = Math.Max(50, ms)
    End Sub

    Private Sub OnTick(sender As Object, e As EventArgs)
        For Each b In _bindings
            b.Refresh()
        Next
        RaiseEvent Refreshed(Me, EventArgs.Empty)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _timer.Stop()
        _timer.Dispose()
    End Sub

End Class
