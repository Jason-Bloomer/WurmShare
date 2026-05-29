Imports System
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>
''' Holds all state data for a single party member's display panel.
''' This is also the wire-format DTO used to communicate party-member state
''' between clients over the SecureChannel: <see cref="ToJson"/> serializes
''' a snapshot for transmission, and <see cref="TryFromJson"/> safely parses
''' an incoming snapshot back into an instance.
''' </summary>
Public Class PartyMemberData

    ' ── Identity ──────────────────────────────────────────────────────────────
    Public Property PlayerName As String = "Player"
    Public Property IsInCombat As Boolean = False
    Public Property IsStunned As Boolean = False
    Public Property IsEncumbered As Boolean = False

    ' ── Health / Stamina (shared bar) ─────────────────────────────────────────
    ''' <summary>0.0 – 1.0  Current health fraction.</summary>
    Public Property HealthPercent As Double = 1.0

    ''' <summary>0.0 – 1.0  Stamina fraction, capped at HealthPercent.</summary>
    Public Property StaminaPercent As Double = 1.0

    ' ── Sustenance bars ───────────────────────────────────────────────────────
    ''' <summary>0.0 – 1.0</summary>
    Public Property WaterPercent As Double = 1.0

    ''' <summary>0.0 – 1.0</summary>
    Public Property FoodPercent As Double = 1.0

    ''' <summary>0.0 – 1.0  Religious / spiritual favor meter.</summary>
    Public Property FavorPercent As Double = 1.0

    ''' <summary>
    ''' Whether this member is tracking favor. When False the Favor bar is
    ''' omitted from the member's PartySubPanel entirely. Driven on the local
    ''' client by CheckBox1's checked state and round-tripped over the wire so
    ''' remote panels match.
    ''' </summary>
    Public Property FavorEnabled As Boolean = False

    ' ── Action bar ────────────────────────────────────────────────────────────
    Public Property ActionBarVisible As Boolean = False
    ''' <summary>0.0 – 1.0  Progress of the current action.</summary>
    Public Property ActionPercent As Double = 0.0
    Public Property ActionLabel As String = "Action"

    ' ── Numpad grid (positions 1-9, numpad layout) ────────────────────────────
    ''' <summary>
    ''' Values for numpad cells. Index 0 = numpad-1 (bottom-left),
    ''' index 8 = numpad-9 (top-right). Stored in display order.
    ''' Layout:  [6]=7  [7]=8  [8]=9
    '''          [3]=4  [4]=5  [5]=6
    '''          [0]=1  [1]=2  [2]=3
    ''' </summary>
    Public Property NumpadValues As Integer() = New Integer(8) {}
    Public Property SelectedNumpadKey As Integer

    ' ── Combat stance / shelter indicators (drawn around the numpad) ───────────
    ''' <summary>
    ''' Combat stance indicator, 0–3. 0 = no cell highlighted; 1/2/3 highlight
    ''' the left / middle / right stance cell (rendered Red / Yellow / Blue
    ''' respectively). The value is the highlighted cell's 1-based position.
    ''' </summary>
    Public Property CombatStance As Integer = 0

    ''' <summary>
    ''' Shelter direction indicator, 0–4. 0 = no bar lit; 1–4 light exactly one
    ''' of the four bars framing the numpad. Mapping (arbitrary, just needs to
    ''' be consistent): 1 = top, 2 = right, 3 = bottom, 4 = left.
    ''' </summary>
    Public Property ShelterDirection As Integer = 0

    ' ── Cooldown meter ────────────────────────────────────────────────────────
    Public Property CooldownActive As Boolean = False
    ''' <summary>Total duration of the cooldown in milliseconds.</summary>
    Public Property CooldownTotalMs As Integer = 0
    ''' <summary>Remaining cooldown in milliseconds (counts down to 0).</summary>
    Public Property CooldownRemainingMs As Integer = 0

    ' =========================================================================
    ' JSON SERIALIZATION
    '
    ' All public properties round-trip via System.Text.Json. The same options
    ' are reused for every call so serializer/metadata caches stay warm.
    ' =========================================================================

    Private Shared ReadOnly s_jsonOptions As JsonSerializerOptions =
        New JsonSerializerOptions() With {
            .PropertyNameCaseInsensitive = True,
            .IncludeFields = False,
            .WriteIndented = False,
            .DefaultIgnoreCondition = JsonIgnoreCondition.Never
        }

    ''' <summary>
    ''' Serialize this instance to a compact JSON string suitable for transmission
    ''' over the SecureChannel.
    ''' </summary>
    Public Function ToJson() As String
        Return JsonSerializer.Serialize(Of PartyMemberData)(Me, s_jsonOptions)
    End Function

    ''' <summary>
    ''' Strict deserialization. Throws on null / empty / malformed input.
    ''' Prefer <see cref="TryFromJson"/> for parsing untrusted network strings.
    ''' </summary>
    Public Shared Function FromJson(json As String) As PartyMemberData
        If String.IsNullOrWhiteSpace(json) Then
            Throw New ArgumentException("JSON string is null or empty.", NameOf(json))
        End If

        Dim result As PartyMemberData =
            JsonSerializer.Deserialize(Of PartyMemberData)(json, s_jsonOptions)
        If result Is Nothing Then
            Throw New JsonException("Deserialized payload was null.")
        End If

        Sanitize(result)
        Return result
    End Function

    ''' <summary>
    ''' Non-throwing version of <see cref="FromJson"/>. Returns Nothing if the
    ''' input is not a parseable PartyMemberData document. Useful for filtering
    ''' messages on a shared channel where non-DTO traffic may also flow.
    ''' </summary>
    Public Shared Function TryFromJson(json As String) As PartyMemberData
        If String.IsNullOrWhiteSpace(json) Then Return Nothing

        ' Fast pre-check: a serialized PartyMemberData object always starts
        ' with '{'. Plain chat lines / log dumps / anything else can be
        ' rejected without invoking the JSON parser.
        Dim trimmed As String = json.TrimStart()
        If trimmed.Length = 0 OrElse trimmed.Chars(0) <> "{"c Then Return Nothing

        Try
            Return FromJson(json)
        Catch
            ' Any deserialization failure (malformed JSON, wrong shape,
            ' wrong types, etc.) is treated as "not one of ours".
            Return Nothing
        End Try
    End Function

    ' =========================================================================
    ' STATE MUTATION
    ' =========================================================================

    ''' <summary>
    ''' Copy every field value from <paramref name="other"/> into this instance.
    ''' The object reference of <c>Me</c> is preserved, so any UI panel bound
    ''' via <see cref="PartySubPanel.SetData"/> continues to render this same
    ''' instance and simply needs to be invalidated to pick up the new values.
    ''' </summary>
    Public Sub UpdateFrom(other As PartyMemberData)
        If other Is Nothing Then Return

        ' Identity: only overwrite PlayerName if the remote sent a real one.
        If Not String.IsNullOrEmpty(other.PlayerName) Then
            Me.PlayerName = other.PlayerName
        End If
        Me.IsInCombat = other.IsInCombat
        Me.IsStunned = other.IsStunned
        Me.IsEncumbered = other.IsEncumbered

        ' Bars (clamped defensively in case caller bypassed FromJson).
        Me.HealthPercent = Clamp01(other.HealthPercent)
        Me.StaminaPercent = Clamp01(other.StaminaPercent)
        Me.WaterPercent = Clamp01(other.WaterPercent)
        Me.FoodPercent = Clamp01(other.FoodPercent)
        Me.FavorPercent = Clamp01(other.FavorPercent)
        Me.FavorEnabled = other.FavorEnabled

        ' Action bar.
        Me.ActionBarVisible = other.ActionBarVisible
        Me.ActionPercent = Clamp01(other.ActionPercent)
        If Not String.IsNullOrEmpty(other.ActionLabel) Then
            Me.ActionLabel = other.ActionLabel
        End If

        ' Numpad: copy in-place if shapes match, otherwise adopt the new array.
        Dim incomingPad As Integer() = NormalizeNumpad(other.NumpadValues)
        If Me.NumpadValues Is Nothing OrElse Me.NumpadValues.Length <> incomingPad.Length Then
            Me.NumpadValues = incomingPad
        Else
            Array.Copy(incomingPad, Me.NumpadValues, incomingPad.Length)
        End If
        Me.SelectedNumpadKey = other.SelectedNumpadKey

        ' Discrete indicators (clamped to their valid ranges).
        Me.CombatStance = Math.Max(0, Math.Min(3, other.CombatStance))
        Me.ShelterDirection = Math.Max(0, Math.Min(4, other.ShelterDirection))

        ' Cooldown (negative values are nonsensical for a duration).
        Me.CooldownActive = other.CooldownActive
        Me.CooldownTotalMs = Math.Max(0, other.CooldownTotalMs)
        Me.CooldownRemainingMs = Math.Max(0, other.CooldownRemainingMs)
    End Sub

    ''' <summary>
    ''' Produce a deep copy of this instance. Useful when the caller needs a
    ''' snapshot that won't observe future mutations of the original.
    ''' </summary>
    Public Function Clone() As PartyMemberData
        Dim copy As New PartyMemberData()
        copy.UpdateFrom(Me)
        Return copy
    End Function

    ' =========================================================================
    ' INTERNAL HELPERS
    ' =========================================================================

    ''' <summary>
    ''' Apply defensive normalization to a freshly-deserialized instance so the
    ''' rest of the application can rely on its invariants (PlayerName non-empty,
    ''' bar fractions in [0,1], NumpadValues a 9-element array, etc.).
    ''' </summary>
    Private Shared Sub Sanitize(d As PartyMemberData)
        If d Is Nothing Then Return
        If String.IsNullOrEmpty(d.PlayerName) Then d.PlayerName = "Player"
        If d.ActionLabel Is Nothing Then d.ActionLabel = "Action"
        d.HealthPercent = Clamp01(d.HealthPercent)
        d.StaminaPercent = Clamp01(d.StaminaPercent)
        d.WaterPercent = Clamp01(d.WaterPercent)
        d.FoodPercent = Clamp01(d.FoodPercent)
        d.FavorPercent = Clamp01(d.FavorPercent)
        d.ActionPercent = Clamp01(d.ActionPercent)
        d.CooldownTotalMs = Math.Max(0, d.CooldownTotalMs)
        d.CooldownRemainingMs = Math.Max(0, d.CooldownRemainingMs)
        d.NumpadValues = NormalizeNumpad(d.NumpadValues)
        d.CombatStance = Math.Max(0, Math.Min(3, d.CombatStance))
        d.ShelterDirection = Math.Max(0, Math.Min(4, d.ShelterDirection))
    End Sub

    Private Shared Function Clamp01(v As Double) As Double
        If Double.IsNaN(v) Then Return 0.0
        If v < 0.0 Then Return 0.0
        If v > 1.0 Then Return 1.0
        Return v
    End Function

    ''' <summary>
    ''' Guarantee a 9-element NumpadValues array, padding with zeros or
    ''' truncating as required. Defends against malformed remote payloads.
    ''' </summary>
    Private Shared Function NormalizeNumpad(src As Integer()) As Integer()
        Dim out(8) As Integer
        If src Is Nothing Then Return out
        Dim n As Integer = Math.Min(src.Length, 9)
        If n > 0 Then Array.Copy(src, out, n)
        Return out
    End Function

End Class
