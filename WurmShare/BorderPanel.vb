Public Class BorderPanel
    Inherits Panel

    ' Your 8 border images — set these however you like (resources, properties, etc.)
    Public Property CornerTL As Image
    Public Property CornerTR As Image
    Public Property CornerBL As Image
    Public Property CornerBR As Image
    Public Property EdgeTop As Image
    Public Property EdgeBottom As Image
    Public Property EdgeLeft As Image
    Public Property EdgeRight As Image

    Public Sub New()
        ' Prevent flicker
        Me.DoubleBuffered = True
        Me.ResizeRedraw = True
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)

        Dim g As Graphics = e.Graphics
        Dim w As Integer = Me.Width
        Dim h As Integer = Me.Height

        ' Assumes all corner images are the same size
        Dim cw As Integer = If(CornerTL IsNot Nothing, CornerTL.Width, 0)
        Dim ch As Integer = If(CornerTL IsNot Nothing, CornerTL.Height, 0)


        ' --- Draw repeating edges ---
        ' Top and Bottom
        If EdgeTop IsNot Nothing OrElse EdgeBottom IsNot Nothing Then
            Dim x As Integer = cw
            Do While x < w - cw
                Dim drawWidth As Integer = Math.Min(EdgeTop.Width, w - cw - x)
                If EdgeTop IsNot Nothing Then
                    g.DrawImage(EdgeTop, New Rectangle(x - 8, 0, drawWidth + 16, ch), New Rectangle(0, 0, drawWidth, EdgeTop.Height), GraphicsUnit.Pixel)
                End If
                If EdgeBottom IsNot Nothing Then
                    g.DrawImage(EdgeBottom, New Rectangle(x - 8, h - ch, drawWidth + 16, ch), New Rectangle(0, 0, drawWidth, EdgeBottom.Height), GraphicsUnit.Pixel)
                End If
                x += EdgeTop.Width
            Loop
        End If

        ' Left and Right
        If EdgeLeft IsNot Nothing OrElse EdgeRight IsNot Nothing Then
            Dim y As Integer = ch
            Do While y < h - ch
                Dim drawHeight As Integer = Math.Min(EdgeLeft.Height, h - ch - y)
                If EdgeLeft IsNot Nothing Then
                    g.DrawImage(EdgeLeft, New Rectangle(0, y - 8, cw, drawHeight + 16), New Rectangle(0, 0, EdgeLeft.Width, drawHeight), GraphicsUnit.Pixel)
                End If
                If EdgeRight IsNot Nothing Then
                    g.DrawImage(EdgeRight, New Rectangle(w - cw, y - 8, cw, drawHeight + 16), New Rectangle(0, 0, EdgeRight.Width, drawHeight), GraphicsUnit.Pixel)
                End If
                y += EdgeLeft.Height
            Loop
        End If

        ' --- Draw corners ---
        If CornerTL IsNot Nothing Then g.DrawImage(CornerTL, 0, 0)
        If CornerTR IsNot Nothing Then g.DrawImage(CornerTR, w - cw, 0)
        If CornerBL IsNot Nothing Then g.DrawImage(CornerBL, 0, h - ch)
        If CornerBR IsNot Nothing Then g.DrawImage(CornerBR, w - cw, h - ch)
    End Sub

    Private Sub InitializeComponent()
        Me.SuspendLayout()
        Me.ResumeLayout(False)

    End Sub
End Class