' ==============================================================================
' SecureChannelClient.vb
' Drop-in VB.NET module for encrypted, password-gated channel communication
' with the SecureChannel PHP server.
'
' SERVER ENDPOINTS (hardcoded file paths, no mod_rewrite required)
' ─────────────────────────────────────────────────────────────────────────────
'   POST  /channel_create.php
'   POST  /channel_join.php
'   POST  /messages.php
'   GET   /messages.php?channel_id=...&auth_token=...&since_id=...
'
' CRYPTOGRAPHIC DESIGN
' ─────────────────────────────────────────────────────────────────────────────
'   Given password P:
'     channel_id  = Hex( SHA-256(P) )           <- channel identity, sent to server
'     auth_token  = Hex( SHA-256(channel_id) )  <- proof of password; server stores this
'     aes_key     = raw bytes of SHA-256(P)     <- 256-bit AES key, NEVER leaves this process
'
'   Messages are AES-256-CBC encrypted before transmission.
'   The server stores only opaque Base64 ciphertext and has no decryption key.
'
' REQUIREMENTS
' ─────────────────────────────────────────────────────────────────────────────
'   .NET 6+ (or .NET Framework 4.7.2+ with the System.Text.Json NuGet package)
'
' QUICK-START
' ─────────────────────────────────────────────────────────────────────────────
'   Using client As New SecureChannelClient("https://yourserver.com")
'
'       ' First-time setup - create a new channel:
'       Await client.CreateChannelAsync("your-secret-password")
'
'       ' Subsequent runs - join the existing channel:
'       Await client.JoinChannelAsync("your-secret-password")
'
'       ' Send:
'       Await client.PostMessageAsync("Hello, encrypted world!")
'
'       ' Receive (call repeatedly to poll for new messages):
'       For Each msg In Await client.GetMessagesAsync()
'           Console.WriteLine(msg)
'       Next
'
'   End Using
' ==============================================================================

Imports System
Imports System.Collections.Generic
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class SecureChannelClient
    Implements IDisposable

    ' Hardcoded file paths - no mod_rewrite needed, Apache serves these directly
    Private Const PATH_CREATE   As String = "/channel_create.php"
    Private Const PATH_JOIN     As String = "/channel_join.php"
    Private Const PATH_MESSAGES As String = "/messages.php"

    Private ReadOnly _http    As HttpClient
    Private ReadOnly _baseUrl As String
    Private _channelId As String = String.Empty
    Private _authToken As String = String.Empty
    Private _aesKey    As Byte() = Nothing
    Private _lastId    As Long   = 0
    Private _disposed  As Boolean = False

    ' =========================================================================
    ' CONSTRUCTOR
    ' =========================================================================

    ''' <summary>
    ''' Creates a new SecureChannelClient.
    ''' </summary>
    ''' <param name="serverBaseUrl">
    '''   Base URL of the server, e.g. "https://yourserver.com" or
    '''   "https://yourserver.com/subfolder". Trailing slash is trimmed.
    ''' </param>
    ''' <param name="timeoutSeconds">HTTP request timeout in seconds (default 30).</param>
    Public Sub New(serverBaseUrl As String, Optional timeoutSeconds As Integer = 30)
        If String.IsNullOrWhiteSpace(serverBaseUrl) Then
            Throw New ArgumentException("Server URL must not be empty.", NameOf(serverBaseUrl))
        End If
        If timeoutSeconds < 1 Then
            Throw New ArgumentOutOfRangeException(NameOf(timeoutSeconds))
        End If
        _baseUrl = serverBaseUrl.TrimEnd("/"c)
        _http = New HttpClient()
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        _http.DefaultRequestHeaders.Accept.Add(
            New MediaTypeWithQualityHeaderValue("application/json"))
    End Sub

    ' =========================================================================
    ' PUBLIC API
    ' =========================================================================

    ''' <summary>
    ''' Creates a new channel protected by <paramref name="password"/>.
    ''' Returns True on success, False if the channel already exists (use JoinChannelAsync instead).
    ''' </summary>
    Public Async Function CreateChannelAsync(password As String,
                                             Optional ct As CancellationToken = Nothing) As Task(Of Boolean)
        ValidatePassword(password)
        DeriveKeys(password)
        Dim body = BuildJson(New Dictionary(Of String, String) From {
            {"channel_id", _channelId}, {"auth_token", _authToken}
        })
        Using resp = Await PostJsonAsync(PATH_CREATE, body, ct)
            Select Case resp.StatusCode
                Case Net.HttpStatusCode.Created  : Return True
                Case Net.HttpStatusCode.Conflict : Return False
                Case Else : Await ThrowOnBadResponse(resp, "CreateChannel") : Return False
            End Select
        End Using
    End Function

    ''' <summary>Synchronous wrapper for CreateChannelAsync.</summary>
    Public Function CreateChannel(password As String) As Boolean
        Return CreateChannelAsync(password).GetAwaiter().GetResult()
    End Function

    ' -------------------------------------------------------------------------

    ''' <summary>
    ''' Joins an existing channel. Returns True on success, False if the channel
    ''' does not exist or the password is wrong.
    ''' </summary>
    Public Async Function JoinChannelAsync(password As String,
                                           Optional ct As CancellationToken = Nothing) As Task(Of Boolean)
        ValidatePassword(password)
        DeriveKeys(password)
        Dim body = BuildJson(New Dictionary(Of String, String) From {
            {"channel_id", _channelId}, {"auth_token", _authToken}
        })
        Using resp = Await PostJsonAsync(PATH_JOIN, body, ct)
            Select Case resp.StatusCode
                Case Net.HttpStatusCode.OK                                   : Return True
                Case Net.HttpStatusCode.Unauthorized, Net.HttpStatusCode.NotFound : Return False
                Case Else : Await ThrowOnBadResponse(resp, "JoinChannel")   : Return False
            End Select
        End Using
    End Function

    ''' <summary>Synchronous wrapper for JoinChannelAsync.</summary>
    Public Function JoinChannel(password As String) As Boolean
        Return JoinChannelAsync(password).GetAwaiter().GetResult()
    End Function

    ' -------------------------------------------------------------------------

    ''' <summary>
    ''' Encrypts <paramref name="text"/> with AES-256-CBC and posts it to the current channel.
    ''' The server stores only opaque ciphertext and has no decryption key.
    ''' Call CreateChannelAsync or JoinChannelAsync first.
    ''' </summary>
    Public Async Function PostMessageAsync(text As String,
                                           Optional ct As CancellationToken = Nothing) As Task(Of Boolean)
        EnsureJoined()
        If text Is Nothing Then Throw New ArgumentNullException(NameOf(text))
        Dim body = BuildJson(New Dictionary(Of String, String) From {
            {"channel_id", _channelId}, {"auth_token", _authToken}, {"data", Encrypt(text)}
        })
        Using resp = Await PostJsonAsync(PATH_MESSAGES, body, ct)
            If resp.StatusCode = Net.HttpStatusCode.Created Then Return True
            Await ThrowOnBadResponse(resp, "PostMessage")
            Return False
        End Using
    End Function

    ''' <summary>Synchronous wrapper for PostMessageAsync.</summary>
    Public Function PostMessage(text As String) As Boolean
        Return PostMessageAsync(text).GetAwaiter().GetResult()
    End Function

    ' -------------------------------------------------------------------------

    ''' <summary>
    ''' Retrieves and decrypts all messages since the last call.
    ''' Uses an internal cursor — each call returns only NEW messages.
    ''' Call ResetMessageCursor to start from the beginning of the channel.
    ''' Call CreateChannelAsync or JoinChannelAsync first.
    ''' </summary>
    Public Async Function GetMessagesAsync(Optional ct As CancellationToken = Nothing) As Task(Of List(Of String))
        EnsureJoined()
        Dim url = $"{_baseUrl}{PATH_MESSAGES}?channel_id={Uri.EscapeDataString(_channelId)}" &
                  $"&auth_token={Uri.EscapeDataString(_authToken)}&since_id={_lastId}"
        Using resp = Await _http.GetAsync(url, ct)
            Select Case resp.StatusCode
                Case Net.HttpStatusCode.Unauthorized, Net.HttpStatusCode.NotFound
                    Throw New UnauthorizedAccessException(
                        "Channel credentials rejected. Verify the password and channel existence.")
            End Select
            resp.EnsureSuccessStatusCode()
            Return ParseMessagesResponse(Await resp.Content.ReadAsStringAsync())
        End Using
    End Function

    ''' <summary>Synchronous wrapper for GetMessagesAsync.</summary>
    Public Function GetMessages() As List(Of String)
        Return GetMessagesAsync().GetAwaiter().GetResult()
    End Function

    ' -------------------------------------------------------------------------

    ''' <summary>Resets the polling cursor so the next GetMessages call returns all messages.</summary>
    Public Sub ResetMessageCursor()
        Interlocked.Exchange(_lastId, 0)
    End Sub

    ''' <summary>The channel identifier (Hex SHA-256 of the password). Empty until joined.</summary>
    Public ReadOnly Property ChannelId As String
        Get
            Return _channelId
        End Get
    End Property

    ''' <summary>True if CreateChannel or JoinChannel has been called successfully.</summary>
    Public ReadOnly Property IsJoined As Boolean
        Get
            Return Not String.IsNullOrEmpty(_channelId)
        End Get
    End Property

    ' =========================================================================
    ' CRYPTOGRAPHY
    ' =========================================================================

    Private Sub DeriveKeys(password As String)
        Using sha As SHA256 = SHA256.Create()
            Dim pwdBytes As Byte() = Encoding.UTF8.GetBytes(password)
            Dim hash1 As Byte() = sha.ComputeHash(pwdBytes)
            Dim hash2 As Byte() = sha.ComputeHash(hash1)
            _channelId = ToHex(hash1)
            _authToken = ToHex(hash2)
            If _aesKey IsNot Nothing Then Array.Clear(_aesKey, 0, _aesKey.Length)
            _aesKey = hash1
        End Using
    End Sub

    Private Function Encrypt(plaintext As String) As String
        Using aes As Aes = Aes.Create()
            aes.Key = _aesKey : aes.Mode = CipherMode.CBC
            aes.Padding = PaddingMode.PKCS7 : aes.GenerateIV()
            Dim plain As Byte() = Encoding.UTF8.GetBytes(plaintext)
            Using enc = aes.CreateEncryptor()
                Dim cipher As Byte() = enc.TransformFinalBlock(plain, 0, plain.Length)
                Dim result(aes.IV.Length + cipher.Length - 1) As Byte
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length)
                Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length)
                Return Convert.ToBase64String(result)
            End Using
        End Using
    End Function

    Private Function Decrypt(ciphertext As String) As String
        Dim data As Byte() = Convert.FromBase64String(ciphertext)
        If data.Length < 17 Then Throw New CryptographicException("Ciphertext too short.")
        Using aes As Aes = Aes.Create()
            aes.Key = _aesKey : aes.Mode = CipherMode.CBC : aes.Padding = PaddingMode.PKCS7
            Dim iv(15) As Byte
            Buffer.BlockCopy(data, 0, iv, 0, 16)
            aes.IV = iv
            Using dec = aes.CreateDecryptor()
                Return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, 16, data.Length - 16))
            End Using
        End Using
    End Function

    ' =========================================================================
    ' JSON HELPERS
    ' =========================================================================

    Private Shared Function BuildJson(pairs As Dictionary(Of String, String)) As String
        Dim sb As New StringBuilder(256)
        sb.Append("{"c)
        Dim first As Boolean = True
        For Each kvp In pairs
            If Not first Then sb.Append(","c)
            first = False
            sb.Append(""""c) : sb.Append(JsonEscape(kvp.Key))
            sb.Append(""":""") : sb.Append(JsonEscape(kvp.Value))
            sb.Append(""""c)
        Next
        sb.Append("}"c)
        Return sb.ToString()
    End Function

    Private Shared Function JsonEscape(s As String) As String
        Dim sb As New StringBuilder(s.Length)
        For Each c As Char In s
            Select Case c
                Case "\"c  : sb.Append("\\")
                Case """"c : sb.Append("\""")
                Case Chr(8)  : sb.Append("\b")
                Case Chr(9)  : sb.Append("\t")
                Case Chr(10) : sb.Append("\n")
                Case Chr(12) : sb.Append("\f")
                Case Chr(13) : sb.Append("\r")
                Case Else
                    If AscW(c) < 32 Then sb.Append($"\u{AscW(c):x4}") Else sb.Append(c)
            End Select
        Next
        Return sb.ToString()
    End Function

    Private Function ParseMessagesResponse(json As String) As List(Of String)
        Dim results As New List(Of String)
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = doc.RootElement
                Dim lastIdEl As JsonElement
                If root.TryGetProperty("last_id", lastIdEl) Then
                    Dim serverLastId As Long = lastIdEl.GetInt64()
                    If serverLastId > _lastId Then Interlocked.Exchange(_lastId, serverLastId)
                End If
                Dim messagesEl As JsonElement
                If Not root.TryGetProperty("messages", messagesEl) Then Return results
                For Each msg As JsonElement In messagesEl.EnumerateArray()
                    Dim dataEl As JsonElement
                    If msg.TryGetProperty("data", dataEl) Then
                        Dim encrypted As String = dataEl.GetString()
                        If Not String.IsNullOrEmpty(encrypted) Then
                            Try
                                results.Add(Decrypt(encrypted))
                            Catch
                                ' Skip messages that cannot be decrypted
                            End Try
                        End If
                    End If
                Next
            End Using
        Catch ex As JsonException
            Throw New InvalidOperationException("Failed to parse server response.", ex)
        End Try
        Return results
    End Function

    ' =========================================================================
    ' HTTP, GUARDS, UTILITY
    ' =========================================================================

    Private Async Function PostJsonAsync(path As String, json As String,
                                         ct As CancellationToken) As Task(Of HttpResponseMessage)
        Return Await _http.PostAsync(_baseUrl & path,
            New StringContent(json, Encoding.UTF8, "application/json"), ct)
    End Function

    Private Shared Async Function ThrowOnBadResponse(resp As HttpResponseMessage,
                                                     ctx As String) As Task
        Dim body As String = String.Empty
        Try : body = Await resp.Content.ReadAsStringAsync() : Catch : End Try
        Throw New HttpRequestException(
            $"[SecureChannel] {ctx} failed - HTTP {CInt(resp.StatusCode)}: {body}")
    End Function

    Private Sub EnsureJoined()
        If String.IsNullOrEmpty(_channelId) Then
            Throw New InvalidOperationException(
                "No channel active. Call CreateChannel or JoinChannel first.")
        End If
    End Sub

    Private Shared Sub ValidatePassword(password As String)
        If password Is Nothing Then Throw New ArgumentNullException("password")
        If password.Length = 0 Then Throw New ArgumentException("Password must not be empty.")
    End Sub

    Private Shared Function ToHex(bytes As Byte()) As String
        Dim sb As New StringBuilder(bytes.Length * 2)
        For Each b As Byte In bytes : sb.Append(b.ToString("x2")) : Next
        Return sb.ToString()
    End Function

    ' =========================================================================
    ' IDisposable
    ' =========================================================================

    Public Sub Dispose() Implements IDisposable.Dispose
        If Not _disposed Then
            _http?.Dispose()
            If _aesKey IsNot Nothing Then
                Array.Clear(_aesKey, 0, _aesKey.Length)
                _aesKey = Nothing
            End If
            _disposed = True
        End If
        GC.SuppressFinalize(Me)
    End Sub

End Class
