using System.Net.WebSockets;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace XComfortDotNet;

/// <summary>
/// Connection state
/// </summary>
public enum ConnectionState
{
    Initial = 1,
    Loading = 2,
    Loaded = 3
}

/// <summary>
/// Secure WebSocket connection to xComfort Bridge with encryption
/// </summary>
public class SecureBridgeConnection : IDisposable
{
    internal readonly ClientWebSocket _webSocket;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly string _deviceId;
    private readonly Subject<JsonElement> _messageSubject;
    private int _mc;

    public ConnectionState State { get; private set; }
    public IObservable<JsonElement> Messages { get; }
    public string DeviceId => _deviceId;

    public SecureBridgeConnection(ClientWebSocket webSocket, byte[] key, byte[] iv, string deviceId)
    {
        _webSocket = webSocket;
        _key = key;
        _iv = iv;
        _deviceId = deviceId;
        _messageSubject = new Subject<JsonElement>();
        _mc = 0;
        State = ConnectionState.Initial;
        Messages = _messageSubject;
    }

    internal string Decrypt(string data)
    {
        var cipherBytes = Convert.FromBase64String(data);
        
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.Zeros;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        
        // Remove null padding
        var endIndex = Array.FindLastIndex(decryptedBytes, b => b != 0) + 1;
        if (endIndex <= 0)
            return "{}";

        var decryptedString = Encoding.UTF8.GetString(decryptedBytes, 0, endIndex);
        return decryptedString;
    }

    private string Encrypt(string data)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
        
        // Pad to AES block size (16 bytes)
        var blockSize = 16;
        var paddedLength = (dataBytes.Length + blockSize - 1) / blockSize * blockSize;
        var paddedBytes = new byte[paddedLength];
        Array.Copy(dataBytes, paddedBytes, dataBytes.Length);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.Zeros;

        using var encryptor = aes.CreateEncryptor();
        var encryptedBytes = encryptor.TransformFinalBlock(paddedBytes, 0, paddedBytes.Length);
        
        return Convert.ToBase64String(encryptedBytes) + "\u0004";
    }

    public async Task PumpAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Loading;

        await SendMessageAsync(240, new { });
        await SendMessageAsync(242, new { });
        await SendMessageAsync(2, new { });

        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var encryptedMessage = messageBuilder.ToString();
                    messageBuilder.Clear();

                    var decrypted = Decrypt(encryptedMessage);
                    
                    if (!string.IsNullOrWhiteSpace(decrypted) && decrypted != "{}")
                    {
                        var message = JsonSerializer.Deserialize<JsonElement>(decrypted);

                        if (message.TryGetProperty("mc", out var mcProperty))
                        {
                            // Send ACK
                            await SendAsync(new { type_int = 1, @ref = mcProperty.GetInt32() });
                        }

                        if (message.TryGetProperty("payload", out _))
                        {
                            _messageSubject.OnNext(message);
                        }
                    }
                }
            }
        }
    }

    public async Task SendMessageAsync(int messageType, object payload)
    {
        _mc++;
        await SendAsync(new { type_int = messageType, mc = _mc, payload });
    }

    public async Task SendMessageAsync(Messages messageType, object payload)
    {
        await SendMessageAsync((int)messageType, payload);
    }

    private async Task SendAsync(object data)
    {
        var json = JsonSerializer.Serialize(data);
        var encrypted = Encrypt(json);
        var bytes = Encoding.UTF8.GetBytes(encrypted);
        
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes), 
            WebSocketMessageType.Text, 
            true, 
            CancellationToken.None);
    }

    public async Task CloseAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
    }

    public void Dispose()
    {
        _webSocket?.Dispose();
        _messageSubject?.Dispose();
    }
}

/// <summary>
/// Helper class to establish secure connection to xComfort Bridge
/// </summary>
public static class ConnectionHelper
{
    private static string GenerateSalt()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 12)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }

    private static string Hash(string deviceId, string authKey, string salt)
    {
        using var sha256 = SHA256.Create();
        
        // First hash: SHA256(deviceId + authKey)
        var firstInput = Encoding.UTF8.GetBytes(deviceId + authKey);
        var firstHash = sha256.ComputeHash(firstInput);
        var firstHashHex = BitConverter.ToString(firstHash).Replace("-", "").ToLower();
        
        // Second hash: SHA256(salt + firstHashHex)
        var secondInput = Encoding.UTF8.GetBytes(salt + firstHashHex);
        var secondHash = sha256.ComputeHash(secondInput);
        
        return BitConverter.ToString(secondHash).Replace("-", "").ToLower();
    }

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket ws)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                var message = messageBuilder.ToString();
                // Remove trailing null character if present
                if (message.EndsWith('\0'))
                    message = message.TrimEnd('\0');
                
                return JsonSerializer.Deserialize<JsonElement>(message);
            }
        }
    }

    private static async Task SendAsync(ClientWebSocket ws, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public static async Task<SecureBridgeConnection> SetupSecureConnectionAsync(string ipAddress, string authKey)
    {
        var ws = new ClientWebSocket();
        
        try
        {
            await ws.ConnectAsync(new Uri($"ws://{ipAddress}/"), CancellationToken.None);

            // Receive initial message
            var msg = await ReceiveAsync(ws);

            if (msg.GetProperty("type_int").GetInt32() == (int)Messages.NACK)
            {
                throw new Exception(msg.GetProperty("info").GetString() ?? "Connection declined");
            }

            var deviceId = msg.GetProperty("payload").GetProperty("device_id").GetString()!;
            var connectionId = msg.GetProperty("payload").GetProperty("connection_id").GetInt32();

            // Send connection confirm
            await SendAsync(ws, new
            {
                type_int = 11,
                mc = -1,
                payload = new
                {
                    client_type = "shl-app",
                    client_id = "c956e43f999f8004",
                    client_version = "3.0.0",
                    connection_id = connectionId
                }
            });

            msg = await ReceiveAsync(ws);

            if (msg.GetProperty("type_int").GetInt32() == (int)Messages.CONNECTION_DECLINED)
            {
                throw new Exception(msg.GetProperty("payload").GetProperty("error_message").GetString() ?? "Connection declined");
            }

            // Initialize secure connection
            await SendAsync(ws, new { type_int = 14, mc = -1 });

            msg = await ReceiveAsync(ws);
            var publicKeyPem = msg.GetProperty("payload").GetProperty("public_key").GetString()!;

            // Parse RSA public key
            var pemReader = new PemReader(new StringReader(publicKeyPem));
            var publicKey = (AsymmetricKeyParameter)pemReader.ReadObject();

            // Generate AES key and IV
            var key = new byte[32];
            var iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
                rng.GetBytes(iv);
            }

            // Encrypt key and IV with RSA
            var cipher = new Pkcs1Encoding(new RsaEngine());
            cipher.Init(true, publicKey);
            
            var keyIvString = BitConverter.ToString(key).Replace("-", "").ToLower() + ":::" + 
                            BitConverter.ToString(iv).Replace("-", "").ToLower();
            var keyIvBytes = Encoding.UTF8.GetBytes(keyIvString);
            var encryptedSecret = cipher.ProcessBlock(keyIvBytes, 0, keyIvBytes.Length);
            var secret = Convert.ToBase64String(encryptedSecret);

            // Send encrypted secret
            await SendAsync(ws, new
            {
                type_int = 16,
                mc = -1,
                payload = new { secret }
            });

            var connection = new SecureBridgeConnection(ws, key, iv, deviceId);

            // Login process
            var loginMsg = await ReceiveEncryptedAsync(connection);

            if (loginMsg.GetProperty("type_int").GetInt32() != 17)
            {
                throw new Exception("Failed to establish secure connection");
            }

            var salt = GenerateSalt();
            var password = Hash(deviceId, authKey, salt);

            await connection.SendMessageAsync(30, new
            {
                username = "default",
                password,
                salt
            });

            loginMsg = await ReceiveEncryptedAsync(connection);

            if (loginMsg.GetProperty("type_int").GetInt32() != 32)
            {
                throw new Exception("Login failed");
            }

            var token = loginMsg.GetProperty("payload").GetProperty("token").GetString()!;
            await connection.SendMessageAsync(33, new { token });

            // Receive token validation
            await ReceiveEncryptedAsync(connection);

            // Renew token
            await connection.SendMessageAsync(37, new { token });

            loginMsg = await ReceiveEncryptedAsync(connection);

            if (loginMsg.GetProperty("type_int").GetInt32() != 38)
            {
                throw new Exception("Token renewal failed");
            }

            token = loginMsg.GetProperty("payload").GetProperty("token").GetString()!;
            await connection.SendMessageAsync(33, new { token });

            // Receive token validation
            await ReceiveEncryptedAsync(connection);

            return connection;
        }
        catch
        {
            ws?.Dispose();
            throw;
        }
    }

    private static async Task<JsonElement> ReceiveEncryptedAsync(SecureBridgeConnection connection)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (true)
        {
            var result = await connection._webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                var encryptedMessage = messageBuilder.ToString();
                var decrypted = connection.Decrypt(encryptedMessage);
                return JsonSerializer.Deserialize<JsonElement>(decrypted);
            }
        }
    }
}
