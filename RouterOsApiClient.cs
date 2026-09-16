using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace IndigoRouterScheduler;

public sealed class RouterOsApiClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private Stream? _stream;

    public static async Task<RouterOsApiClient> ConnectAsync(RouterRecord router, AppSettings settings, CancellationToken cancellationToken)
    {
        var client = new RouterOsApiClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));
            await client._tcp.ConnectAsync(router.Host, router.ApiPort, timeout.Token);
            Stream stream = client._tcp.GetStream();
            if (settings.UseApiSsl)
            {
                var ssl = new SslStream(stream, false, (_, _, _, errors) => settings.AllowInvalidTlsCertificate || errors == SslPolicyErrors.None);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = router.Host }, timeout.Token);
                stream = ssl;
            }
            client._stream = stream;
            await client.LoginAsync(router.Username, router.Password, timeout.Token);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ApiReply>> ExecuteAsync(string command, CancellationToken cancellationToken, params string[] attributes)
    {
        EnsureConnected();
        var words = new List<string> { command };
        words.AddRange(attributes.Select(a => a.StartsWith('=') ? a : "=" + a));
        await WriteSentenceAsync(words, cancellationToken);

        var replies = new List<ApiReply>();
        while (true)
        {
            List<string> sentence = await ReadSentenceAsync(cancellationToken);
            if (sentence.Count == 0) continue;
            string type = sentence[0];
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string word in sentence.Skip(1))
            {
                string item = word.StartsWith('=') ? word[1..] : word;
                int equals = item.IndexOf('=');
                if (equals >= 0) values[item[..equals]] = item[(equals + 1)..];
                else values[item] = "";
            }
            replies.Add(new ApiReply(type, values));
            if (type == "!trap" || type == "!fatal")
                throw new RouterOsApiException(values.GetValueOrDefault("message") ?? type);
            if (type == "!done") return replies;
        }
    }

    public async Task SendExpectedDisconnectAsync(string command, CancellationToken cancellationToken, params string[] attributes)
    {
        try { await ExecuteAsync(command, cancellationToken, attributes); }
        catch (Exception ex) when (ex is IOException or SocketException) { }
    }

    public async Task<(string Id, long Size)> GetFileInfoAsync(string fileName, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApiReply> replies = await ExecuteAsync("/file/print", cancellationToken, ".proplist=.id,name,size");
        ApiReply? file = replies.FirstOrDefault(x => x.Type == "!re" &&
            x.Attributes.GetValueOrDefault("name")?.Equals(fileName, StringComparison.Ordinal) == true);
        if (file is null) throw new FileNotFoundException($"Router file was not found: {fileName}");
        if (!long.TryParse(file.Attributes.GetValueOrDefault("size"), out long size) || size <= 0)
            throw new InvalidDataException($"Router returned an invalid size for {fileName}.");
        return (file.Attributes.GetValueOrDefault(".id") ?? throw new InvalidDataException("Router file ID is missing."), size);
    }

    public async Task DownloadFileAsync(string remoteFileName, string localPath, CancellationToken cancellationToken)
    {
        long size = 0;
        Exception? last = null;
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                (_, size) = await GetFileInfoAsync(remoteFileName, cancellationToken);
                break;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                last = ex;
                if (attempt < 20) await Task.Delay(500, cancellationToken);
            }
        }
        if (size <= 0) throw new IOException($"Router file {remoteFileName} was not ready for download.", last);
        string partialPath = localPath + ".part";
        try
        {
            await using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 32768, true))
            {
                long offset = 0;
                while (offset < size)
                {
                    int requested = (int)Math.Min(32768, size - offset);
                    byte[] data = await ReadFileChunkAsync(remoteFileName, offset, requested, cancellationToken);
                    if (data.Length == 0) throw new EndOfStreamException($"Router stopped sending {remoteFileName} at byte {offset} of {size}.");
                    int accepted = Math.Min(data.Length, requested);
                    await output.WriteAsync(data.AsMemory(0, accepted), cancellationToken);
                    offset += accepted;
                }
                await output.FlushAsync(cancellationToken);
            }
            File.Move(partialPath, localPath, true);
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            throw;
        }
    }

    public async Task DeleteFileAsync(string remoteFileName, CancellationToken cancellationToken)
    {
        (string id, _) = await GetFileInfoAsync(remoteFileName, cancellationToken);
        await ExecuteAsync("/file/remove", cancellationToken, $"numbers={id}");
    }

    private async Task<byte[]> ReadFileChunkAsync(string remoteFileName, long offset, int chunkSize, CancellationToken cancellationToken)
    {
        EnsureConnected();
        await WriteSentenceAsync([
            "/file/read",
            $"=file={remoteFileName}",
            $"=offset={offset}",
            $"=chunk-size={chunkSize}"
        ], cancellationToken);

        using var data = new MemoryStream(chunkSize);
        while (true)
        {
            List<byte[]> sentence = await ReadSentenceBytesAsync(cancellationToken);
            if (sentence.Count == 0) continue;
            string type = Encoding.UTF8.GetString(sentence[0]);
            foreach (byte[] word in sentence.Skip(1))
            {
                const int dataPrefixLength = 6;
                if (HasDataPrefix(word)) data.Write(word, dataPrefixLength, word.Length - dataPrefixLength);
            }
            if (type is "!trap" or "!fatal")
            {
                string message = sentence.Skip(1).Select(word => Encoding.UTF8.GetString(word))
                    .FirstOrDefault(x => x.StartsWith("=message=", StringComparison.OrdinalIgnoreCase))?[9..] ?? type;
                throw new RouterOsApiException(message);
            }
            if (type == "!done") return data.ToArray();
        }
    }

    private static bool HasDataPrefix(byte[] word)
        => word.Length >= 6 && word[0] == (byte)'=' && word[1] == (byte)'d' &&
           word[2] == (byte)'a' && word[3] == (byte)'t' && word[4] == (byte)'a' && word[5] == (byte)'=';

    private async Task LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApiReply> first = await ExecuteAsync("/login", cancellationToken, $"name={username}", $"password={password}");
        string? challenge = first.SelectMany(x => x.Attributes).FirstOrDefault(x => x.Key.Equals("ret", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrEmpty(challenge)) return;

        byte[] challengeBytes = Convert.FromHexString(challenge);
        byte[] pass = Encoding.UTF8.GetBytes(password);
        byte[] input = new byte[1 + pass.Length + challengeBytes.Length];
        Buffer.BlockCopy(pass, 0, input, 1, pass.Length);
        Buffer.BlockCopy(challengeBytes, 0, input, 1 + pass.Length, challengeBytes.Length);
        string response = "00" + Convert.ToHexString(MD5.HashData(input)).ToLowerInvariant();
        await ExecuteAsync("/login", cancellationToken, $"name={username}", $"response={response}");
        Array.Clear(pass, 0, pass.Length);
        Array.Clear(input, 0, input.Length);
    }

    private async Task WriteSentenceAsync(IEnumerable<string> words, CancellationToken cancellationToken)
    {
        foreach (string word in words)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(word);
            await WriteLengthAsync(bytes.Length, cancellationToken);
            await _stream!.WriteAsync(bytes, cancellationToken);
        }
        await _stream!.WriteAsync(new byte[] { 0 }, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<List<string>> ReadSentenceAsync(CancellationToken cancellationToken)
    {
        List<byte[]> raw = await ReadSentenceBytesAsync(cancellationToken);
        return raw.Select(word => Encoding.UTF8.GetString(word)).ToList();
    }

    private async Task<List<byte[]>> ReadSentenceBytesAsync(CancellationToken cancellationToken)
    {
        var words = new List<byte[]>();
        while (true)
        {
            int length = await ReadLengthAsync(cancellationToken);
            if (length == 0) return words;
            byte[] buffer = new byte[length];
            await ReadExactlyAsync(buffer, cancellationToken);
            words.Add(buffer);
        }
    }

    private async Task WriteLengthAsync(int length, CancellationToken ct)
    {
        byte[] b;
        if (length < 0x80) b = [(byte)length];
        else if (length < 0x4000) b = [(byte)((length >> 8) | 0x80), (byte)length];
        else if (length < 0x200000) b = [(byte)((length >> 16) | 0xC0), (byte)(length >> 8), (byte)length];
        else if (length < 0x10000000) b = [(byte)((length >> 24) | 0xE0), (byte)(length >> 16), (byte)(length >> 8), (byte)length];
        else b = [0xF0, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length];
        await _stream!.WriteAsync(b, ct);
    }

    private async Task<int> ReadLengthAsync(CancellationToken ct)
    {
        int c = await ReadByteAsync(ct);
        if ((c & 0x80) == 0) return c;
        if ((c & 0xC0) == 0x80) return ((c & ~0xC0) << 8) | await ReadByteAsync(ct);
        if ((c & 0xE0) == 0xC0) return ((c & ~0xE0) << 16) | (await ReadByteAsync(ct) << 8) | await ReadByteAsync(ct);
        if ((c & 0xF0) == 0xE0) return ((c & ~0xF0) << 24) | (await ReadByteAsync(ct) << 16) | (await ReadByteAsync(ct) << 8) | await ReadByteAsync(ct);
        if ((c & 0xF8) == 0xF0) return (await ReadByteAsync(ct) << 24) | (await ReadByteAsync(ct) << 16) | (await ReadByteAsync(ct) << 8) | await ReadByteAsync(ct);
        throw new InvalidDataException("Invalid RouterOS API word length.");
    }

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        byte[] b = new byte[1];
        int n = await _stream!.ReadAsync(b, ct);
        if (n == 0) throw new EndOfStreamException();
        return b[0];
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await _stream!.ReadAsync(buffer.AsMemory(offset), ct);
            if (n == 0) throw new EndOfStreamException();
            offset += n;
        }
    }

    private void EnsureConnected()
    {
        if (_stream is null) throw new InvalidOperationException("Not connected to a router.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _tcp.Dispose();
    }
}

public sealed class RouterOsApiException(string message) : Exception(message);
