Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' A self-contained, owner-drawn UserControl that displays one party member's
''' Health/Stamina, Water, Food, Action, Numpad and Cooldown widgets.
''' All rendering is pure GDI+ – no child controls are used for the bars.
''' </summary>
Public Class PartySubPanel
    Inherits UserControl

#Region "Constants / Colours"

    ' Background
    Private Shared ReadOnly ColourBackground As Color = Color.FromArgb(30, 30, 35)
    Private Shared ReadOnly ColourBorder As Color = Color.FromArgb(80, 80, 90)
    Private Shared ReadOnly ColourNameText As Color = Color.FromArgb(220, 220, 220)

    ' Highlight Colors
    Private Shared ReadOnly ColourHighlightBackground As Color = Color.FromArgb(80, 30, 30)
    Private Shared ReadOnly ColourHighlightBorder As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly ColourHighlightNameText As Color = Color.FromArgb(255, 120, 120)
    ' ---Stunned---
    Private Shared ReadOnly ColourHighlightStunnedBackground As Color = Color.FromArgb(80, 80, 30)
    Private Shared ReadOnly ColourHighlightStunnedBorder As Color = Color.FromArgb(220, 220, 80)
    Private Shared ReadOnly ColourHighlightStunnedNameText As Color = Color.FromArgb(255, 255, 120)
    ' ---Encumbered---
    Private Shared ReadOnly ColourHighlightEncumberedBackground As Color = Color.FromArgb(30, 80, 30)
    Private Shared ReadOnly ColourHighlightEncumberedBorder As Color = Color.FromArgb(80, 220, 80)
    Private Shared ReadOnly ColourHighlightEncumberedNameText As Color = Color.FromArgb(120, 255, 120)

    ' Health / Stamina bar
    Private Shared ReadOnly ColourHealthFill As Color = Color.FromArgb(180, 60, 60)
    Private Shared ReadOnly ColourHealthEmpty As Color = Color.FromArgb(40, 40, 40)
    Private Shared ReadOnly ColourStaminaFill As Color = Color.FromArgb(60, 180, 90)

    ' Water / Food bars
    Private Shared ReadOnly ColourWater As Color = Color.FromArgb(60, 140, 220)
    Private Shared ReadOnly ColourFood As Color = Color.FromArgb(190, 130, 60)
    Private Shared ReadOnly ColourSustEmptyBg As Color = Color.FromArgb(35, 35, 35)

    ' Action bar
    Private Shared ReadOnly ColourFavor As Color = Color.FromArgb(160, 100, 220)
    Private Shared ReadOnly ColourFavorBg As Color = Color.FromArgb(35, 35, 35)

    ' Numpad grid
    Private Shared ReadOnly ColourNumpadBg As Color = Color.FromArgb(40, 40, 50)
    Private Shared ReadOnly ColourNumpadBorder As Color = Color.FromArgb(70, 70, 80)
    Private Shared ReadOnly ColourNumpadText As Color = Color.FromArgb(200, 200, 200)

    ' Combat stance cells (highlight colour by 1-based position, left→right)
    Private Shared ReadOnly ColourStance As Color() = {
        Color.FromArgb(210, 60, 60),     ' 1 = Red
        Color.FromArgb(220, 200, 70),    ' 2 = Yellow
        Color.FromArgb(70, 130, 220)     ' 3 = Blue
    }

    ' Shelter direction bars (lit state)
    Private Shared ReadOnly ColourShelterOn As Color = Color.FromArgb(195, 195, 205)

    ' Cooldown
    Private Shared ReadOnly ColourCooldownFill As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly ColourCooldownBg As Color = Color.FromArgb(35, 35, 35)

    ' Layout
    Private Const Padding As Integer = 6
    Private Const BarHeight As Integer = 19
    Private Const ThinBarHeight As Integer = 8
    Private Const ActionBarHeight As Integer = 8
    Private Const CooldownBarHeight As Integer = 8
    Private Const FavorBarHeight As Integer = 10
    Private Const NameHeight As Integer = 16
    Private Const NumpadCellSize As Integer = 18
    Private Const RowGap As Integer = 2
    Private Const CornerRadius As Integer = 2

    ' Combat stance: 3 cells, half a numpad cell wide, full cell height.
    Private Const StanceCellW As Integer = NumpadCellSize \ 2
    Private Const StanceGap As Integer = 3          ' space between stance block and numpad

    ' Shelter bars: thin frame around the numpad.
    Private Const ShelterBarThickness As Integer = 4
    Private Const ShelterBarGap As Integer = 0      ' space between bar and numpad edge

#End Region

#Region "Fields"

    Private _data As PartyMemberData
    Private _font As Font
    Private _smallFont As Font
    Private _tinyFont As Font

#End Region

#Region "Constructor / Init"

    Public Sub New()
        Me.DoubleBuffered = True
        Me.ResizeRedraw = True
        Me.BackColor = ColourBackground

        _data = New PartyMemberData()
        _font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        _smallFont = New Font("Segoe UI", 7.5F, FontStyle.Regular)
        _tinyFont = New Font("Segoe UI", 6.5F, FontStyle.Regular)
    End Sub

    ''' <summary>Bind a data object and trigger a repaint.</summary>
    Public Sub SetData(data As PartyMemberData)
        _data = data
        Me.Invalidate()
    End Sub

    Public ReadOnly Property Data As PartyMemberData
        Get
            Return _data
        End Get
    End Property

#End Region

#Region "Layout Helpers"

    ''' <summary>
    ''' Calculates the preferred height based on which widgets are visible.
    ''' Call this (or <see cref="AutoResizeHeight"/>) after changing any
    ''' height-affecting flag - ActionBarVisible, FavorEnabled - so the
    ''' panel reflows before the next paint.
    ''' </summary>
    Public Function PreferredHeight() As Integer
        Dim h As Integer = Padding
        h += NameHeight + RowGap               ' player name
        h += BarHeight + RowGap                ' health/stamina
        If _data.FavorEnabled = True Then
            h += FavorBarHeight + RowGap            ' favor
        End If
        h += ThinBarHeight + RowGap            ' water/food
        h += CooldownBarHeight + RowGap        ' cooldown
        h += Padding
        Return h
    End Function

    ''' <summary>Recalculate and apply preferred height.</summary>
    Public Sub AutoResizeHeight()
        Me.Height = PreferredHeight()
    End Sub

#End Region

#Region "Paint"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        Dim cursor As Integer = Padding
        Dim contentW As Integer = Me.Width - Padding * 2

        Dim DrawColorBackground As Color
        Dim DrawColorBorder As Color
        Dim DrawColorNameText As Color
        If _data.IsInCombat = True Then
            DrawColorBackground = ColourHighlightBackground
            DrawColorBorder = ColourHighlightBorder
            DrawColorNameText = ColourHighlightNameText
        ElseIf _data.IsStunned = True Then
            DrawColorBackground = ColourHighlightStunnedBackground
            DrawColorBorder = ColourHighlightStunnedBorder
            DrawColorNameText = ColourHighlightStunnedNameText
        ElseIf _data.IsEncumbered = True Then
            DrawColorBackground = ColourHighlightEncumberedBackground
            DrawColorBorder = ColourHighlightEncumberedBorder
            DrawColorNameText = ColourHighlightEncumberedNameText
        Else
            DrawColorBackground = ColourBackground
            DrawColorBorder = ColourBorder
            DrawColorNameText = ColourNameText
        End If

        Me.BackColor = DrawColorBackground
        ' Panel border
        Using borderPen As New Pen(DrawColorBorder, 1)
            Dim rect As New Rectangle(0, 0, Me.Width - 1, Me.Height - 1)
            DrawRoundRect(g, borderPen, rect, CornerRadius)
        End Using

        ' ── Player Name ───────────────────────────────────────────────────────
        Dim nameRect As New Rectangle(Padding, cursor, contentW, NameHeight)
        Using nameBrush As New SolidBrush(DrawColorNameText)
            g.DrawString(_data.PlayerName, _font, nameBrush, nameRect, StringFormat.GenericDefault)
        End Using

        cursor += NameHeight + RowGap

        Dim MarginRight As Integer = (NumpadCellSize + 2) * 3

        ' ── Health / Stamina bar ─────────────────────────────────────────────
        DrawHealthStaminaBar(g, Padding, cursor, (contentW - MarginRight), BarHeight)
        cursor += BarHeight + RowGap

        ' ── Favor bar (only when this member is tracking favor) ──────────────
        If _data.FavorEnabled = True Then
            DrawFavorBar(g, Padding, cursor, (contentW - MarginRight), FavorBarHeight)
            cursor += FavorBarHeight + RowGap
        End If

        ' ── Water / Food bar (split) ─────────────────────────────────────────
        DrawWaterFoodBar(g, Padding, cursor, (contentW - MarginRight), ThinBarHeight)
        cursor += ThinBarHeight + RowGap

        DrawCooldownBar(g, Padding, cursor, (contentW - MarginRight), CooldownBarHeight)
        cursor += CooldownBarHeight + RowGap

        ' ── Numpad + surrounding indicators ──────────────────────────────────
        Dim numpadW As Integer = NumpadCellSize * 3
        Dim numpadH As Integer = NumpadCellSize * 3
        Dim numpadX As Integer = (contentW - MarginRight) + 12
        Dim numpadY As Integer = Padding + 2

        ' Combat-stance row: 3 half-width cells left of the numpad, in the band
        ' above the HP% text and to the right of the player name.
        DrawCombatStance(g, numpadX, numpadY)

        ' Shelter-direction bars framing the numpad on all four sides.
        DrawShelterBars(g, numpadX, numpadY, numpadW, numpadH)

        DrawNumpadGrid(g, numpadX, numpadY, numpadW, numpadH)
    End Sub

#End Region

#Region "Bar Drawing Routines"

    Private Sub DrawHealthStaminaBar(g As Graphics, x As Integer, y As Integer,
                                 w As Integer, h As Integer)
        ' Calculate pixel boundaries
        Dim damageX As Integer = CInt(w * Clamp01(_data.HealthPercent))
        Dim stamRatio As Double = If(_data.HealthPercent > 0, Clamp01(_data.StaminaPercent / _data.HealthPercent), 0.0)
        Dim stamW As Integer = CInt(damageX * stamRatio)

        ' 1. Full bar background (stamina-depleted zone)
        FillRoundRect(g, New SolidBrush(ColourHealthEmpty),
                  New Rectangle(x, y, w, h), CornerRadius)

        ' 2. Stamina fill (left portion, within healthy range)
        If stamW > 0 Then
            FillRoundRect(g, New SolidBrush(ColourStaminaFill),
                      New Rectangle(x, y, stamW, h), CornerRadius)
        End If

        ' 3. Damage zone (red, fills in from the right as health drops)
        Dim damageW As Integer = w - damageX
        If damageW > 0 Then
            FillRoundRect(g, New SolidBrush(ColourHealthFill),
                      New Rectangle(x + damageX, y, damageW, h), CornerRadius)
        End If

        ' Border
        Using pen As New Pen(ColourBorder, 1)
            DrawRoundRect(g, pen, New Rectangle(x, y, w - 1, h - 1), CornerRadius)
        End Using

        ' Labels
        Using textBrush As New SolidBrush(Color.White)
            Dim sf As New StringFormat() With {.Alignment = StringAlignment.Near,
                                           .LineAlignment = StringAlignment.Center}
            g.DrawString($"STA {_data.StaminaPercent:P0}", _tinyFont, textBrush,
                     New RectangleF(x + 2, y, w / 2, h), sf)
            sf.Alignment = StringAlignment.Far
            g.DrawString($"HP {_data.HealthPercent:P0}", _tinyFont, textBrush,
                     New RectangleF(x, y, w - 2, h), sf)
        End Using
    End Sub

    Private Sub DrawWaterFoodBar(g As Graphics, x As Integer, y As Integer,
                                 w As Integer, h As Integer)
        Dim halfW As Integer = (w - 1) \ 2    ' 1px gap between halves

        ' Water (left half)
        Dim waterRect As New Rectangle(x, y, halfW, h)
        FillRoundRect(g, New SolidBrush(ColourSustEmptyBg), waterRect, CornerRadius)
        Dim waterFillW As Integer = CInt(halfW * Clamp01(_data.WaterPercent))
        If waterFillW > 0 Then
            FillRoundRect(g, New SolidBrush(ColourWater),
                          New Rectangle(x, y, waterFillW, h), CornerRadius)
        End If
        Using pen As New Pen(ColourBorder, 1)
            DrawRoundRect(g, pen, New Rectangle(x, y, halfW - 1, h - 1), CornerRadius)
        End Using

        ' Water label
        Using tb As New SolidBrush(Color.White)
            Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center,
                                               .LineAlignment = StringAlignment.Center}
            g.DrawString($"W {_data.WaterPercent:P0}", _tinyFont, tb,
                         New RectangleF(x, y, halfW, h), sf)
        End Using

        ' Food (right half)
        Dim foodX As Integer = x + halfW + 1
        Dim foodW As Integer = w - halfW - 1
        Dim foodRect As New Rectangle(foodX, y, foodW, h)
        FillRoundRect(g, New SolidBrush(ColourSustEmptyBg), foodRect, CornerRadius)
        Dim foodFillW As Integer = CInt(foodW * Clamp01(_data.FoodPercent))
        If foodFillW > 0 Then
            FillRoundRect(g, New SolidBrush(ColourFood),
                          New Rectangle(foodX, y, foodFillW, h), CornerRadius)
        End If
        Using pen As New Pen(ColourBorder, 1)
            DrawRoundRect(g, pen, New Rectangle(foodX, y, foodW - 1, h - 1), CornerRadius)
        End Using

        ' Food label
        Using tb As New SolidBrush(Color.White)
            Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center,
                                               .LineAlignment = StringAlignment.Center}
            g.DrawString($"F {_data.FoodPercent:P0}", _tinyFont, tb,
                         New RectangleF(foodX, y, foodW, h), sf)
        End Using
    End Sub

    Private Sub DrawFavorBar(g As Graphics, x As Integer, y As Integer,
                              w As Integer, h As Integer)
        ' Background
        FillRoundRect(g, New SolidBrush(ColourFavorBg),
                      New Rectangle(x, y, w, h), CornerRadius)

        ' Fill
        Dim fillW As Integer = CInt(w * Clamp01(_data.FavorPercent))
        If fillW > 0 Then
            FillRoundRect(g, New SolidBrush(ColourFavor),
                          New Rectangle(x, y, fillW, h), CornerRadius)
        End If

        ' Border
        Using pen As New Pen(ColourBorder, 1)
            DrawRoundRect(g, pen, New Rectangle(x, y, w - 1, h - 1), CornerRadius)
        End Using

        ' Label
        Using tb As New SolidBrush(Color.White)
            Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center,
                                               .LineAlignment = StringAlignment.Center}
            g.DrawString($"FAVOR  {_data.FavorPercent:P0}",
                         _tinyFont, tb, New RectangleF(x, y, w, h), sf)
        End Using
    End Sub

    ''' <summary>
    ''' Draws a 3×3 numpad-layout grid.
    ''' Cell positions match numpad keys: row 0 = keys 7,8,9  row 2 = keys 1,2,3.
    ''' Internal data index mapping (numpad key number → array index 0-8):
    '''   key 7→6, 8→7, 9→8  (top row)
    '''   key 4→3, 5→4, 6→5  (mid row)
    '''   key 1→0, 2→1, 3→2  (bot row)
    ''' </summary>
    Private Sub DrawNumpadGrid(g As Graphics, x As Integer, y As Integer,
                           w As Integer, h As Integer)
        Dim cellW As Integer = w \ 3
        Dim cellH As Integer = h \ 3

        ' Maps display position (top-left to bottom-right) to numpad key number
        Dim displayOrder As Integer() = {7, 8, 9, 4, 5, 6, 1, 2, 3}

        Dim selectedIdx As Integer = NumpadKeyToIndex(_data.SelectedNumpadKey)

        For i As Integer = 0 To 8
            Dim col As Integer = i Mod 3
            Dim row As Integer = i \ 3
            Dim cx As Integer = x + col * cellW
            Dim cy As Integer = y + row * cellH

            Dim cellRect As New Rectangle(cx, cy, cellW - 1, cellH - 1)

            ' Highlight selected cell green, all others use default background
            Dim isSelected As Boolean = (NumpadKeyToIndex(displayOrder(i)) = selectedIdx AndAlso
                                     _data.SelectedNumpadKey <> -1)
            Dim fillColour As Color = If(isSelected,
                                     Color.FromArgb(60, 200, 80),
                                     ColourNumpadBg)

            FillRoundRect(g, New SolidBrush(fillColour), cellRect, 2)

            Using pen As New Pen(ColourNumpadBorder, 1)
                DrawRoundRect(g, pen, cellRect, 2)
            End Using
        Next
    End Sub

    ''' <summary>
    ''' Draws the combat-stance indicator: a 3-across row of half-width cells
    ''' sitting just left of the numpad's top-left cell. CombatStance is the
    ''' 1-based index of the single highlighted cell (0 = none). Highlight
    ''' colour is fixed per position: 1=Red, 2=Yellow, 3=Blue.
    ''' </summary>
    Private Sub DrawCombatStance(g As Graphics, numpadX As Integer, numpadY As Integer)
        Dim cellW As Integer = StanceCellW
        Dim cellH As Integer = NumpadCellSize / 2          ' same height as a numpad cell
        Dim totalW As Integer = cellW * 3

        ' Right edge sits clear of the numpad's left shelter bar, then the block
        ' extends leftward into the empty space beside the name.
        Dim startX As Integer = numpadX - ShelterBarGap - ShelterBarThickness - StanceGap - totalW
        Dim y As Integer = numpadY

        For i As Integer = 0 To 2
            Dim cx As Integer = startX + i * cellW
            Dim cellRect As New Rectangle(cx, y, cellW - 1, cellH - 1)

            ' Exactly one cell lit, matching the 1-based stance value.
            Dim isOn As Boolean = (_data.CombatStance = i + 1)
            Dim fill As Color = If(isOn, ColourStance(i), ColourNumpadBg)

            FillRoundRect(g, New SolidBrush(fill), cellRect, 2)
            Using pen As New Pen(ColourNumpadBorder, 1)
                DrawRoundRect(g, pen, cellRect, 2)
            End Using
        Next
    End Sub

    ''' <summary>
    ''' Draws the four shelter-direction bars framing the numpad. Exactly one
    ''' is lit based on ShelterDirection (0 = none). Mapping:
    '''   1 = top, 2 = right, 3 = bottom, 4 = left.
    ''' </summary>
    Private Sub DrawShelterBars(g As Graphics, numpadX As Integer, numpadY As Integer, numpadW As Integer, numpadH As Integer)
        Dim t As Integer = ShelterBarThickness
        Dim gap As Integer = ShelterBarGap

        Dim topRect As New Rectangle(numpadX, numpadY - gap - t, numpadW, t)
        Dim rightRect As New Rectangle(numpadX + numpadW + gap, numpadY, t, numpadH)
        Dim bottomRect As New Rectangle(numpadX, numpadY + numpadH + gap, numpadW, t)
        Dim leftRect As New Rectangle(numpadX - gap - t, numpadY, t, numpadH)

        DrawShelterBar(g, topRect, _data.ShelterDirection = 1)
        DrawShelterBar(g, rightRect, _data.ShelterDirection = 2)
        DrawShelterBar(g, bottomRect, _data.ShelterDirection = 3)
        DrawShelterBar(g, leftRect, _data.ShelterDirection = 4)
    End Sub

    Private Sub DrawShelterBar(g As Graphics, r As Rectangle, isOn As Boolean)
        ' Axis-aligned rectangles - plain fill keeps thin bars crisp.
        Dim fill As Color = If(isOn, ColourShelterOn, ColourNumpadBg)
        Using b As New SolidBrush(fill)
            g.FillRectangle(b, r)
        End Using
        Using pen As New Pen(ColourNumpadBorder, 1)
            g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1)
        End Using
    End Sub

    Private Sub DrawCooldownBar(g As Graphics, x As Integer, y As Integer,
                                w As Integer, h As Integer)
        ' Background
        FillRoundRect(g, New SolidBrush(ColourCooldownBg),
                      New Rectangle(x, y, w, h), CornerRadius)

        If _data.CooldownActive AndAlso _data.CooldownTotalMs > 0 Then
            ' Fill from right to left (draining)
            Dim fraction As Double = Clamp01(CDbl(_data.CooldownRemainingMs) / _data.CooldownTotalMs)
            Dim fillW As Integer = CInt(w * fraction)
            If fillW > 0 Then
                FillRoundRect(g, New SolidBrush(ColourCooldownFill),
                              New Rectangle(x, y, fillW, h), CornerRadius)
            End If

            ' Remaining time label
            Dim secRemaining As Double = _data.CooldownRemainingMs / 1000.0
            Dim label As String = $"CD {secRemaining:0.0}s"
            Using tb As New SolidBrush(Color.White)
                Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center,
                                                   .LineAlignment = StringAlignment.Center}
                g.DrawString(label, _tinyFont, tb,
                             New RectangleF(x, y, w, h), sf)
            End Using
        Else
            Using tb As New SolidBrush(Color.FromArgb(100, 200, 200, 200))
                Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center,
                                                   .LineAlignment = StringAlignment.Center}
                g.DrawString("CD ready", _tinyFont, tb,
                             New RectangleF(x, y, w, h), sf)
            End Using
        End If

        Using pen As New Pen(ColourBorder, 1)
            DrawRoundRect(g, pen, New Rectangle(x, y, w - 1, h - 1), CornerRadius)
        End Using
    End Sub

#End Region

#Region "GDI+ Helper Methods"

    Private Shared Sub DrawRoundRect(g As Graphics, pen As Pen, r As Rectangle, radius As Integer)
        Using path As GraphicsPath = BuildRoundRectPath(r, radius)
            g.DrawPath(pen, path)
        End Using
    End Sub

    Private Shared Sub FillRoundRect(g As Graphics, brush As Brush, r As Rectangle, radius As Integer)
        Using path As GraphicsPath = BuildRoundRectPath(r, radius)
            g.FillPath(brush, path)
        End Using
    End Sub

    Private Shared Function BuildRoundRectPath(r As Rectangle, radius As Integer) As GraphicsPath
        Dim d As Integer = radius * 2
        Dim path As New GraphicsPath()
        path.AddArc(r.X, r.Y, d, d, 180, 90)
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Shared Function Clamp01(v As Double) As Double
        Return Math.Max(0.0, Math.Min(1.0, v))
    End Function

#End Region

#Region "Public API – Data Mutation"

    ' ── Health / Stamina ──────────────────────────────────────────────────────

    ''' <summary>Update health and stamina fractions (0.0–1.0) and repaint.</summary>
    Public Sub UpdateCombatStatus(flag As Boolean)
        _data.IsInCombat = flag
        Me.Invalidate()
    End Sub
    Public Sub UpdateStunnedStatus(flag As Boolean)
        _data.IsStunned = flag
        Me.Invalidate()
    End Sub
    Public Sub UpdateEncumberedStatus(flag As Boolean)
        _data.IsEncumbered = flag
        Me.Invalidate()
    End Sub

    ''' <summary>Update health and stamina fractions (0.0–1.0) and repaint.</summary>
    Public Sub UpdateHealthStamina(health As Double, stamina As Double)
        _data.HealthPercent = health
        _data.StaminaPercent = stamina
        Me.Invalidate()
    End Sub

    ' ── Water / Food ──────────────────────────────────────────────────────────

    Public Sub UpdateWater(waterFraction As Double)
        _data.WaterPercent = waterFraction
        Me.Invalidate()
    End Sub

    Public Sub UpdateFood(foodFraction As Double)
        _data.FoodPercent = foodFraction
        Me.Invalidate()
    End Sub

    Public Sub UpdateSustenance(water As Double, food As Double)
        _data.WaterPercent = water
        _data.FoodPercent = food
        Me.Invalidate()
    End Sub

    ' ── Favor bar ──-──────────────────────────────────────────────────────────
    Public Sub UpdateFavorPercent(percent As Double)
        _data.FavorPercent = percent
        Me.Invalidate()
    End Sub

    Public Sub UpdateFavorVisibility(visible As Boolean)
        ' If the visibility actually changes, the panel's preferred height does
        ' too - the favor row slot appears / disappears - so the panel must
        ' reflow before the next paint or the cooldown row (drawn after favor)
        ' gets pushed off the bottom edge.
        Dim changed As Boolean = (_data.FavorEnabled <> visible)
        _data.FavorEnabled = visible
        If changed Then AutoResizeHeight()
        Me.Invalidate()
    End Sub

    ' ── Action bar ────────────────────────────────────────────────────────────

    ''' <summary>Show the action bar with a label and initial progress.</summary>
    Public Sub ShowActionBar(label As String, Optional initialProgress As Double = 0.0)
        _data.ActionLabel = label
        _data.ActionPercent = initialProgress
        _data.ActionBarVisible = True
        AutoResizeHeight()
        Me.Invalidate()
    End Sub

    ''' <summary>Update the action bar's progress fraction (0.0–1.0).</summary>
    Public Sub UpdateActionProgress(progress As Double)
        _data.ActionPercent = progress
        Me.Invalidate()
    End Sub

    ''' <summary>Hide the action bar and collapse the panel.</summary>
    Public Sub HideActionBar()
        _data.ActionBarVisible = False
        AutoResizeHeight()
        Me.Invalidate()
    End Sub

    ' ── Numpad grid ───────────────────────────────────────────────────────────

    ''' <summary>
    ''' Set a specific numpad cell value.
    ''' numpadKey: 1–9 matching the numpad layout.
    ''' </summary>
    Public Sub SelectNumpadCell(numpadKey As Integer)
        _data.SelectedNumpadKey = numpadKey
        Me.Invalidate()
    End Sub

    ''' <summary>Read back which cell is currently selected (-1 if none).</summary>
    Public Function GetSelectedNumpadCell() As Integer
        Return _data.SelectedNumpadKey
    End Function

    ''' <summary>Maps numpad key number (1-9) to internal array index.</summary>
    Private Shared Function NumpadKeyToIndex(numpadKey As Integer) As Integer
        ' Numpad:  7→idx6  8→idx7  9→idx8
        '          4→idx3  5→idx4  6→idx5
        '          1→idx0  2→idx1  3→idx2
        Select Case numpadKey
            Case 1 : Return 0
            Case 2 : Return 1
            Case 3 : Return 2
            Case 4 : Return 3
            Case 5 : Return 4
            Case 6 : Return 5
            Case 7 : Return 6
            Case 8 : Return 7
            Case 9 : Return 8
            Case Else : Return -1
        End Select
    End Function

    ' ── Combat Stance ─────────────────────────────────────────────────────────

    ''' <summary>Set Combat Stance (0-3)</summary>
    Public Sub UpdateCombatStance(newStance As Integer)
        _data.CombatStance = newStance
        Me.Invalidate()
    End Sub

    ' ── Shelter Direction ─────────────────────────────────────────────────────

    ''' <summary>Set Combat Stance (0-3)</summary>
    Public Sub UpdateShelterDirection(newShelterDirection As Integer)
        _data.ShelterDirection = newShelterDirection
        Me.Invalidate()
    End Sub

    ' ── Cooldown ──────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Trigger the cooldown meter.
    ''' durationSeconds: how many seconds the CD takes to expire.
    ''' The caller is responsible for ticking via TickCooldown() on a Timer.
    ''' </summary>
    Public Sub TriggerCooldown(durationSeconds As Double)
        _data.CooldownTotalMs = CInt(durationSeconds * 1000)
        _data.CooldownRemainingMs = _data.CooldownTotalMs
        _data.CooldownActive = True
        Me.Invalidate()
    End Sub

    ''' <summary>
    ''' Advance the cooldown by elapsedMs milliseconds.
    ''' Returns True while the cooldown is still running, False when it expires.
    ''' Call this from your update timer.
    ''' </summary>
    Public Function TickCooldown(elapsedMs As Integer) As Boolean
        If Not _data.CooldownActive Then Return False
        _data.CooldownRemainingMs -= elapsedMs
        If _data.CooldownRemainingMs <= 0 Then
            _data.CooldownRemainingMs = 0
            _data.CooldownActive = False
            Me.Invalidate()
            Return False
        End If
        Me.Invalidate()
        Return True
    End Function

    ''' <summary>Immediately cancel the cooldown.</summary>
    Public Sub CancelCooldown()
        _data.CooldownActive = False
        _data.CooldownRemainingMs = 0
        Me.Invalidate()
    End Sub

#End Region

#Region "Dispose"

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _font?.Dispose()
            _smallFont?.Dispose()
            _tinyFont?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

#End Region

End Class
