Imports System.Windows.Forms
Imports System.Drawing

''' <summary>
''' A scrollable panel that manages a variable-width grid of PartySubPanel controls.
''' Panels reflow automatically when the container is resized or when members are added/removed.
''' </summary>
Public Class PartyGridPanel
    Inherits Panel

#Region "Fields"

    Private ReadOnly _panels As New List(Of PartySubPanel)()
    Private _columns As Integer = 4        ' Desired column count; adjusts dynamically
    Private _cellSpacing As Integer = 6    ' Gap between cells in pixels

#End Region

#Region "Constructor"

    Public Sub New()
        Me.AutoScroll = True
        Me.BackColor = Color.FromArgb(1, 22, 22, 28)
        Me.Padding = New Padding(6)
    End Sub

#End Region

#Region "Properties"

    ''' <summary>Number of columns in the grid (default 4). Set before populating.</summary>
    Public Property Columns As Integer
        Get
            Return _columns
        End Get
        Set(value As Integer)
            _columns = Math.Max(1, value)
            Reflow()
        End Set
    End Property

    ''' <summary>Pixel gap between sub-panels.</summary>
    Public Property CellSpacing As Integer
        Get
            Return _cellSpacing
        End Get
        Set(value As Integer)
            _cellSpacing = Math.Max(0, value)
            Reflow()
        End Set
    End Property

    ''' <summary>Read-only list of all hosted sub-panels.</summary>
    Public ReadOnly Property Panels As IReadOnlyList(Of PartySubPanel)
        Get
            Return _panels.AsReadOnly()
        End Get
    End Property

    ''' <summary>Count of active party members.</summary>
    Public ReadOnly Property MemberCount As Integer
        Get
            Return _panels.Count
        End Get
    End Property

#End Region

#Region "Member Management"

    ''' <summary>
    ''' Add a new party member sub-panel and return it so the caller can
    ''' configure its initial data.
    ''' </summary>
    Public Function AddMember(Optional playerName As String = "Player") As PartySubPanel
        Dim psp As New PartySubPanel()
        psp.Data.PlayerName = playerName
        psp.AutoResizeHeight()

        AddHandler psp.Resize, AddressOf OnSubPanelResize

        _panels.Add(psp)
        Me.Controls.Add(psp)
        Reflow()
        Return psp
    End Function

    ''' <summary>Remove the sub-panel at the given index.</summary>
    Public Sub RemoveMemberAt(index As Integer)
        If index < 0 OrElse index >= _panels.Count Then Return
        Dim psp As PartySubPanel = _panels(index)
        RemoveHandler psp.Resize, AddressOf OnSubPanelResize
        _panels.RemoveAt(index)
        Me.Controls.Remove(psp)
        psp.Dispose()
        Reflow()
    End Sub

    ''' <summary>Remove all party member panels.</summary>
    Public Sub ClearMembers()
        For Each psp In _panels
            RemoveHandler psp.Resize, AddressOf OnSubPanelResize
            Me.Controls.Remove(psp)
            psp.Dispose()
        Next
        _panels.Clear()
        Reflow()
    End Sub

    ''' <summary>
    ''' Replace all panels at once from a list of names.
    ''' Any existing panels are discarded.
    ''' </summary>
    Public Sub SetMembers(playerNames As IEnumerable(Of String))
        ClearMembers()
        For Each name As String In playerNames
            AddMember(name)
        Next
    End Sub

    ''' <summary>
    ''' Retrieve a specific member's sub-panel by index (0-based).
    ''' Returns Nothing if out of range.
    ''' </summary>
    Public Function GetPanel(index As Integer) As PartySubPanel
        If index < 0 OrElse index >= _panels.Count Then Return Nothing
        Return _panels(index)
    End Function

    ''' <summary>
    ''' Look up a panel by its player name (case-insensitive).
    ''' Returns Nothing if no panel currently holds that name.
    ''' Preferred over index-based lookup for network code, since indices
    ''' can shift between clients depending on join order.
    ''' </summary>
    Public Function GetPanelByName(name As String) As PartySubPanel
        If String.IsNullOrEmpty(name) Then Return Nothing
        For Each psp As PartySubPanel In _panels
            If psp.Data IsNot Nothing AndAlso
               String.Equals(psp.Data.PlayerName, name, StringComparison.OrdinalIgnoreCase) Then
                Return psp
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Get the 0-based index of the panel with the given player name, or -1 if not found.
    ''' </summary>
    Public Function IndexOfName(name As String) As Integer
        If String.IsNullOrEmpty(name) Then Return -1
        For i As Integer = 0 To _panels.Count - 1
            If _panels(i).Data IsNot Nothing AndAlso
               String.Equals(_panels(i).Data.PlayerName, name, StringComparison.OrdinalIgnoreCase) Then
                Return i
            End If
        Next
        Return -1
    End Function

    ''' <summary>
    ''' Look up a panel by name; if none exists, create one. Idempotent —
    ''' calling twice with the same name returns the same panel.
    ''' </summary>
    Public Function FindOrAddMember(name As String) As PartySubPanel
        Dim existing As PartySubPanel = GetPanelByName(name)
        If existing IsNot Nothing Then Return existing
        Return AddMember(name)
    End Function

    ''' <summary>
    ''' Remove the panel matching the given player name (case-insensitive).
    ''' Returns True if a panel was removed.
    ''' </summary>
    Public Function RemoveMemberByName(name As String) As Boolean
        Dim idx As Integer = IndexOfName(name)
        If idx < 0 Then Return False
        RemoveMemberAt(idx)
        Return True
    End Function

#End Region

#Region "Layout"

    ''' <summary>
    ''' Recalculate all sub-panel positions and sizes in a grid layout.
    ''' Called automatically when members are added/removed or the container resizes.
    ''' </summary>
    Public Sub Reflow()
        Dim visible As List(Of PartySubPanel) = _panels.Where(Function(p) p.Visible).ToList()
        If visible.Count = 0 Then Return
        If _panels.Count = 0 Then Return

        Dim availW As Integer = Me.ClientSize.Width - Me.Padding.Horizontal
        Dim cols As Integer = Math.Max(1, Math.Min(_columns, _panels.Count))
        Dim cellW As Integer = (availW - (_cellSpacing * (cols - 1))) \ cols

        Dim col As Integer = 0
        Dim row As Integer = 0
        Dim rowHeights As New List(Of Integer)()
        rowHeights.Add(0)

        ' First pass: determine row heights (each row is the tallest panel in it)
        Dim panelRows As New List(Of Integer)()   ' which row each panel is in
        For i As Integer = 0 To visible.Count - 1
            Dim r As Integer = i \ cols
            While rowHeights.Count <= r
                rowHeights.Add(0)
            End While
            rowHeights(r) = Math.Max(rowHeights(r), visible(i).PreferredHeight())
            panelRows.Add(r)
        Next

        ' Second pass: position panels
        Me.SuspendLayout()
        For i As Integer = 0 To visible.Count - 1
            Dim psp As PartySubPanel = visible(i)
            col = i Mod cols
            row = i \ cols

            Dim xPos As Integer = Me.Padding.Left + col * (cellW + _cellSpacing)
            Dim yPos As Integer = Me.Padding.Top
            For r As Integer = 0 To row - 1
                yPos += rowHeights(r) + _cellSpacing
            Next

            psp.Width = cellW
            psp.Height = rowHeights(row)
            psp.Location = New Point(xPos, yPos)
        Next
        Me.ResumeLayout(True)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        Reflow()
    End Sub

    Private Sub OnSubPanelResize(sender As Object, e As EventArgs)
        Reflow()
    End Sub

#End Region

End Class
