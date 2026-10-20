using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Magnetar.Photo.Heimdall.Desktop;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Services;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Libraries onboarding screen.
/// Has no dependency on Avalonia UI types: all platform interaction is
/// injected via delegates (folderPicker) from the composition root.
/// </summary>
public sealed class LibraryScreenViewModel : INotifyPropertyChanged
{
    private readonly LibraryOnboardingPresentationLogicService _facade;
    private string _displayName = "My photo library";
    private string _rootPath = string.Empty;
    private string _statusMessage = "Add a library folder to start cataloguing your photos.";
    private string? _errorMessage;
    private int _cataloguedCount;
    private string? _lastScannedLibraryName;
    private string? _lastScannedRootPath;
    private bool _isBusy;
    private RemoteDuplicateAnalysisPresentationLogicService? _remoteAnalysis;
    private RemoteSshConnectionSettings _remoteSshSettings;
    private readonly Func<Task<string?>>? _sshKeyPicker;
    private readonly Func<RemoteSshConnectionSettings, (RemoteDuplicateAnalysisPresentationLogicService? Service, string? Error)>? _configureRemote;
    private string _remoteRootId;
    private string _remoteStatusMessage;
    private string? _remoteErrorMessage;
    private int _remoteDuplicateGroupCount;
    private int _remoteCandidateFileCount;
    private int _remoteScannedAssetCount;
    private int _remoteHashesComputed;
    private int _remoteIssueCount;
    private int _remoteMaximumConcurrency = 1;
    private int _remoteEffectiveConcurrency;
    private IReadOnlyList<RemoteDuplicateGroupDisplayModel> _remoteDuplicateGroups = [];
    private IReadOnlyList<AgentRootRemoteContractsModel> _remoteAllowedRoots = [];
    private AgentRootRemoteContractsModel? _sourceRootCandidate;
    private AgentRootRemoteContractsModel? _sourceRootToRemove;
    private IReadOnlyList<RemoteLibraryOrganizerProfileViewModel> _organizerProfiles = [];
    private bool _isRemoteConnected;
    private bool _hasRemoteResult;

    /// <param name="facade">Presentation-layer facade; the only dependency this VM knows about.</param>
    /// <param name="folderPicker">
    ///   Optional async delegate that returns an absolute folder path chosen by the user,
    ///   or <see langword="null"/> when the dialog is cancelled.
    ///   Pass <see langword="null"/> (default) to disable the folder-picker button.
    /// </param>
    public LibraryScreenViewModel(
        LibraryOnboardingPresentationLogicService facade,
        Func<Task<string?>>? folderPicker = null,
        RemoteDuplicateAnalysisPresentationLogicService? remoteAnalysis = null,
        string remoteRootId = "",
        string? remoteConfigurationError = null,
        RemoteSshConnectionSettings? remoteSshSettings = null,
        Func<Task<string?>>? sshKeyPicker = null,
        Func<RemoteSshConnectionSettings, (RemoteDuplicateAnalysisPresentationLogicService? Service, string? Error)>? configureRemote = null)
    {
        _facade = facade;
        _remoteAnalysis = remoteAnalysis;
        _remoteSshSettings = remoteSshSettings ?? new RemoteSshConnectionSettings(RootId: remoteRootId);
        _sshKeyPicker = sshKeyPicker;
        _configureRemote = configureRemote;
        _remoteRootId = remoteRootId;
        _remoteStatusMessage = remoteAnalysis is null
            ? remoteConfigurationError ?? "Configura el agente SSH para habilitar análisis remoto."
            : "SSH configurado. Conecta para cargar las raíces autorizadas del agente NAS.";

        AddAndScanCommand = new AsyncCommand(AddAndScanAsync, CanAddAndScan);
        AnalyzeRemoteDuplicatesCommand = new AsyncCommand(AnalyzeRemoteDuplicatesAsync, CanAnalyzeRemoteDuplicates);
        ConnectRemoteAgentCommand = new AsyncCommand(ConnectRemoteAgentAsync, CanConnectRemoteAgent);
        ChooseSshKeyCommand = sshKeyPicker is not null
            ? new AsyncCommand(ChooseSshKeyAsync, () => !IsBusy)
            : new AsyncCommand(() => Task.CompletedTask, () => false);
        SaveRemoteSshSettingsCommand = new AsyncCommand(SaveRemoteSshSettingsAsync, () => !IsBusy);
        CreateDedicatedSshKeyCommand = new AsyncCommand(CreateDedicatedSshKeyAsync, () => !IsBusy);
        AddSourceRootCommand = new AsyncCommand(AddSourceRootAsync, () => !IsBusy && SourceRootCandidate is not null && SelectedSourceRoots.All(root => root.Id != SourceRootCandidate.Id));
        RemoveSourceRootCommand = new AsyncCommand(RemoveSourceRootAsync, () => !IsBusy && SourceRootToRemove is not null);

        ChooseFolderCommand = folderPicker is not null
            ? new AsyncCommand(async () =>
            {
                var picked = await folderPicker();
                if (picked is not null)
                    RootPath = picked;
            }, () => !IsBusy)
            : new AsyncCommand(() => Task.CompletedTask, () => false); // disabled — no picker provided
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand AddAndScanCommand { get; }
    public ICommand ChooseFolderCommand { get; }
    public ICommand AnalyzeRemoteDuplicatesCommand { get; }
    public ICommand ConnectRemoteAgentCommand { get; }
    public ICommand ChooseSshKeyCommand { get; }
    public ICommand SaveRemoteSshSettingsCommand { get; }
    public ICommand CreateDedicatedSshKeyCommand { get; }
    public ICommand AddSourceRootCommand { get; }
    public ICommand RemoveSourceRootCommand { get; }
    public ObservableCollection<AgentRootRemoteContractsModel> SelectedSourceRoots { get; } = [];
    public IReadOnlyList<string> OrganizerFolderPatternOptions { get; } = ["{camera}/{year}/{month}", "{year}/{month}/{camera}", "{year}/{month}", "{camera}/{year}"];
    public string ConsolidationCapabilityStatus => "Configura destino, fuentes y esquema por biblioteca. El plan remoto se habilitará cuando Heimdall exponga la operación de consolidación.";

    public string RemoteSshHost { get => _remoteSshSettings.Host; set => UpdateSshSettings(_remoteSshSettings with { Host = value ?? "" }); }
    public int RemoteSshPort { get => _remoteSshSettings.Port; set => UpdateSshSettings(_remoteSshSettings with { Port = value }); }
    public string RemoteSshUsername { get => _remoteSshSettings.Username; set => UpdateSshSettings(_remoteSshSettings with { Username = value ?? "" }); }
    public string RemoteSshPrivateKeyPath { get => _remoteSshSettings.PrivateKeyPath; set => UpdateSshSettings(_remoteSshSettings with { PrivateKeyPath = value ?? "" }); }
    public string RemoteSshHostKeySha256 { get => _remoteSshSettings.HostKeySha256; set => UpdateSshSettings(_remoteSshSettings with { HostKeySha256 = value ?? "" }); }
    public string RemoteSshAgentCommand { get => _remoteSshSettings.AgentCommand; set => UpdateSshSettings(_remoteSshSettings with { AgentCommand = value ?? "" }); }
    public string RemoteSshRootId { get => _remoteSshSettings.RootId; set => UpdateSshSettings(_remoteSshSettings with { RootId = value ?? "" }); }
    public string RemoteSshKeyFileName => string.IsNullOrWhiteSpace(RemoteSshPrivateKeyPath) ? "No se ha elegido una llave privada" : Path.GetFileName(RemoteSshPrivateKeyPath);
    public string RemoteSshKeyPathDisplay => string.IsNullOrWhiteSpace(RemoteSshPrivateKeyPath) ? "" : RemoteSshPrivateKeyPath;

    public string RemoteRootId
    {
        get => _remoteRootId;
        set { if (SetField(ref _remoteRootId, value)) { OnPropertyChanged(nameof(IsRemoteRootAuthorized)); OnPropertyChanged(nameof(RemoteSelectedRoot)); OnPropertyChanged(nameof(SourceRootCandidates)); OnPropertyChanged(nameof(SelectedOrganizerProfile)); SourceRootCandidate = SourceRootCandidates.FirstOrDefault(); RaiseCanExecuteChanged(); } }
    }

    public IReadOnlyList<AgentRootRemoteContractsModel> RemoteAllowedRoots
    {
        get => _remoteAllowedRoots;
        private set
        {
            if (SetField(ref _remoteAllowedRoots, value))
            {
                OnPropertyChanged(nameof(HasRemoteRoots));
                OnPropertyChanged(nameof(IsRemoteRootAuthorized));
                OnPropertyChanged(nameof(RemoteSelectedRoot));
                OnPropertyChanged(nameof(SourceRootCandidates));
            }
        }
    }
    public bool HasRemoteRoots => RemoteAllowedRoots.Count > 0;
    public IReadOnlyList<AgentRootRemoteContractsModel> SourceRootCandidates => RemoteAllowedRoots.Where(root => root.Id != RemoteRootId).ToArray();
    public IReadOnlyList<RemoteLibraryOrganizerProfileViewModel> OrganizerProfiles => _organizerProfiles;
    public RemoteLibraryOrganizerProfileViewModel? SelectedOrganizerProfile => _organizerProfiles.FirstOrDefault(profile => profile.RootId == RemoteRootId);
    public AgentRootRemoteContractsModel? SourceRootCandidate
    {
        get => _sourceRootCandidate;
        set { if (SetField(ref _sourceRootCandidate, value)) RaiseSourceCommands(); }
    }
    public AgentRootRemoteContractsModel? SourceRootToRemove
    {
        get => _sourceRootToRemove;
        set { if (SetField(ref _sourceRootToRemove, value)) RaiseSourceCommands(); }
    }
    public AgentRootRemoteContractsModel? RemoteSelectedRoot
    {
        get => RemoteAllowedRoots.FirstOrDefault(root => StringComparer.Ordinal.Equals(root.Id, RemoteRootId));
        set { if (value is not null) RemoteRootId = value.Id; }
    }
    public bool IsRemoteConnected { get => _isRemoteConnected; private set => SetField(ref _isRemoteConnected, value); }
    public bool IsRemoteRootAuthorized => RemoteAllowedRoots.Any(root => StringComparer.Ordinal.Equals(root.Id, RemoteRootId.Trim()));

    public string RemoteStatusMessage { get => _remoteStatusMessage; private set => SetField(ref _remoteStatusMessage, value); }
    public string? RemoteErrorMessage
    {
        get => _remoteErrorMessage;
        private set { if (SetField(ref _remoteErrorMessage, value)) OnPropertyChanged(nameof(HasRemoteError)); }
    }
    public bool HasRemoteError => !string.IsNullOrWhiteSpace(_remoteErrorMessage);
    public int RemoteDuplicateGroupCount { get => _remoteDuplicateGroupCount; private set => SetField(ref _remoteDuplicateGroupCount, value); }
    public int RemoteCandidateFileCount { get => _remoteCandidateFileCount; private set => SetField(ref _remoteCandidateFileCount, value); }
    public int RemoteScannedAssetCount { get => _remoteScannedAssetCount; private set => SetField(ref _remoteScannedAssetCount, value); }
    public int RemoteHashesComputed { get => _remoteHashesComputed; private set => SetField(ref _remoteHashesComputed, value); }
    public int RemoteIssueCount { get => _remoteIssueCount; private set => SetField(ref _remoteIssueCount, value); }
    public int RemoteMaximumConcurrency { get => _remoteMaximumConcurrency; private set => SetField(ref _remoteMaximumConcurrency, value); }
    public int RemoteEffectiveConcurrency { get => _remoteEffectiveConcurrency; private set => SetField(ref _remoteEffectiveConcurrency, value); }
    public IReadOnlyList<RemoteDuplicateGroupDisplayModel> RemoteDuplicateGroups
    {
        get => _remoteDuplicateGroups;
        private set
        {
            if (SetField(ref _remoteDuplicateGroups, value))
            {
                OnPropertyChanged(nameof(HasRemoteDuplicateGroups));
                OnPropertyChanged(nameof(HasNoRemoteDuplicateGroups));
            }
        }
    }
    public bool HasRemoteResult { get => _hasRemoteResult; private set => SetField(ref _hasRemoteResult, value); }
    public bool HasRemoteDuplicateGroups => _remoteDuplicateGroups.Count > 0;
    public bool HasNoRemoteDuplicateGroups => _remoteDuplicateGroups.Count == 0;

    // ── Input properties ─────────────────────────────────────────────────

    /// <summary>Human-readable name the user assigns to this library.</summary>
    public string DisplayName
    {
        get => _displayName;
        set { if (SetField(ref _displayName, value)) RaiseCanExecuteChanged(); }
    }

    /// <summary>Absolute path to the root folder of the library.</summary>
    public string RootPath
    {
        get => _rootPath;
        set { if (SetField(ref _rootPath, value)) RaiseCanExecuteChanged(); }
    }

    // ── Output / status properties ────────────────────────────────────────

    /// <summary>General status text shown below the form (scanning progress, completion notice, etc.).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    /// <summary>Human-readable error message from the last failed operation; <see langword="null"/> when no error.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary><see langword="true"/> when the last operation produced an error.</summary>
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>Number of media assets catalogued in the most recent successful scan.</summary>
    public int CataloguedCount
    {
        get => _cataloguedCount;
        private set
        {
            if (SetField(ref _cataloguedCount, value))
                OnPropertyChanged(nameof(HasCataloguedCount));
        }
    }

    /// <summary><see langword="true"/> after a successful scan that found at least one asset.</summary>
    public bool HasCataloguedCount => _cataloguedCount > 0;

    /// <summary>Library name from the most recent successful scan, never the editable draft.</summary>
    public string? LastScannedLibraryName
    {
        get => _lastScannedLibraryName;
        private set => SetField(ref _lastScannedLibraryName, value);
    }

    /// <summary>Library root from the most recent successful scan, never the editable draft.</summary>
    public string? LastScannedRootPath
    {
        get => _lastScannedRootPath;
        private set => SetField(ref _lastScannedRootPath, value);
    }

    /// <summary>
    ///   <see langword="true"/> while an operation is running.
    ///   Also used to disable input controls during scanning.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsInputEnabled));
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Convenience inverse of <see cref="IsBusy"/> for binding <c>IsEnabled</c> on input controls.</summary>
    public bool IsInputEnabled => !_isBusy;

    // ── Private implementation ────────────────────────────────────────────

    private bool CanAddAndScan() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        !string.IsNullOrWhiteSpace(RootPath);

    private bool CanAnalyzeRemoteDuplicates() =>
        !IsBusy && IsRemoteConnected && _remoteAnalysis is not null && IsRemoteRootAuthorized;

    private bool CanConnectRemoteAgent() => !IsBusy && _remoteAnalysis is not null;

    private void UpdateSshSettings(RemoteSshConnectionSettings settings)
    {
        _remoteSshSettings = settings;
        OnPropertyChanged(nameof(RemoteSshHost));
        OnPropertyChanged(nameof(RemoteSshPort));
        OnPropertyChanged(nameof(RemoteSshUsername));
        OnPropertyChanged(nameof(RemoteSshPrivateKeyPath));
        OnPropertyChanged(nameof(RemoteSshHostKeySha256));
        OnPropertyChanged(nameof(RemoteSshAgentCommand));
        OnPropertyChanged(nameof(RemoteSshRootId));
        OnPropertyChanged(nameof(RemoteSshKeyFileName));
        OnPropertyChanged(nameof(RemoteSshKeyPathDisplay));
    }

    private async Task ChooseSshKeyAsync()
    {
        var path = await _sshKeyPicker!();
        if (!string.IsNullOrWhiteSpace(path)) RemoteSshPrivateKeyPath = path;
    }

    private async Task CreateDedicatedSshKeyAsync()
    {
        IsBusy = true;
        RemoteErrorMessage = null;
        try
        {
            RemoteSshPrivateKeyPath = await SshIdentityGenerator.CreateDedicatedEd25519Async();
            RemoteStatusMessage = $"Llave Ed25519 dedicada creada en {RemoteSshPrivateKeyPath}. Su llave pública está en {RemoteSshPrivateKeyPath}.pub; instala esa parte en la NAS y guarda la conexión.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            RemoteErrorMessage = exception.Message;
            RemoteStatusMessage = "No se pudo crear la llave SSH dedicada.";
        }
        finally { IsBusy = false; }
    }

    private Task SaveRemoteSshSettingsAsync()
    {
        try
        {
            RemoteSshConnectionSettingsStore.Save(_remoteSshSettings);
            if (_configureRemote is not null)
            {
                var configured = _configureRemote(_remoteSshSettings);
                _remoteAnalysis = configured.Service;
                _remoteRootId = _remoteSshSettings.RootId;
                IsRemoteConnected = false;
                RemoteAllowedRoots = [];
                RemoteErrorMessage = configured.Error;
                RemoteStatusMessage = configured.Error ?? "Configuración guardada en los datos privados de Heimdall. Conecta para comprobar la NAS.";
                OnPropertyChanged(nameof(RemoteRootId));
                RaiseCanExecuteChanged();
            }
            else
            {
                RemoteStatusMessage = "Configuración guardada. Reinicia Heimdall para aplicarla.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            RemoteErrorMessage = exception.Message;
            RemoteStatusMessage = "No se pudo guardar la configuración SSH.";
        }
        return Task.CompletedTask;
    }

    /// <summary>Connects to the configured agent and loads the roots it authorizes for this user.</summary>
    public async Task ConnectRemoteAgentAsync()
    {
        if (_remoteAnalysis is null) return;
        IsBusy = true;
        RemoteErrorMessage = null;
        RemoteStatusMessage = "Conectando con el agente SSH y cargando raíces autorizadas…";
        try
        {
            var capabilities = await _remoteAnalysis.GetCapabilitiesAsync();
            if (!capabilities.AllowedOperations.Contains(OperationKindRemoteContractsModel.DuplicateScan))
                throw new InvalidOperationException("El agente conectado no anuncia la operación de análisis de duplicados.");
            if (capabilities.MaximumConcurrency < 1)
                throw new InvalidDataException("El agente anunció un máximo de concurrencia inválido para el análisis.");

            RemoteAllowedRoots = capabilities.AllowedRoots;
            SelectedSourceRoots.Clear();
            _organizerProfiles = capabilities.AllowedRoots.Select(root => new RemoteLibraryOrganizerProfileViewModel(root)).ToArray();
            OnPropertyChanged(nameof(OrganizerProfiles));
            OnPropertyChanged(nameof(SelectedOrganizerProfile));
            SourceRootCandidate = SourceRootCandidates.FirstOrDefault();
            RemoteMaximumConcurrency = capabilities.MaximumConcurrency;
            IsRemoteConnected = true;
            if (!IsRemoteRootAuthorized)
                RemoteRootId = RemoteAllowedRoots.Count > 0 ? RemoteAllowedRoots[0].Id : string.Empty;

            RemoteStatusMessage = RemoteAllowedRoots.Count == 0
                ? "Agente conectado, pero no anuncia raíces autorizadas."
                : $"Agente conectado: {RemoteAllowedRoots.Count} raíces autorizadas. Selecciona una para analizar.";
        }
        catch (Exception exception)
        {
            IsRemoteConnected = false;
            RemoteAllowedRoots = [];
            RemoteMaximumConcurrency = 1;
            RemoteErrorMessage = exception.Message;
            RemoteStatusMessage = "No se pudo conectar con el agente remoto.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Runs read-only duplicate analysis on the NAS-side agent; it never mutates remote files.</summary>
    public async Task AnalyzeRemoteDuplicatesAsync()
    {
        if (_remoteAnalysis is null) return;
        if (!IsRemoteConnected)
            await ConnectRemoteAgentAsync();
        if (!IsRemoteConnected || !IsRemoteRootAuthorized) return;
        IsBusy = true;
        RemoteErrorMessage = null;
        RemoteStatusMessage = HasRemoteResult
            ? "Reanalizando en la NAS… Se conserva visible el último resultado hasta completar el nuevo análisis."
            : "Analizando en la NAS…";
        try
        {
            var result = await _remoteAnalysis.AnalyzeAsync(
                RemoteRootId.Trim(), requestedConcurrency: RemoteMaximumConcurrency, Guid.NewGuid().ToString("N"));
            if (result.Analysis is null)
            {
                RemoteStatusMessage = $"Análisis remoto: {result.Receipt.Status}" +
                                      (string.IsNullOrWhiteSpace(result.Receipt.Reason) ? "." : $" — {result.Receipt.Reason}") +
                                      (HasRemoteResult ? " Se conserva el último resultado completo." : string.Empty);
                return;
            }

            HasRemoteResult = true;
            RemoteScannedAssetCount = result.Analysis.CataloguedAssetCount;
            RemoteCandidateFileCount = result.Analysis.CandidateFileCount;
            RemoteDuplicateGroupCount = result.Analysis.Groups.Count;
            RemoteHashesComputed = result.Analysis.HashesComputed;
            RemoteIssueCount = result.Analysis.Issues.Count;
            RemoteEffectiveConcurrency = result.Receipt.EffectiveConcurrency;
            RemoteDuplicateGroups = result.Analysis.Groups.Select((group, index) =>
                RemoteDuplicateGroupDisplayModel.From(index + 1, group)).ToArray();
            RemoteStatusMessage = $"Análisis NAS completado: {RemoteCandidateFileCount} candidatos, " +
                                  $"{RemoteDuplicateGroupCount} grupos duplicados, " +
                                  $"{result.Analysis.Issues.Count} incidencias. Archivos sin cambios.";
        }
        catch (Exception exception)
        {
            RemoteErrorMessage = exception.Message;
            RemoteStatusMessage = "No se pudo completar el análisis remoto." +
                                  (HasRemoteResult ? " Se conserva el último resultado completo." : string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs a real catalog scan while retaining the last successful summary on failure.</summary>
    public async Task AddAndScanAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Adding library and scanning media…";
        try
        {
            var result = await _facade.AddAndScanAsync(DisplayName.Trim(), RootPath.Trim());
            CataloguedCount = result.Scan.CataloguedAssetCount;
            LastScannedLibraryName = result.Library.DisplayName;
            LastScannedRootPath = result.Library.CanonicalPath;
            StatusMessage = $"{result.Library.DisplayName}: catalogued {result.Scan.CataloguedAssetCount} media assets.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Scan failed — see error details below.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RaiseCanExecuteChanged()
    {
        ((AsyncCommand)AddAndScanCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ChooseFolderCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)AnalyzeRemoteDuplicatesCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ConnectRemoteAgentCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ChooseSshKeyCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveRemoteSshSettingsCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)CreateDedicatedSshKeyCommand).RaiseCanExecuteChanged();
        RaiseSourceCommands();
    }

    private void RaiseSourceCommands()
    {
        ((AsyncCommand)AddSourceRootCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)RemoveSourceRootCommand).RaiseCanExecuteChanged();
    }

    private Task AddSourceRootAsync()
    {
        var source = SourceRootCandidate;
        if (source is not null && source.Id != RemoteRootId && SelectedSourceRoots.All(root => root.Id != source.Id))
            SelectedSourceRoots.Add(source);
        SourceRootCandidate = SourceRootCandidates.FirstOrDefault(root => SelectedSourceRoots.All(selected => selected.Id != root.Id));
        RaiseSourceCommands();
        return Task.CompletedTask;
    }

    private Task RemoveSourceRootAsync()
    {
        if (SourceRootToRemove is not null) SelectedSourceRoots.Remove(SourceRootToRemove);
        SourceRootToRemove = null;
        RaiseSourceCommands();
        return Task.CompletedTask;
    }

    // ── Inner ICommand implementation ─────────────────────────────────────

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public async void Execute(object? parameter) => await execute();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
