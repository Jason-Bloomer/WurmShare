Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Custom container for the four self-bar capture overlays. Lays out four
''' fixed-height PictureBox sub-controls in a strict top-to-bottom order:
'''
'''   ┌─────────────────────────────────────┐
'''   │ Health / Stamina   (13 px, full)    │
'''   ├─────────────────────────────────────┤
'''   │ Favor              (10 px, full)    │   ◄── optional
'''   ├──────────────────┬──────────────────┤
'''   │ Food   (8 px, ½) │ Water  (8 px, ½) │
'''   └──────────────────┴──────────────────┘
'''
''' Width is user-controllable; the children stretch to fill it (Food and
''' Water each get exactly half — Water absorbs the extra pixel on odd
''' widths so the boundary lands at floor(width / 2)). Height is locked to
''' whichever total the current Favor visibility requires — 31 px with
''' Favor shown, 21 px without — regardless of what the designer, layout
''' engine, or user code tries to set.
'''
''' Each child is exposed as a typed PictureBox property so the existing
''' BindHealthStaminaCapture / BindFavorCapture / BindWaterCapture /
''' BindFoodCapture functions continue to work unchanged.
''' </summary>
<DefaultProperty("FavorVisible")>
<ToolboxItem(True)>
Public Class CaptureSurfacePanel
    Inherits Panel

    ' Base / reference pixel heights for each bar. These are the heights the
    ' panel uses at its MINIMUM size (1.0x scale). When the user grows the
    ' panel on the Y-axis, each bar's actual height is scaled proportionally
    ' from these so the relative ratios that match the in-game UI are
    ' preserved at any size.
    Public Const BaseHealthStaminaHeight As Integer = 13
    Public Const BaseFavorHeight As Integer = 10
    Public Const BaseFoodWaterHeight As Integer = 8

    ' Resize-nub strip at the bottom-right. Fixed size at any panel scale -
    ' the nub stays the same physical hit target regardless of how tall the
    ' bars get above it.
    Public Const NubSize As Integer = 10
    Public Const MinimumPanelWidth As Integer = 30

    Private ReadOnly _hpStamBox As PictureBox
    Private ReadOnly _favorBox As PictureBox
    Private ReadOnly _foodBox As PictureBox
    Private ReadOnly _waterBox As PictureBox
    Private ReadOnly _resizeNub As Button

    Private _favorVisible As Boolean = True
    Private _suppressLayout As Boolean = False

    ' Drag-resize state. Tracking is done in screen coordinates so the
    ' calculation isn't disturbed by the control's own size changing
    ' mid-drag.
    Private _resizing As Boolean = False
    Private _resizeAnchorScreenX As Integer
    Private _resizeAnchorScreenY As Integer
    Private _resizeStartWidth As Integer
    Private _resizeStartHeight As Integer

    Public Sub New()
        Me.SuspendLayout()

        Me.DoubleBuffered = True
        Me.AutoScroll = False
        Me.Padding = Padding.Empty
        Me.Margin = Padding.Empty
        Me.BackColor = Color.Black

        ' Sub-control default colours are picked to match the underlying
        ' game bars during alignment — once positioned, the user will
        ' typically set them all to the form's TransparencyKey (e.g.
        ' Color.Magenta) so the overlay disappears at runtime.
        _hpStamBox = NewSubControl(Color.DarkRed)
        _favorBox = NewSubControl(Color.Purple)
        _foodBox = NewSubControl(Color.SaddleBrown)
        _waterBox = NewSubControl(Color.DarkBlue)

        ' Resize nub: a small, gray, intentionally-visible Button anchored
        ' to the lower-right corner of the container. Its BackColor is
        ' deliberately NOT a likely TransparencyKey value, so it stays
        ' visible even when the bar boxes are made transparent for runtime.
        _resizeNub = New Button()
        _resizeNub.Size = New Size(NubSize, NubSize)
        _resizeNub.Margin = Padding.Empty
        _resizeNub.Padding = Padding.Empty
        _resizeNub.FlatStyle = FlatStyle.Flat
        _resizeNub.FlatAppearance.BorderSize = 1
        _resizeNub.FlatAppearance.BorderColor = SystemColors.ControlDarkDark
        _resizeNub.BackColor = SystemColors.ControlDark
        _resizeNub.ForeColor = SystemColors.ControlLightLight
        _resizeNub.TabStop = False
        _resizeNub.Cursor = Cursors.SizeNWSE
        _resizeNub.Text = ""
        AddHandler _resizeNub.MouseDown, AddressOf OnResizeNubMouseDown
        AddHandler _resizeNub.MouseMove, AddressOf OnResizeNubMouseMove
        AddHandler _resizeNub.MouseUp, AddressOf OnResizeNubMouseUp

        Me.Controls.AddRange(New Control() {_hpStamBox, _favorBox, _foodBox,
                                            _waterBox, _resizeNub})

        ' Default size. Width is a starting guess; height is the minimum
        ' (sum of all base bar heights plus the nub strip). Both can be
        ' grown by the user via the nub or via code; SetBoundsCore enforces
        ' the minimums.
        Me.Size = New Size(200, MinimumPanelHeight)

        Me.ResumeLayout(False)
        ApplyLayout()
    End Sub

    Private Shared Function NewSubControl(defaultColour As Color) As PictureBox
        Dim p As New PictureBox()
        p.Margin = Padding.Empty
        p.Padding = Padding.Empty
        p.BorderStyle = BorderStyle.None
        p.SizeMode = PictureBoxSizeMode.Normal
        p.BackColor = defaultColour
        Return p
    End Function

    ' =========================================================================
    ' Sub-control accessors
    '
    ' Returned as PictureBox so the existing Bind*Capture(pb As PictureBox)
    ' signatures take them without change. Hidden from the designer property
    ' grid - the boxes are internal layout details, not configurable
    ' top-level properties.
    ' =========================================================================

    <Browsable(False)>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property HealthStaminaBox As PictureBox
        Get
            Return _hpStamBox
        End Get
    End Property

    <Browsable(False)>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property FavorBox As PictureBox
        Get
            Return _favorBox
        End Get
    End Property

    <Browsable(False)>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property FoodBox As PictureBox
        Get
            Return _foodBox
        End Get
    End Property

    <Browsable(False)>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property WaterBox As PictureBox
        Get
            Return _waterBox
        End Get
    End Property

    ' =========================================================================
    ' Favor visibility
    ' =========================================================================

    ''' <summary>
    ''' Show or hide the Favor capture box. When hidden, the Food/Water row
    ''' moves up to occupy its space; the panel's minimum height also drops
    ''' by <see cref="BaseFavorHeight"/>, and if the panel was at its old
    ''' minimum it shrinks to the new one automatically.
    ''' </summary>
    <Category("Behavior")>
    <DefaultValue(True)>
    <Description("Show / hide the Favor capture row. When hidden, Food/Water moves up.")>
    Public Property FavorVisible As Boolean
        Get
            Return _favorVisible
        End Get
        Set(value As Boolean)
            If _favorVisible = value Then Return
            _favorVisible = value
            _favorBox.Visible = value

            ' If the panel is now below the new minimum (e.g. toggling Favor
            ' ON while we're at the no-favor minimum), bump up to the new
            ' minimum. Otherwise keep the user's chosen size and let
            ' ApplyLayout rescale the proportions.
            Dim minH As Integer = MinimumPanelHeight
            If Me.Height < minH Then
                Me.Height = minH
            End If
            ApplyLayout()
        End Set
    End Property

    ' =========================================================================
    ' Size clamping
    '
    ' Width and height are both user-controllable. The clamp below prevents
    ' shrinking below the sum of base bar heights (so the layout never has
    ' to deal with bars at zero height); growing in either dimension is
    ' unrestricted. ApplyLayout takes care of distributing whatever height
    ' the user actually picked across the bars proportionally.
    ' =========================================================================

    ''' <summary>
    ''' Minimum total panel height for the current bar configuration. Equals
    ''' the sum of all visible base bar heights plus the nub strip — i.e.
    ''' the size at which every bar is at its 1.0x base height.
    ''' </summary>
    <Browsable(False)>
    Public ReadOnly Property MinimumPanelHeight As Integer
        Get
            Dim h As Integer = BaseHealthStaminaHeight + BaseFoodWaterHeight + NubSize
            If _favorVisible Then h += BaseFavorHeight
            Return h
        End Get
    End Property

    Protected Overrides Sub SetBoundsCore(x As Integer, y As Integer,
                                          width As Integer, height As Integer,
                                          specified As BoundsSpecified)
        Dim minH As Integer = MinimumPanelHeight
        If height < minH Then height = minH
        If width < MinimumPanelWidth Then width = MinimumPanelWidth
        MyBase.SetBoundsCore(x, y, width, height, specified)
    End Sub

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Math.Max(proposedSize.Width, MinimumPanelWidth),
                        Math.Max(proposedSize.Height, MinimumPanelHeight))
    End Function

    ' =========================================================================
    ' Layout
    ' =========================================================================

    Protected Overrides Sub OnResize(eventargs As EventArgs)
        MyBase.OnResize(eventargs)
        ApplyLayout()
    End Sub

    ''' <summary>
    ''' Place each child at its pixel-exact position for the current size and
    ''' favor visibility. Bar heights are scaled proportionally from the base
    ''' constants (<see cref="BaseHealthStaminaHeight"/> etc.) so the relative
    ''' ratios that match the in-game UI are preserved at any panel height.
    ''' The nub strip stays a fixed <see cref="NubSize"/> tall regardless of
    ''' scale, so the resize handle remains the same physical hit-target.
    ''' Re-entrant calls are suppressed so SuspendLayout / ResumeLayout
    ''' cycles inside child Bounds assignments don't recurse.
    ''' </summary>
    Private Sub ApplyLayout()
        If _suppressLayout Then Return

        Dim w As Integer = Me.ClientSize.Width
        Dim totalH As Integer = Me.ClientSize.Height
        If w < 0 Then w = 0
        If totalH < 0 Then totalH = 0

        _suppressLayout = True
        Me.SuspendLayout()
        Try
            ' Bars occupy everything except the fixed-size nub strip at the
            ' bottom. Each bar's height is base * scale, where scale is the
            ' ratio of available bar area to the sum of base bar heights.
            Dim barArea As Integer = Math.Max(0, totalH - NubSize)
            Dim baseBars As Integer = BaseHealthStaminaHeight + BaseFoodWaterHeight
            If _favorVisible Then baseBars += BaseFavorHeight
            Dim scale As Double = If(baseBars > 0, barArea / CDbl(baseBars), 0.0)

            Dim hpStamH As Integer = CInt(Math.Round(BaseHealthStaminaHeight * scale))
            Dim favorH As Integer = If(_favorVisible,
                                       CInt(Math.Round(BaseFavorHeight * scale)),
                                       0)
            ' Food/Water row absorbs any rounding error so the three bar
            ' heights add up to barArea exactly and the layout has no gaps.
            Dim foodWaterH As Integer = barArea - hpStamH - favorH
            If foodWaterH < 0 Then foodWaterH = 0

            ' Row 1 — Health / Stamina, full width.
            _hpStamBox.Bounds = New Rectangle(0, 0, w, hpStamH)
            Dim y As Integer = hpStamH

            ' Row 2 — Favor, full width, only when visible. When hidden the
            ' box is collapsed to an empty rectangle so its Bounds report
            ' something innocuous rather than stale coordinates, AND so the
            ' Width/Height > 0 check in the capture sampler trips and the
            ' favor sample is cleanly skipped.
            If _favorVisible Then
                _favorBox.Bounds = New Rectangle(0, y, w, favorH)
                y += favorH
            Else
                _favorBox.Bounds = Rectangle.Empty
            End If

            ' Row 3 — Water (left, blue in-game) + Food (right, green/orange).
            ' On odd width Food gets the extra pixel so the boundary lands at
            ' floor(w / 2).
            Dim half As Integer = w \ 2
            _waterBox.Bounds = New Rectangle(0, y, half, foodWaterH)
            _foodBox.Bounds = New Rectangle(half, y, w - half, foodWaterH)
            y += foodWaterH

            ' Nub strip — resize handle anchored at the bottom-right corner.
            ' Fixed NubSize x NubSize regardless of panel scale.
            Dim nubX As Integer = Math.Max(0, w - NubSize)
            _resizeNub.Bounds = New Rectangle(nubX, y, NubSize, NubSize)
        Finally
            Me.ResumeLayout(False)
            _suppressLayout = False
        End Try
    End Sub

    ' =========================================================================
    ' Resize-nub drag handling
    '
    ' MouseDown captures the screen X+Y of the cursor and the container's
    ' current width and height, then sets Capture = True on the nub so
    ' subsequent MouseMove events keep flowing even when the cursor leaves
    ' the nub's footprint after the panel resizes underneath it. MouseMove
    ' computes the screen-space deltas and updates Me.Size — SetBoundsCore
    ' enforces the minimums on both axes. MouseUp releases the capture.
    '
    ' Tracking in SCREEN coordinates is important: every Size update moves
    ' the nub (it's anchored to the bottom-right corner), so a relative
    ' tracking would compound on itself.
    ' =========================================================================

    Private Sub OnResizeNubMouseDown(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Return
        _resizing = True
        _resizeAnchorScreenX = Control.MousePosition.X
        _resizeAnchorScreenY = Control.MousePosition.Y
        _resizeStartWidth = Me.Width
        _resizeStartHeight = Me.Height
        _resizeNub.Capture = True
    End Sub

    Private Sub OnResizeNubMouseMove(sender As Object, e As MouseEventArgs)
        If Not _resizing Then Return
        Dim dx As Integer = Control.MousePosition.X - _resizeAnchorScreenX
        Dim dy As Integer = Control.MousePosition.Y - _resizeAnchorScreenY
        Dim newWidth As Integer = _resizeStartWidth + dx
        Dim newHeight As Integer = _resizeStartHeight + dy
        ' SetBoundsCore clamps both axes to their minimums, but we pre-clamp
        ' so a no-op Size assignment isn't issued every mouse-move during a
        ' shrink-past-minimum drag.
        If newWidth < MinimumPanelWidth Then newWidth = MinimumPanelWidth
        Dim minH As Integer = MinimumPanelHeight
        If newHeight < minH Then newHeight = minH
        If newWidth <> Me.Width OrElse newHeight <> Me.Height Then
            Me.Size = New Size(newWidth, newHeight)
        End If
    End Sub

    Private Sub OnResizeNubMouseUp(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Return
        _resizing = False
        _resizeNub.Capture = False
    End Sub

End Class
