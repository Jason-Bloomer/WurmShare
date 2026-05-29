Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' Handles all cryptographic operations for the party network.
'''
''' From a single password two independent secrets are derived:
'''   ChannelId  – a public token safe to send to the rendezvous server.
'''                Identifies the group but reveals nothing about the password.
'''   ChannelKey – an AES-256 + HMAC key bundle that never leaves the client.
'''                Used to encrypt and authenticate every message.
'''
''' Encryption: AES-256-CBC with a random IV prepended to each ciphertext.
''' Authentication: HMAC-SHA256 appended to each encrypted payload (encrypt-then-MAC).
''' </summary>
Public Class CryptoHelper

#Region "Constants"

    Private Const PBKDF2_ITERATIONS As Integer = 120_000
    Private Const AES_KEY_BYTES As Integer = 32     ' 256 bits
    Private Const MAC_KEY_BYTES As Integer = 32     ' 256 bits
    Private Const IV_BYTES As Integer = 16
    Private Const MAC_BYTES As Integer = 32         ' HMAC-SHA256 output

    ' Fixed salts – different for each derived value so the two outputs are independent.
    ' These are NOT secret; they just domain-separate the derivations.
    Private Shared ReadOnly SALT_CHANNEL_ID As Byte() =
        Encoding.UTF8.GetBytes("PartyOverlay_ChannelID_v1_salt__")
    Private Shared ReadOnly SALT_AES_KEY As Byte() =
        Encoding.UTF8.GetBytes("PartyOverlay_AESKey_v1_salt_____")
    Private Shared ReadOnly SALT_MAC_KEY As Byte() =
        Encoding.UTF8.GetBytes("PartyOverlay_MACKey_v1_salt_____")

#End Region

#Region "Derived Secrets"

    ''' <summary>
    ''' Derives the public channel identifier from the password.
    ''' Safe to transmit to the rendezvous server.
    ''' Returns a lowercase hex string.
    ''' </summary>
    Public Shared Function DeriveChannelId(password As String) As String
        Dim pwBytes As Byte() = Encoding.UTF8.GetBytes(password)
        Using prf As New Rfc2898DeriveBytes(pwBytes, SALT_CHANNEL_ID,
                                            PBKDF2_ITERATIONS, HashAlgorithmName.SHA256)
            Dim raw As Byte() = prf.GetBytes(16)   ' 128-bit identifier
            Return BitConverter.ToString(raw).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    ''' <summary>
    ''' Derives the AES-256 encryption key and HMAC-SHA256 MAC key from the password.
    ''' These never leave the local machine.
    ''' </summary>
    Public Shared Sub DeriveChannelKeys(password As String,
                                        <Out> ByRef aesKey As Byte(),
                                        <Out> ByRef macKey As Byte())
        Dim pwBytes As Byte() = Encoding.UTF8.GetBytes(password)

        Using prf As New Rfc2898DeriveBytes(pwBytes, SALT_AES_KEY,
                                            PBKDF2_ITERATIONS, HashAlgorithmName.SHA256)
            aesKey = prf.GetBytes(AES_KEY_BYTES)
        End Using

        Using prf As New Rfc2898DeriveBytes(pwBytes, SALT_MAC_KEY,
                                            PBKDF2_ITERATIONS, HashAlgorithmName.SHA256)
            macKey = prf.GetBytes(MAC_KEY_BYTES)
        End Using
    End Sub

#End Region

#Region "Encrypt / Decrypt"

    ''' <summary>
    ''' Encrypts plaintext bytes and appends an HMAC.
    ''' Output layout: [IV (16)] [Ciphertext (n)] [HMAC (32)]
    ''' </summary>
    Public Shared Function Encrypt(plaintext As Byte(),
                                   aesKey As Byte(),
                                   macKey As Byte()) As Byte()
        Using aes As Aes = Aes.Create()
            aes.Key = aesKey
            aes.Mode = CipherMode.CBC
            aes.Padding = PaddingMode.PKCS7
            aes.GenerateIV()

            Dim iv As Byte() = aes.IV

            Dim ciphertext As Byte()
            Using encryptor As ICryptoTransform = aes.CreateEncryptor()
                ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length)
            End Using

            ' Concatenate IV + ciphertext
            Dim payload(IV_BYTES + ciphertext.Length - 1) As Byte
            Buffer.BlockCopy(iv, 0, payload, 0, IV_BYTES)
            Buffer.BlockCopy(ciphertext, 0, payload, IV_BYTES, ciphertext.Length)

            ' HMAC over the IV+ciphertext
            Using hmac As New HMACSHA256(macKey)
                Dim mac As Byte() = hmac.ComputeHash(payload)
                Dim result(payload.Length + MAC_BYTES - 1) As Byte
                Buffer.BlockCopy(payload, 0, result, 0, payload.Length)
                Buffer.BlockCopy(mac, 0, result, payload.Length, MAC_BYTES)
                Return result
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Verifies the HMAC and decrypts. Returns Nothing if authentication fails.
    ''' </summary>
    Public Shared Function Decrypt(data As Byte(),
                                   aesKey As Byte(),
                                   macKey As Byte()) As Byte()
        If data Is Nothing OrElse data.Length < IV_BYTES + MAC_BYTES + 1 Then
            Return Nothing
        End If

        ' Split off the trailing MAC
        Dim payloadLen As Integer = data.Length - MAC_BYTES
        Dim payload(payloadLen - 1) As Byte
        Dim receivedMac(MAC_BYTES - 1) As Byte
        Buffer.BlockCopy(data, 0, payload, 0, payloadLen)
        Buffer.BlockCopy(data, payloadLen, receivedMac, 0, MAC_BYTES)

        ' Verify HMAC (constant-time comparison)
        Using hmac As New HMACSHA256(macKey)
            Dim expectedMac As Byte() = hmac.ComputeHash(payload)
            If Not ConstantTimeEquals(expectedMac, receivedMac) Then
                Return Nothing   ' Authentication failure
            End If
        End Using

        ' Decrypt
        Dim iv(IV_BYTES - 1) As Byte
        Dim ciphertext(payloadLen - IV_BYTES - 1) As Byte
        Buffer.BlockCopy(payload, 0, iv, 0, IV_BYTES)
        Buffer.BlockCopy(payload, IV_BYTES, ciphertext, 0, ciphertext.Length)

        Using aes As Aes = Aes.Create()
            aes.Key = aesKey
            aes.IV = iv
            aes.Mode = CipherMode.CBC
            aes.Padding = PaddingMode.PKCS7
            Try
                Using decryptor As ICryptoTransform = aes.CreateDecryptor()
                    Return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length)
                End Using
            Catch ex As CryptographicException
                Return Nothing   ' Padding error – tampered or wrong key
            End Try
        End Using
    End Function

    ''' <summary>Constant-time byte array comparison to prevent timing attacks.</summary>
    Private Shared Function ConstantTimeEquals(a As Byte(), b As Byte()) As Boolean
        If a.Length <> b.Length Then Return False
        Dim diff As Integer = 0
        For i As Integer = 0 To a.Length - 1
            diff = diff Or (CInt(a(i)) Xor CInt(b(i)))
        Next
        Return diff = 0
    End Function

#End Region

#Region "Convenience – String overloads"

    Public Shared Function EncryptString(plaintext As String,
                                         aesKey As Byte(),
                                         macKey As Byte()) As Byte()
        Return Encrypt(Encoding.UTF8.GetBytes(plaintext), aesKey, macKey)
    End Function

    Public Shared Function DecryptToString(data As Byte(),
                                           aesKey As Byte(),
                                           macKey As Byte()) As String
        Dim raw As Byte() = Decrypt(data, aesKey, macKey)
        If raw Is Nothing Then Return Nothing
        Return Encoding.UTF8.GetString(raw)
    End Function

#End Region

#Region "Client Identity"

    ''' <summary>
    ''' Generates a random 128-bit client ID for this installation.
    ''' Persist this to user settings so it survives restarts.
    ''' </summary>
    Public Shared Function GenerateClientId() As String
        Dim bytes(15) As Byte
        Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            rng.GetBytes(bytes)
        End Using
        Return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()
    End Function

#End Region

End Class
