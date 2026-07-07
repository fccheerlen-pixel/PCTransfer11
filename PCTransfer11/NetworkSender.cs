using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PCTransfer11.Models;

namespace PCTransfer11.Services;

/// <summary>
/// De zendende kant van een netwerkoverdracht: vindt ontvangende pc's op het
/// lokale netwerk via UDP-broadcast, en stuurt daarna het pakket via TCP.
/// </summary>
public sealed class NetworkSender
{
    private const string DiscoveryRequestMagic = "PCTRANSFER11_DISCOVER";
    private const string DiscoveryReplyMagic = "PCTRANSFER11_HERE";

    /// <summary>
    /// Stuurt een UDP-broadcast het lokale netwerk op en verzamelt gedurende
    /// <paramref name="timeoutMs"/> milliseconden alle reacties van draaiende
    /// PCTransfer11-ontvangers.
    /// </summary>
    public static async Task<List<DiscoveredReceiver>> DiscoverAsync(int timeoutMs, CancellationToken ct)
    {
        var found = new List<DiscoveredReceiver>();
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        byte[] requestBytes = Encoding.UTF8.GetBytes(DiscoveryRequestMagic);
        await udp.SendAsync(requestBytes, requestBytes.Length,
            new IPEndPoint(IPAddress.Broadcast, NetworkReceiver.DiscoveryPort));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(timeoutCts.Token);
                string text = Encoding.UTF8.GetString(result.Buffer);
                string[] parts = text.Split('|');
                if (parts.Length == 3 && parts[0] == DiscoveryReplyMagic &&
                    int.TryParse(parts[2], out int port))
                {
                    string ip = result.RemoteEndPoint.Address.ToString();
                    if (!found.Any(f => f.IpAddress == ip))
                        found.Add(new DiscoveredReceiver(ip, parts[1], port));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // timeout bereikt - normale manier om de zoektocht af te ronden
        }

        return found;
    }

    /// <summary>Verstuurt het pakketbestand naar de opgegeven ontvanger.</summary>
    public async Task SendAsync(string ipAddress, int tcpPort, string filePath, IProgress<double> progress,
        IProgress<string> log, CancellationToken ct)
    {
        using var client = new TcpClient();
        log.Report($"Verbinden met {ipAddress}:{tcpPort} ...");
        await client.ConnectAsync(ipAddress, tcpPort, ct);
        log.Report("Verbonden. Overdracht gestart ...");

        using var networkStream = client.GetStream();
        await using var fileStream = File.OpenRead(filePath);
        long totalBytes = fileStream.Length;

        byte[] lengthBytes = BitConverter.GetBytes(totalBytes);
        await networkStream.WriteAsync(lengthBytes, ct);

        byte[] buffer = new byte[81920];
        long sent = 0;
        int read;
        while ((read = await fileStream.ReadAsync(buffer, ct)) > 0)
        {
            await networkStream.WriteAsync(buffer.AsMemory(0, read), ct);
            sent += read;
            progress.Report(totalBytes == 0 ? 1.0 : (double)sent / totalBytes);
        }

        log.Report("Verzending voltooid.");
    }
}
