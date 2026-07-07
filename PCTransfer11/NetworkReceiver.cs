using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PCTransfer11.Services;

/// <summary>
/// De ontvangende kant van een netwerkoverdracht. Luistert op een TCP-poort
/// voor het binnenkomende pakket, en beantwoordt tegelijk UDP-discovery-
/// broadcasts van zendende pc's zodat die deze machine automatisch kunnen
/// vinden op het lokale netwerk.
/// </summary>
public sealed class NetworkReceiver
{
    public const int DefaultTcpPort = 51715;
    public const int DiscoveryPort = 51716;
    private const string DiscoveryRequestMagic = "PCTRANSFER11_DISCOVER";
    private const string DiscoveryReplyMagic = "PCTRANSFER11_HERE";

    private readonly IProgress<string> _log;

    public NetworkReceiver(IProgress<string> log)
    {
        _log = log;
    }

    /// <summary>
    /// Draait op de achtergrond en beantwoordt discoveryverzoeken totdat
    /// <paramref name="ct"/> wordt geannuleerd. Aanroepen als "fire and forget"
    /// task zodra de gebruiker naar het ontvangst-tabblad gaat.
    /// </summary>
    public async Task RunDiscoveryResponderAsync(int tcpPort, CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                string text = Encoding.UTF8.GetString(result.Buffer);
                if (text != DiscoveryRequestMagic) continue;

                string reply = $"{DiscoveryReplyMagic}|{Environment.MachineName}|{tcpPort}";
                byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                await udp.SendAsync(replyBytes, replyBytes.Length, result.RemoteEndPoint);
            }
        }
        catch (Exception ex)
        {
            _log.Report($"Discovery-service gestopt: {ex.Message}");
        }
    }

    /// <summary>
    /// Wacht op één inkomende verbinding, ontvangt het pakket en slaat het op
    /// als <paramref name="saveAsPath"/>. Retourneert zodra de overdracht klaar is.
    /// </summary>
    public async Task ReceiveOnceAsync(string saveAsPath, IProgress<double> progress, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, DefaultTcpPort);
        listener.Start();
        try
        {
            _log.Report($"Wachten op verbinding op poort {DefaultTcpPort} ...");
            using var client = await listener.AcceptTcpClientAsync(ct);
            _log.Report($"Verbonden met {client.Client.RemoteEndPoint}. Ontvangst gestart ...");

            using var networkStream = client.GetStream();
            byte[] lengthBuffer = new byte[8];
            await ReadExactAsync(networkStream, lengthBuffer, ct);
            long totalBytes = BitConverter.ToInt64(lengthBuffer, 0);

            await using var fileStream = File.Create(saveAsPath);
            byte[] buffer = new byte[81920];
            long received = 0;
            while (received < totalBytes)
            {
                int toRead = (int)Math.Min(buffer.Length, totalBytes - received);
                int read = await networkStream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (read == 0) throw new IOException("Verbinding werd onverwacht verbroken tijdens de overdracht.");
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                progress.Report(totalBytes == 0 ? 1.0 : (double)received / totalBytes);
            }

            _log.Report("Ontvangst voltooid.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) throw new IOException("Verbinding werd onverwacht verbroken.");
            offset += read;
        }
    }
}
