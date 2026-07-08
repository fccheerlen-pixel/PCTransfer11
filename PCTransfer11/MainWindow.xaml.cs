using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PCTransfer11.Models;
using PCTransfer11.Services;

namespace PCTransfer11;

public partial class MainWindow : Window
{
    private readonly List<FileSelectionItem> _fileItems = new();
    private readonly List<AppProfile> _appProfiles = new();
    private readonly List<RestoreSelectionItem> _restoreItems = new();

    private string? _selectedBackupFolder;
    private PackageManifest? _selectedBackupManifest;

    private readonly NetworkReceiver _networkReceiver;
    private readonly NetworkSender _networkSender = new();

    private CancellationTokenSource? _discoveryResponderCts;

    private readonly Progress<string> _logProgress;
    private readonly Progress<double> _percentProgress;

    private CancellationTokenSource? _operationCts;

    public MainWindow()
    {
        InitializeComponent();

        _logProgress = new Progress<string>(Log);
        _percentProgress = new Progress<double>(p =>
        {
            TransferProgressBar.Value = p;
            ProgressPercentText.Text = $"{p * 100:0}%";
        });
        _networkReceiver = new NetworkReceiver(_logProgress);

        InitializeFileItems();
        InitializeAppProfiles();

        FilesItemsControl.ItemsSource = _fileItems;

        var appsView = (ListCollectionView)CollectionViewSource.GetDefaultView(_appProfiles);
        appsView.GroupDescriptions.Clear();
        appsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppProfile.Category)));
        appsView.CustomSort = Comparer<AppProfile>.Create((a, b) =>
            string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase));
        AppsItemsControl.ItemsSource = appsView;

        UpdateReceiverInfoText();
        StartDiscoveryResponderIfReceiver();
        InitializeAboutTab();

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

        // Let op: bewust GEEN "openbare/gedeelde" (Public, C:\Users\Public\...) mappen
        // meer toevoegen. Er wordt alleen nog van het eigen gebruikersprofiel
        // gebackupt, niet van het openbare/gedeelde profiel.

        foreach (var item in _fileItems)
            item.IsChecked = item.Exists;
    }

    private void InitializeAppProfiles()
    {
        _appProfiles.AddRange(KnownApps.GetAll());
        foreach (var app in _appProfiles)
            app.IsChecked = app.IsAvailable;
    }

    private void InitializeAboutTab()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersionText.Text = version != null
            ? $"Versie {version.Major}.{version.Minor}.{version.Build}"
            : "Versie onbekend";
    }

    private void AppsSelectAll_Click(object sender, RoutedEventArgs e) => SetAllAppChecks(true);
    private void AppsSelectNone_Click(object sender, RoutedEventArgs e) => SetAllAppChecks(false);

    private void SetAllAppChecks(bool value)
    {
        foreach (var app in _appProfiles.Where(a => a.IsAvailable))
            app.IsChecked = value;
        CollectionViewSource.GetDefaultView(_appProfiles).Refresh();
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
        var ct = BeginOperation();
        try
        {
            string tempPackagePath = Path.Combine(Path.GetTempPath(), "PCTransfer11_ontvangen.pctbackup");
            await _networkReceiver.ReceiveOnceAsync(tempPackagePath, _percentProgress, ct);

            var result = MessageBox.Show(
                "Het pakket is ontvangen. Nu meteen terugzetten op deze pc?",
                "PCTransfer11", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var restorer = new PackageRestorer(_logProgress);
                await restorer.RestoreZipAsync(tempPackagePath, overwriteExisting: true, _percentProgress, ct);
                MessageBox.Show("Terugzetten voltooid.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            Log("Ontvangst gestopt door gebruiker.");
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens ontvangst: {ex.Message}");
            MessageBox.Show($"Er ging iets mis tijdens de ontvangst:\n{ex.Message}", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void DiscoverReceivers_Click(object sender, RoutedEventArgs e)
    {
        var ct = BeginOperation();
        Log("Zoeken naar pc's op het netwerk ...");
        try
        {
            var found = await NetworkSender.DiscoverAsync(2500, ct);
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
        catch (OperationCanceledException)
        {
            Log("Zoeken gestopt door gebruiker.");
        }
        finally
        {
            EndOperation();
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

        var ct = BeginOperation();
        try
        {
            string tempPackagePath = Path.Combine(Path.GetTempPath(), "PCTransfer11_te_verzenden.pctbackup");
            var builder = new PackageBuilder(_logProgress);
            await builder.BuildToZipAsync(GetCheckedFiles(), GetCheckedApps(), tempPackagePath, ct);

            await _networkSender.SendAsync(ip, port, tempPackagePath, _percentProgress, _logProgress, ct);

            MessageBox.Show("Verzending voltooid.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Verzending gestopt door gebruiker.");
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens verzenden: {ex.Message}");
            MessageBox.Show($"Er ging iets mis tijdens het verzenden:\n{ex.Message}", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Kies waar de back-upmap moet komen" };
        if (dialog.ShowDialog() != true) return;

        string backupFolder = Path.Combine(dialog.FolderName, $"PCTransfer_backup_{DateTime.Now:yyyyMMdd_HHmmss}");

        var ct = BeginOperation();
        try
        {
            var builder = new PackageBuilder(_logProgress);
            await builder.BuildToDirectoryAsync(GetCheckedFiles(), GetCheckedApps(), backupFolder, _percentProgress, ct);
            MessageBox.Show(
                $"Back-up gemaakt in:\n{backupFolder}\n\nJe kan deze map direct openen, bekijken en bewerken in Verkenner.",
                "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Back-up gestopt door gebruiker.");
            MessageBox.Show($"Back-up gestopt. Wat al gekopieerd was staat nog in:\n{backupFolder}",
                "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens back-up maken: {ex.Message}");
            MessageBox.Show($"Er ging iets mis:\n{ex.Message}", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    // ================= TERUGZETTEN (selectief) =================

    private void ChooseBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Kies een PCTransfer11-back-upmap" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var manifest = PackageRestorer.LoadManifest(dialog.FolderName);
            _selectedBackupFolder = dialog.FolderName;
            _selectedBackupManifest = manifest;

            _restoreItems.Clear();
            foreach (var f in manifest.Files)
                _restoreItems.Add(new RestoreSelectionItem { DisplayName = f.DisplayName, Key = f.PackagePath, IsSetting = false, IsChecked = true });
            foreach (var s in manifest.Settings)
                _restoreItems.Add(new RestoreSelectionItem { DisplayName = $"Instellingen: {s.DisplayName}", Key = s.AppId, IsSetting = true, IsChecked = true });

            RestoreItemsControl.ItemsSource = null;
            RestoreItemsControl.ItemsSource = _restoreItems;
            RestoreSelectionPanel.Visibility = Visibility.Visible;
            SelectedBackupFolderText.Text = $"Gekozen back-up: {dialog.FolderName} ({manifest.CreatedAtUtc:g} UTC, van '{manifest.CreatedByMachine}')";
            Log($"Back-upmap ingelezen: {dialog.FolderName} ({_restoreItems.Count} items gevonden).");
        }
        catch (Exception ex)
        {
            RestoreSelectionPanel.Visibility = Visibility.Collapsed;
            _selectedBackupFolder = null;
            _selectedBackupManifest = null;
            MessageBox.Show($"Kon deze map niet als PCTransfer11-back-up inlezen:\n{ex.Message}", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreSelectAll_Click(object sender, RoutedEventArgs e) => SetAllRestoreChecks(true);
    private void RestoreSelectNone_Click(object sender, RoutedEventArgs e) => SetAllRestoreChecks(false);

    private void SetAllRestoreChecks(bool value)
    {
        foreach (var item in _restoreItems)
            item.IsChecked = value;
        RestoreItemsControl.ItemsSource = null;
        RestoreItemsControl.ItemsSource = _restoreItems;
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBackupFolder == null || _selectedBackupManifest == null)
        {
            MessageBox.Show("Kies eerst een back-upmap.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var checkedFilePaths = _restoreItems.Where(i => !i.IsSetting && i.IsChecked).Select(i => i.Key).ToHashSet();
        var checkedAppIds = _restoreItems.Where(i => i.IsSetting && i.IsChecked).Select(i => i.Key).ToHashSet();

        if (checkedFilePaths.Count == 0 && checkedAppIds.Count == 0)
        {
            MessageBox.Show("Vink minstens één item aan om terug te zetten.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            "Bestaande bestanden met dezelfde naam worden overschreven. Doorgaan?",
            "PCTransfer11", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var ct = BeginOperation();
        try
        {
            var restorer = new PackageRestorer(_logProgress);
            await restorer.RestoreFromFolderAsync(
                _selectedBackupFolder, _selectedBackupManifest,
                checkedFilePaths, checkedAppIds,
                overwriteExisting: true, _percentProgress, ct);
            MessageBox.Show("Terugzetten voltooid.", "PCTransfer11", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Terugzetten gestopt door gebruiker.");
        }
        catch (Exception ex)
        {
            Log($"Fout tijdens terugzetten: {ex.Message}");
            MessageBox.Show($"Er ging iets mis tijdens het terugzetten:\n{ex.Message}", "PCTransfer11",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private IEnumerable<FileSelectionItem> GetCheckedFiles() => _fileItems.Where(f => f.IsChecked && f.Exists);
    private IEnumerable<AppProfile> GetCheckedApps() => _appProfiles.Where(a => a.IsChecked && a.IsAvailable);

    /// <summary>
    /// Start een nieuwe annuleerbare operatie: maakt een verse CancellationTokenSource,
    /// schakelt de Stop-knop in en reset de voortgangsbalk. Geef het geretourneerde
    /// token mee aan de service-aanroep in plaats van CancellationToken.None.
    /// </summary>
    private CancellationToken BeginOperation()
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        SetBusy(true);
        return _operationCts.Token;
    }

    private void EndOperation()
    {
        SetBusy(false);
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts == null || _operationCts.IsCancellationRequested) return;
        Log("Bezig met stoppen ... (kan even duren tot het huidige bestand klaar is)");
        StopButton.IsEnabled = false;
        _operationCts.Cancel();
    }

    private void SetBusy(bool busy)
    {
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        StopButton.IsEnabled = busy;
        if (busy)
        {
            TransferProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
        }
    }
}
