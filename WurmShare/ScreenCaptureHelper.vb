Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Windows.Forms
Imports System.Runtime.InteropServices

''' <summary>
''' Captures a rectangular region of the screen and analyses pixel data to determine
''' how "filled" a progress bar is.  Works by comparing pixel luminance or colour
''' against a threshold, scanning left-to-right for the transition from filled to empty.
'''
''' Usage pattern
''' ─────────────
'''  1. Create a PictureBox and position it precisely over the target game's bar.
'''  2. Pass that PictureBox to CaptureUnderControl().
'''  3. Call AnalyseBarFill() on the resulting Bitmap.
'''  4. Repeat on a Timer (e.g. every 200 ms).
''' </summary>
Public Class ScreenCaptureHelper

#Region "P/Invoke"

    <DllImport("user32.dll")>
    Private Shared Function GetWindowRect(hWnd As IntPtr, ByRef rect As RECT) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

#End Region

#Region "Screen Capture"

    ''' <summary>
    ''' Captures the screen pixels that lie directly beneath the given control.
    ''' The control itself is NOT painted; we capture what is behind it on the desktop.
    ''' </summary>
    ''' <param name="ctrl">The PictureBox (or any control) whose screen area to capture.</param>
    ''' <returns>A new Bitmap of the captured region. Caller must dispose it.</returns>
    Public Shared Function CaptureUnderControl(ctrl As Control) As Bitmap
        Dim screenBounds As Rectangle = ctrl.RectangleToScreen(ctrl.ClientRectangle)
        Return CaptureRegion(screenBounds)
    End Function

    ''' <summary>
    ''' Captures an arbitrary screen rectangle.
    ''' </summary>
    Public Shared Function CaptureRegion(region As Rectangle) As Bitmap
        If region.Width <= 0 OrElse region.Height <= 0 Then
            Return New Bitmap(1, 1)
        End If

        Dim bmp As New Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.CopyFromScreen(region.Location, Point.Empty, region.Size)
        End Using
        Return bmp
    End Function

    ''' <summary>
    ''' Convenience: capture a region defined by absolute screen coordinates.
    ''' </summary>
    Public Shared Function CaptureRegion(x As Integer, y As Integer,
                                         width As Integer, height As Integer) As Bitmap
        Return CaptureRegion(New Rectangle(x, y, width, height))
    End Function

#End Region

#Region "Bar Analysis"

    ''' <summary>
    ''' RECOMMENDED default analyser. Determines a bar's fill fraction without
    ''' needing the fill colour up front. Classifies each pixel as "filled" if
    ''' it is NOT background — where background is any neutral colour whose
    ''' R/G/B channels are close together (dark browns, near-black, mid greys)
    ''' and "filled" is any saturated colour where one or two channels sit
    ''' significantly higher than the others (red, green, blue, orange,
    ''' yellow, etc.). The same threshold therefore works for every bar.
    '''
    ''' Why this is more robust than the colour-match variants for
    ''' gradient/sprite-rendered bars:
    '''   * The bar bevel makes the top and bottom rows deviate from the bar's
    '''     true colour, so single-row scans are noisy. Here we sample several
    '''     rows tightly clustered around the vertical centre and combine them
    '''     by per-column majority vote.
    '''   * Anti-aliased pixels at the fill→empty boundary can briefly fall
    '''     below threshold mid-fill. A small "noise tolerance" lets the run
    '''     survive a few-pixel gap; only a sustained empty stretch ends it.
    '''   * Because we look at channel spread rather than channel value, a
    '''     gradient that brightens or darkens the fill doesn't change the
    '''     classification.
    '''
    ''' Returns the rightmost X that is part of a contiguous fill run starting
    ''' at the left edge, divided by the bar width. Returns 0.0 for an empty
    ''' bar or unreadable bitmap.
    ''' </summary>
    ''' <param name="barBitmap">The captured bar bitmap.</param>
    ''' <param name="channelSpread">
    '''   Minimum <c>max(R,G,B) - min(R,G,B)</c> for a pixel to count as
    '''   filled. 50 is a good default for the standard "dark neutral
    '''   background, saturated coloured fill" UI bars. Lower for washed-out
    '''   fills, raise if the background itself has some tint.
    ''' </param>
    ''' <param name="sampleRows">
    '''   How many horizontal rows to sample around the bar's centre.
    '''   Must be at least 1. Odd numbers (1, 3, 5) give a clean majority.
    ''' </param>
    ''' <param name="noiseTolerance">
    '''   Consecutive empty-pixel run length allowed inside the fill before
    '''   it is considered ended (default 3). Set to 0 to disable.
    ''' </param>
    ''' <param name="absoluteFloor">
    '''   Minimum <c>max(R,G,B)</c> for a pixel to count as filled, applied
    '''   IN ADDITION to <paramref name="channelSpread"/>. Defaults to 0
    '''   (disabled), preserving the spread-only behaviour. Raise it for
    '''   desaturated bars whose fill spread overlaps the spread of dim
    '''   tinted background: the brightness floor rejects the dim background
    '''   while letting the brighter (but still low-spread) fill through.
    ''' </param>
    Public Shared Function AnalyseBarFill(barBitmap As Bitmap,
                                          Optional channelSpread As Integer = 50,
                                          Optional sampleRows As Integer = 3,
                                          Optional noiseTolerance As Integer = 3,
                                          Optional absoluteFloor As Integer = 0) As Double
        If barBitmap Is Nothing OrElse barBitmap.Width = 0 OrElse barBitmap.Height = 0 Then
            Return 0.0
        End If

        Dim w As Integer = barBitmap.Width
        Dim h As Integer = barBitmap.Height

        ' Build the list of rows to sample, tightly clustered around the
        ' bar's vertical centre. For sampleRows=3 on a 20-pixel-tall bar,
        ' this picks rows 9, 10, 11 — the truest-colour pixels.
        Dim n As Integer = Math.Max(1, sampleRows)
        Dim rows(n - 1) As Integer
        Dim mid As Integer = h \ 2
        For i As Integer = 0 To n - 1
            Dim rowY As Integer = mid + (i - (n - 1) \ 2)
            If rowY < 0 Then rowY = 0
            If rowY >= h Then rowY = h - 1
            rows(i) = rowY
        Next

        ' Per-column "is this filled?" mask, computed via LockBits for speed.
        Dim filledCol(w - 1) As Boolean
        Dim data As BitmapData = barBitmap.LockBits(
            New Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

        Try
            Dim stride As Integer = data.Stride
            Dim bytes((stride * h) - 1) As Byte
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

            Dim majority As Integer = (n \ 2) + 1
            For x As Integer = 0 To w - 1
                Dim votes As Integer = 0
                For Each rowY As Integer In rows
                    Dim off As Integer = rowY * stride + x * 4
                    ' 32bpp ARGB layout in memory is B, G, R, A.
                    Dim bch As Integer = bytes(off)
                    Dim gch As Integer = bytes(off + 1)
                    Dim rch As Integer = bytes(off + 2)
                    Dim mx As Integer = Math.Max(rch, Math.Max(gch, bch))
                    Dim mn As Integer = Math.Min(rch, Math.Min(gch, bch))
                    ' Filled = visibly coloured (spread) AND bright enough
                    ' (floor). The floor is what separates a desaturated fill
                    ' from a dim-but-tinted background of similar spread.
                    If (mx - mn) >= channelSpread AndAlso mx >= absoluteFloor Then votes += 1
                Next
                filledCol(x) = (votes >= majority)
            Next
        Finally
            barBitmap.UnlockBits(data)
        End Try

        ' Walk left → right tracking the rightmost X that belongs to a
        ' contiguous fill run starting at the left edge. Brief empty
        ' stretches (≤ noiseTolerance pixels) are tolerated so anti-aliased
        ' gaps inside the fill don't end the run prematurely; once a
        ' sustained empty stretch is hit, the fill is considered over.
        Dim fillExtent As Integer = 0
        Dim emptyStreak As Integer = 0
        For x As Integer = 0 To w - 1
            If filledCol(x) Then
                fillExtent = x + 1
                emptyStreak = 0
            Else
                emptyStreak += 1
                If emptyStreak > noiseTolerance AndAlso fillExtent > 0 Then
                    Exit For
                End If
            End If
        Next

        Return CDbl(fillExtent) / CDbl(w)
    End Function

    ''' <summary>
    ''' Result of analysing a compound HP/Stamina bar — two absolute
    ''' fractions of the full bar width, both in [0, 1].
    ''' </summary>
    Public Structure HpStamReading
        ''' <summary>Width of the green region growing from the left edge.</summary>
        Public Stamina As Double
        ''' <summary>1 - (width of the red region growing from the right edge).</summary>
        Public Health As Double
    End Structure

    ''' <summary>
    ''' Analyse a compound HP/Stamina bar that fills from BOTH ends with
    ''' different colours:
    '''   * Stamina is GREEN and grows rightward from the LEFT edge.
    '''   * Damage is RED and grows leftward from the RIGHT edge — its
    '''     extent equals (1 - HealthPercent).
    '''   * Background (dark neutral) fills any gap between the two.
    ''' This is the layout used by games where health caps stamina (a
    ''' 70%-health player can hold at most 70% stamina): up to three
    ''' regions appear — stamina | gap | damage. When stamina is at its
    ''' health-cap the gap closes and the bar reads fully filled, but the
    ''' green/red border may sit anywhere along it.
    '''
    ''' Returns both fractions from a single capture pass. Per-column
    ''' classification uses the same channel-spread test as
    ''' <see cref="AnalyseBarFill"/>:
    '''   * spread &lt; channelSpread  →  background
    '''   * G &gt; R AND G &gt; B           →  green (stamina)
    '''   * R &gt; G AND R &gt; B           →  red   (damage)
    '''   * anything else (e.g. yellow at the AA boundary) → ambiguous,
    '''     counts as neither colour for the run-length scan.
    ''' Multi-row majority voting and noise-tolerance are applied
    ''' independently to the green-from-left and red-from-right scans, so
    ''' a few-pixel artefact in one zone can't poison the other reading.
    '''
    ''' Both fields of the returned <see cref="HpStamReading"/> are
    ''' absolute fractions of the bar width and can be assigned directly
    ''' to <c>_selfData.StaminaPercent</c> / <c>_selfData.HealthPercent</c>.
    ''' </summary>
    Public Shared Function AnalyseHpStamBar(barBitmap As Bitmap,
                                            Optional channelSpread As Integer = 50,
                                            Optional sampleRows As Integer = 3,
                                            Optional noiseTolerance As Integer = 3,
                                            Optional absoluteFloor As Integer = 0) As HpStamReading
        Dim result As HpStamReading
        result.Stamina = 0.0
        result.Health = 1.0

        If barBitmap Is Nothing OrElse barBitmap.Width = 0 OrElse barBitmap.Height = 0 Then
            Return result
        End If

        Dim w As Integer = barBitmap.Width
        Dim h As Integer = barBitmap.Height

        ' Rows clustered around the bar's vertical centre — same logic as
        ' AnalyseBarFill, since the centre row is where a sprite-rendered
        ' bar's colour is least diluted by bevel / gradient.
        Dim n As Integer = Math.Max(1, sampleRows)
        Dim rows(n - 1) As Integer
        Dim mid As Integer = h \ 2
        For i As Integer = 0 To n - 1
            Dim rowY As Integer = mid + (i - (n - 1) \ 2)
            If rowY < 0 Then rowY = 0
            If rowY >= h Then rowY = h - 1
            rows(i) = rowY
        Next

        ' Per-column classification: 0 = neither (background or ambiguous),
        ' 1 = green, 2 = red. Stored as bytes because a column can only
        ' belong to one bucket.
        Const CLS_NEITHER As Byte = 0
        Const CLS_GREEN As Byte = 1
        Const CLS_RED As Byte = 2
        Dim cls(w - 1) As Byte

        Dim data As BitmapData = barBitmap.LockBits(
            New Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

        Try
            Dim stride As Integer = data.Stride
            Dim bytes((stride * h) - 1) As Byte
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

            Dim majority As Integer = (n \ 2) + 1
            For x As Integer = 0 To w - 1
                Dim greenVotes As Integer = 0
                Dim redVotes As Integer = 0
                For Each rowY As Integer In rows
                    Dim off As Integer = rowY * stride + x * 4
                    Dim bch As Integer = bytes(off)
                    Dim gch As Integer = bytes(off + 1)
                    Dim rch As Integer = bytes(off + 2)
                    Dim mx As Integer = Math.Max(rch, Math.Max(gch, bch))
                    Dim mn As Integer = Math.Min(rch, Math.Min(gch, bch))
                    If (mx - mn) >= channelSpread AndAlso mx >= absoluteFloor Then
                        ' Strict comparisons: a tie between two channels
                        ' (e.g. yellow at the AA boundary, R≈G≫B) votes
                        ' for neither colour, so the gap detection treats
                        ' the transition pixels as ambiguous.
                        If gch > rch AndAlso gch > bch Then
                            greenVotes += 1
                        ElseIf rch > gch AndAlso rch > bch Then
                            redVotes += 1
                        End If
                    End If
                Next
                If greenVotes >= majority Then
                    cls(x) = CLS_GREEN
                ElseIf redVotes >= majority Then
                    cls(x) = CLS_RED
                Else
                    cls(x) = CLS_NEITHER
                End If
            Next
        Finally
            barBitmap.UnlockBits(data)
        End Try

        ' Stamina: walk left → right, find rightmost column of a contiguous
        ' green run starting at the left edge. Brief gaps (≤ noiseTolerance
        ' pixels) don't end the run; a sustained non-green stretch does.
        Dim greenExtent As Integer = 0
        Dim nonGreenStreak As Integer = 0
        For x As Integer = 0 To w - 1
            If cls(x) = CLS_GREEN Then
                greenExtent = x + 1
                nonGreenStreak = 0
            Else
                nonGreenStreak += 1
                If nonGreenStreak > noiseTolerance AndAlso greenExtent > 0 Then
                    Exit For
                End If
            End If
        Next

        ' Damage: walk right → left, find leftmost column of a contiguous
        ' red run starting at the right edge. Symmetrical to the green scan.
        Dim redExtent As Integer = 0
        Dim nonRedStreak As Integer = 0
        For x As Integer = w - 1 To 0 Step -1
            If cls(x) = CLS_RED Then
                redExtent = w - x          ' counted from the right edge
                nonRedStreak = 0
            Else
                nonRedStreak += 1
                If nonRedStreak > noiseTolerance AndAlso redExtent > 0 Then
                    Exit For
                End If
            End If
        Next

        result.Stamina = CDbl(greenExtent) / CDbl(w)
        result.Health = 1.0 - (CDbl(redExtent) / CDbl(w))
        Return result
    End Function

    ''' <summary>
    ''' Determines the fill percentage of a horizontal progress bar in the given bitmap.
    ''' Scans a horizontal slice through the middle of the image left-to-right and
    ''' identifies the rightmost pixel that matches the "filled" colour.
    ''' </summary>
    ''' <param name="barBitmap">A bitmap of the bar region.</param>
    ''' <param name="filledColour">The expected colour of a "filled" pixel.</param>
    ''' <param name="colourTolerance">How much each RGB channel may differ (0-255).</param>
    ''' <param name="scanRow">
    '''   Which row (Y) to scan. -1 = middle row (default).
    '''   Pass a positive integer to scan a specific pixel row.
    ''' </param>
    ''' <returns>Fill fraction 0.0 – 1.0.</returns>
    Public Shared Function AnalyseBarFillByColour(barBitmap As Bitmap,
                                                   filledColour As Color,
                                                   Optional colourTolerance As Integer = 30,
                                                   Optional scanRow As Integer = -1) As Double
        If barBitmap Is Nothing OrElse barBitmap.Width = 0 OrElse barBitmap.Height = 0 Then
            Return 0.0
        End If

        Dim row As Integer = If(scanRow < 0, barBitmap.Height \ 2, scanRow)
        row = Math.Max(0, Math.Min(row, barBitmap.Height - 1))

        Dim lastFilledX As Integer = -1

        For x As Integer = 0 To barBitmap.Width - 1
            Dim px As Color = barBitmap.GetPixel(x, row)
            If ColourMatches(px, filledColour, colourTolerance) Then
                lastFilledX = x
            End If
        Next

        If lastFilledX < 0 Then Return 0.0
        Return CDbl(lastFilledX + 1) / CDbl(barBitmap.Width)
    End Function

    ''' <summary>
    ''' Determines the fill percentage by averaging multiple horizontal scan rows
    ''' across the bar, then taking the median result.  More robust than single-row.
    ''' </summary>
    Public Shared Function AnalyseBarFillMultiRow(barBitmap As Bitmap,
                                                   filledColour As Color,
                                                   Optional colourTolerance As Integer = 30,
                                                   Optional sampleRows As Integer = 5) As Double
        If barBitmap Is Nothing OrElse barBitmap.Height < 1 Then Return 0.0

        Dim results As New List(Of Double)()
        Dim byteStep As Double = If(barBitmap.Height <= 1, 0,
                                CDbl(barBitmap.Height - 1) / CDbl(Math.Max(1, sampleRows - 1)))

        For i As Integer = 0 To sampleRows - 1
            Dim rowY As Integer = Math.Min(CInt(Math.Round(i * byteStep)), barBitmap.Height - 1)
            results.Add(AnalyseBarFillByColour(barBitmap, filledColour, colourTolerance, rowY))
        Next

        results.Sort()
        ' Return median
        Return results(results.Count \ 2)
    End Function

    ''' <summary>
    ''' Alternative analysis that uses luminance contrast rather than a target colour.
    ''' Useful when you don't know the exact fill colour but know the filled pixels are
    ''' significantly brighter (or darker) than the empty ones.
    ''' </summary>
    ''' <param name="filledIsBright">
    '''   True if the "filled" section is the brighter half; False if it is darker.
    ''' </param>
    ''' <param name="luminanceThreshold">
    '''   Pixel luminance (0–255) above/below which a pixel counts as filled.
    ''' </param>
    Public Shared Function AnalyseBarFillByLuminance(barBitmap As Bitmap,
                                                      Optional filledIsBright As Boolean = True,
                                                      Optional luminanceThreshold As Integer = 128,
                                                      Optional scanRow As Integer = -1) As Double
        If barBitmap Is Nothing OrElse barBitmap.Width = 0 Then Return 0.0

        Dim row As Integer = If(scanRow < 0, barBitmap.Height \ 2, scanRow)
        row = Math.Max(0, Math.Min(row, barBitmap.Height - 1))

        Dim lastFilledX As Integer = -1

        For x As Integer = 0 To barBitmap.Width - 1
            Dim px As Color = barBitmap.GetPixel(x, row)
            Dim lum As Integer = CInt((px.R * 0.299) + (px.G * 0.587) + (px.B * 0.114))
            Dim isFilled As Boolean = If(filledIsBright, lum >= luminanceThreshold,
                                                         lum < luminanceThreshold)
            If isFilled Then lastFilledX = x
        Next

        If lastFilledX < 0 Then Return 0.0
        Return CDbl(lastFilledX + 1) / CDbl(barBitmap.Width)
    End Function

    ''' <summary>
    ''' Detects the dominant "filled" colour of a bar automatically by sampling the
    ''' leftmost pixels (assumed to be always filled when the bar has any value).
    ''' Useful for auto-calibration.
    ''' </summary>
    ''' <param name="sampleWidth">How many pixels from the left to sample.</param>
    Public Shared Function AutoDetectFilledColour(barBitmap As Bitmap,
                                                   Optional sampleWidth As Integer = 10,
                                                   Optional scanRow As Integer = -1) As Color
        If barBitmap Is Nothing Then Return Color.Empty

        Dim row As Integer = If(scanRow < 0, barBitmap.Height \ 2, scanRow)
        row = Math.Max(0, Math.Min(row, barBitmap.Height - 1))
        Dim sw As Integer = Math.Min(sampleWidth, barBitmap.Width)

        Dim rTotal As Long = 0, gTotal As Long = 0, bTotal As Long = 0
        For x As Integer = 0 To sw - 1
            Dim px As Color = barBitmap.GetPixel(x, row)
            rTotal += px.R
            gTotal += px.G
            bTotal += px.B
        Next
        Return Color.FromArgb(CInt(rTotal \ sw), CInt(gTotal \ sw), CInt(bTotal \ sw))
    End Function

    ''' <summary>Check if two colours are within tolerance on all three channels.</summary>
    Private Shared Function ColourMatches(a As Color, b As Color, tolerance As Integer) As Boolean
        Return Math.Abs(CInt(a.R) - b.R) <= tolerance AndAlso
               Math.Abs(CInt(a.G) - b.G) <= tolerance AndAlso
               Math.Abs(CInt(a.B) - b.B) <= tolerance
    End Function

#End Region

#Region "Fast Bitmap Access (LockBits)"

    ''' <summary>
    ''' High-performance version of AnalyseBarFillByColour using LockBits.
    ''' Use this when scanning many bars rapidly (e.g. every 100 ms with 20 bars).
    ''' </summary>
    Public Shared Function AnalyseBarFillFast(barBitmap As Bitmap,
                                               filledColour As Color,
                                               Optional colourTolerance As Integer = 30,
                                               Optional scanRow As Integer = -1) As Double
        If barBitmap Is Nothing OrElse barBitmap.Width = 0 OrElse barBitmap.Height = 0 Then
            Return 0.0
        End If

        Dim row As Integer = If(scanRow < 0, barBitmap.Height \ 2, scanRow)
        row = Math.Max(0, Math.Min(row, barBitmap.Height - 1))

        Dim data As BitmapData = barBitmap.LockBits(
            New Rectangle(0, 0, barBitmap.Width, barBitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

        Dim lastFilledX As Integer = -1

        Try
            Dim stride As Integer = data.Stride
            Dim bytes((stride * barBitmap.Height) - 1) As Byte
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

            Dim rowOffset As Integer = row * stride
            For x As Integer = 0 To barBitmap.Width - 1
                Dim pixOffset As Integer = rowOffset + x * 4
                Dim b As Byte = bytes(pixOffset)
                Dim g As Byte = bytes(pixOffset + 1)
                Dim r As Byte = bytes(pixOffset + 2)

                If Math.Abs(CInt(r) - filledColour.R) <= colourTolerance AndAlso
                   Math.Abs(CInt(g) - filledColour.G) <= colourTolerance AndAlso
                   Math.Abs(CInt(b) - filledColour.B) <= colourTolerance Then
                    lastFilledX = x
                End If
            Next
        Finally
            barBitmap.UnlockBits(data)
        End Try

        If lastFilledX < 0 Then Return 0.0
        Return CDbl(lastFilledX + 1) / CDbl(barBitmap.Width)
    End Function

#End Region

#Region "PictureBox Integration"

    ''' <summary>
    ''' Captures the screen region under a PictureBox, optionally displaying the
    ''' captured image in the box (for debug visualisation).
    ''' Returns the fill percentage using the specified colour.
    ''' </summary>
    Public Shared Function CaptureAndAnalyse(pb As PictureBox,
                                              filledColour As Color,
                                              Optional tolerance As Integer = 30,
                                              Optional showCapture As Boolean = False) As Double
        Dim bmp As Bitmap = CaptureUnderControl(pb)
        Try
            If showCapture Then
                Dim prev As Image = pb.Image
                pb.Image = DirectCast(bmp.Clone(), Bitmap)
                pb.SizeMode = PictureBoxSizeMode.StretchImage
                prev?.Dispose()
            End If
            Return AnalyseBarFillFast(bmp, filledColour, tolerance)
        Finally
            If Not showCapture Then bmp.Dispose()
        End Try
    End Function

#End Region

End Class
