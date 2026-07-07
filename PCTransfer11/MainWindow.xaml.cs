using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PCTransfer11.Models;
using PCTransfer11.Services;

namespace PCTransfer11;

public partial class MainWindow : Window
{
    private readonly List<FileSelectionItem> _fileItems = new();
    private readonly List<AppProfile> _appProfiles = new();

    private readonly NetworkReceiver _networkReceiver;
    private readonly NetworkSender _networkSender = new();

    private CancellationTokenSource? _discoveryResponderCts;

    private readonly Progress<string> _logProgress;
    private readonly Progress<double> _percentProgress;

    public MainWindow()
    {
        InitializeComponent();

        _logProgress = new Progress<string>(Log);
        _percentProgress = new Progress<double>(p => TransferProgressBar.Value = p);
        _networkReceiver = new NetworkReceiver(_logProgress);

        InitializeFileItems();
        InitializeAppProfiles();

        FilesItemsControl.ItemsSource = _fileItems;
        AppsItemsControl.ItemsSource = _appProfiles;

        UpdateReceiverInfoText();
        StartDiscoveryResponderIfReceiver();

        Closing += (_, _) => _discoveryResponderCts?.Cancel();
    }

    private void InitializeFileItems()
    {
        _fileItems.Add(FileSelectionItem.ForSpecialFolder("Documenten", Environment.SpecialFolder.MyDocuments));
        _fileItems.Add(FileSelectionItem.ForSpecialFolder("Afbeeldingen", Environment.SpecialFolder.MyPictures));
        _fileItems.Add(FileSelectionItem.ForSpecialFolder("Bureaublad", Environment.SpecialFolder.DesktopDirectory));
        _fileItems.Add(FileSelectionItem.ForSpecialFolder("Video's", Environment.SpecialFolder.MyVideos));
        _fileItems.Add(FileSelectionItem.ForSpecialFolder("Muziek", Environment.SpecialFolder.MyMusic));

        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        _fileItems.Add(new FileSelectionItem { DisplayName = "Downloads", Path = downloads });

        foreach (var item in _fileItems)
            item.IsChecked = item.Exists;
    }

    private void InitializeAppProfiles()
    {
        _appProfiles.AddRange(KnownApps.GetAll());
        foreach (var app in _appProfiles)
            app.IsChecked = app.IsAvailable;
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        });
    }

    // ================= TAB 1: SELECTEREN =================

    private void AddCustomFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Kies een map om mee over te zetten" };
        if (dialog.ShowDialog() == true)
        {
            var item = new FileSelectionItem
            {
                DisplayName = Path.GetFileName(dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar)),
                Path = dialog.FolderName,
                IsChecked = true
            };
            _fileItems.Add(item);
            FilesItemsControl.ItemsSource = null;
            FilesItemsControl.ItemsSource = _fileItems;
        }
    }

    // ================= TAB 2: OVERZETTEN =================

    private void ModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (NetworkPanel == null || BackupPanel == null) return; // tijdens initialisatie van XAML
        bool network = ModeNetworkRadio.IsChecked == true;
        NetworkPanel.Visibility = network ? Visibility.Visible : Visibility.Collapsed;
        BackupPanel.Visibility = network ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RoleRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (ReceiverPanel == null || SenderPanel == null) return;
        bool isReceiver = RoleReceiverRadio.IsChecked == true;
        ReceiverPanel.Visibility = isReceiver ? Visibility.Visible : Visibility.Collapsed;
        SenderPanel.Visibility = isReceiver ? Visibility.Collapsed : Visibility.Visible;

        if (isReceiver)
            StartDiscoveryResponderIfReceiver();
        else
            _discoveryResponderCts?.Cancel();
    }

    private void StartDiscoveryResponderIfReceiver()
    {
        _discoveryResponderCts?.Cancel();
        _discoveryResponderCts = new CancellationTokenSource();
        _ = _networkReceiver.RunDiscoveryResponderAsync(NetworkReceiver.DefaultTcpPort, _discoveryResponderCts.Token);
    }

    private void UpdateReceiverInfoText()
    {
        string ip = GetLocalIPv4() ?? "onbekend";
        ReceiverInfoText.Text = $"Deze pc heet '{Environment.MachineName}' en is te bereiken op {ip}. " +
                                 "Zorg dat beide pc's op hetzelfde (Wifi-)netwerk zitten en klik daarna hieronder op 'Start ontvangen'. " +
                                 "Windows kan de eerste keer om firewall-toestemming vragen - klik dan op 'Toegang toestaan'.";
    }

    private static string? GetLocalIPv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async void StartReceive_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            string tempPackagePath = Path.Combine(Path.GetTempPath(), "PCTransfer11_ontvangen.pctbackup");
            await _networkReceiver.ReceiveOnceAsync(tempPackagePath, _percentProgress, CancellationToken.None);

            var result = MessageBox.Show(
                "Het pakket is ontvangen. Nu meteen terugzetten op deze pc?",
                "PCTransfer11", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var restorer = new PackageRestorer(_logProgress);
                await restorer.RestoreAsync(tempPackagePath, overwriteExisting: true, CancellationToken.None);
                MessageBox.Show("Terugzetten voltooid.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens ontvangst: {ex.Message}");
            MessageBox.Show($"Er ging iets mis tijdens de ontvangst:\n{ex.Message}", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DiscoverReceivers_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        Log("Zoeken naar pc's op het netwerk ...");
        try
        {
            var found = await NetworkSender.DiscoverAsync(2500, CancellationToken.None);
            ReceiversComboBox.ItemsSource = found;
            if (found.Count > 0)
            {
                ReceiversComboBox.SelectedIndex = 0;
                Log($"{found.Count} pc('s) gevonden.");
            }
            else
            {
                Log("Geen pc's gevonden. Controleer of de andere pc op 'Start ontvangen' staat en op hetzelfde netwerk zit.");
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void StartSend_Click(object sender, RoutedEventArgs e)
    {
        string? ip = (ReceiversComboBox.SelectedItem as DiscoveredReceiver)?.IpAddress;
        int port = (ReceiversComboBox.SelectedItem as DiscoveredReceiver)?.TcpPort ?? NetworkReceiver.DefaultTcpPort;

        if (string.IsNullOrWhiteSpace(ip))
            ip = ManualIpTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(ip))
        {
            MessageBox.Show("Kies een gevonden pc of vul een IP-adres in.", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            string tempPackagePath = Path.Combine(Path.GetTempPath(), "PCTransfer11_te_verzenden.pctbackup");
            var builder = new PackageBuilder(_logProgress);
            await builder.BuildAsync(GetCheckedFiles(), GetCheckedApps(), tempPackagePath, CancellationToken.None);

            await _networkSender.SendAsync(ip, port, tempPackagePath, _percentProgress, _logProgress, CancellationToken.None);

            MessageBox.Show("Verzending voltooid.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens verzenden: {ex.Message}");
            MessageBox.Show($"Er ging iets mis tijdens het verzenden:\n{ex.Message}", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Back-up opslaan als",
            Filter = "PCTransfer-back-up (*.pctbackup)|*.pctbackup",
            FileName = $"PCTransfer_backup_{DateTime.Now:yyyyMMdd}.pctbackup"
        };
        if (dialog.ShowDialog() != true) return;

        SetBusy(true);
        try
        {
            var builder = new PackageBuilder(_logProgress);
            await builder.BuildAsync(GetCheckedFiles(), GetCheckedApps(), dialog.FileName, CancellationToken.None);
            MessageBox.Show("Back-up gemaakt.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens back-up maken: {ex.Message}");
            MessageBox.Show($"Er ging iets mis:\n{ex.Message}", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Back-upbestand kiezen",
            Filter = "PCTransfer-back-up (*.pctbackup)|*.pctbackup|Alle bestanden (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "Bestaande bestanden met dezelfde naam worden overschreven. Doorgaan?",
            "PCTransfer11", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            var restorer = new PackageRestorer(_logProgress);
            await restorer.RestoreAsync(dialog.FileName, overwriteExisting: true, CancellationToken.None);
            MessageBox.Show("Terugzetten voltooid.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens terugzetten: {ex.Message}");
            MessageBox.Show($"Er ging iets mis:\n{ex.Message}", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private IEnumerable<FileSelectionItem> GetCheckedFiles() => _fileItems.Where(f => f.IsChecked && f.Exists);
    private IEnumerable<AppProfile> GetCheckedApps() => _appProfiles.Where(a => a.IsChecked && a.IsAvailable);

    private void SetBusy(bool busy)
    {
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        if (busy) TransferProgressBar.Value = 0;
    }
}
