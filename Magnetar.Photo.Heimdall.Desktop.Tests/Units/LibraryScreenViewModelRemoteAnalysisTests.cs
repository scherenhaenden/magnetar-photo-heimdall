using System.Text.Json;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.Desktop.ViewModels;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Services;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.Tests.Units;

public sealed class LibraryScreenViewModelRemoteAnalysisTests
{
    [Fact]
    public async Task RemoteAnalysisCommandUsesConfiguredRootAndShowsAgentResult()
    {
        var analysis = new LibraryDuplicateAnalysisBusinessLogicModel(
            Guid.NewGuid(), 8, 6, 6, 0, 6,
            [new ExactDuplicateGroupBusinessLogicModel(512, "BLAKE3", 1, "abc", ["one.jpg", "two.jpg"])],
            []);
        var agent = new FakeRemoteAgent(analysis, maximumConcurrency: 8, effectiveConcurrency: 3);
        var vm = new LibraryScreenViewModel(
            new LibraryOnboardingPresentationLogicService(null!),
            remoteAnalysis: new RemoteDuplicateAnalysisPresentationLogicService(agent),
            remoteRootId: "nas-photos");

        Assert.False(vm.AnalyzeRemoteDuplicatesCommand.CanExecute(null));
        await vm.ConnectRemoteAgentAsync();
        Assert.True(vm.AnalyzeRemoteDuplicatesCommand.CanExecute(null));
        await vm.AnalyzeRemoteDuplicatesAsync();

        Assert.Equal("nas-photos", agent.SubmittedRequest?.RootId);
        Assert.Equal(OperationKindRemoteContractsModel.DuplicateScan, agent.SubmittedRequest?.Kind);
        Assert.Equal(8, agent.SubmittedRequest?.RequestedConcurrency);
        Assert.Equal(8, vm.RemoteMaximumConcurrency);
        Assert.Equal(3, vm.RemoteEffectiveConcurrency);
        Assert.Equal(6, vm.RemoteCandidateFileCount);
        Assert.Equal(1, vm.RemoteDuplicateGroupCount);
        Assert.Equal(8, vm.RemoteScannedAssetCount);
        Assert.Equal(6, vm.RemoteHashesComputed);
        var group = Assert.Single(vm.RemoteDuplicateGroups);
        Assert.Equal(2, group.FileCount);
        Assert.Equal(512, group.FileLength);
        Assert.Collection(group.RelativePaths,
            path => Assert.Equal("one.jpg", path),
            path => Assert.Equal("two.jpg", path));
        Assert.Contains("Archivos sin cambios", vm.RemoteStatusMessage);
        Assert.False(vm.HasRemoteError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void RemoteAnalysisCommandIsUnavailableWithoutAgentConfiguration()
    {
        var vm = new LibraryScreenViewModel(
            new LibraryOnboardingPresentationLogicService(null!),
            remoteConfigurationError: "SSH no configurado.");

        Assert.False(vm.AnalyzeRemoteDuplicatesCommand.CanExecute(null));
        Assert.Equal("SSH no configurado.", vm.RemoteStatusMessage);
    }

    [Fact]
    public async Task ConnectLoadsAuthorizedRootsAndSelectsAnAllowedRoot()
    {
        var vm = new LibraryScreenViewModel(
            new LibraryOnboardingPresentationLogicService(null!),
            remoteAnalysis: new RemoteDuplicateAnalysisPresentationLogicService(new FakeRemoteAgent(Analysis())),
            remoteRootId: "untrusted-root");

        await vm.ConnectRemoteAgentAsync();

        Assert.True(vm.IsRemoteConnected);
        Assert.Collection(vm.RemoteAllowedRoots,
            root => Assert.Equal("nas-photos", root.Id));
        Assert.Equal("nas-photos", vm.RemoteRootId);
        Assert.True(vm.IsRemoteRootAuthorized);
        Assert.True(vm.AnalyzeRemoteDuplicatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task DestinationCanHaveMultipleSourcesAndEachLibraryKeepsItsOwnOrganizerProfile()
    {
        var roots = new[]
        {
            new AgentRootRemoteContractsModel("library-main", "Biblioteca principal"),
            new AgentRootRemoteContractsModel("camera-roll", "Tarjeta de cámara"),
            new AgentRootRemoteContractsModel("old-archive", "Archivo anterior"),
        };
        var vm = new LibraryScreenViewModel(
            new LibraryOnboardingPresentationLogicService(null!),
            remoteAnalysis: new RemoteDuplicateAnalysisPresentationLogicService(new FakeRemoteAgent(Analysis(), allowedRoots: roots)),
            remoteRootId: "library-main");

        await vm.ConnectRemoteAgentAsync();
        Assert.Equal("library-main", vm.SelectedOrganizerProfile?.RootId);
        Assert.Equal("{camera}/{year}/{month}", vm.SelectedOrganizerProfile?.FolderPattern);
        Assert.Equal("nodate", vm.SelectedOrganizerProfile?.MissingDateFolder);
        Assert.Equal("unknown", vm.SelectedOrganizerProfile?.MissingCameraFolder);

        vm.AddSourceRootCommand.Execute(null);
        Assert.Single(vm.SelectedSourceRoots);
        Assert.Equal("camera-roll", vm.SelectedSourceRoots[0].Id);
        vm.AddSourceRootCommand.Execute(null);
        Assert.Equal(2, vm.SelectedSourceRoots.Count);

        vm.SelectedOrganizerProfile!.FolderPattern = "{year}/{month}/{camera}";
        vm.RemoteRootId = "camera-roll";
        Assert.Equal("camera-roll", vm.SelectedOrganizerProfile?.RootId);
        Assert.Equal("{camera}/{year}/{month}", vm.SelectedOrganizerProfile?.FolderPattern);
        Assert.DoesNotContain(vm.SourceRootCandidates, root => root.Id == "camera-roll");
        Assert.Contains("plan remoto", vm.ConsolidationCapabilityStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedReanalysisKeepsLastSuccessfulResultVisible()
    {
        var agent = new FakeRemoteAgent(Analysis());
        var vm = new LibraryScreenViewModel(
            new LibraryOnboardingPresentationLogicService(null!),
            remoteAnalysis: new RemoteDuplicateAnalysisPresentationLogicService(agent),
            remoteRootId: "nas-photos");

        await vm.AnalyzeRemoteDuplicatesAsync();
        var previousGroup = Assert.Single(vm.RemoteDuplicateGroups);
        agent.SubmitException = new IOException("NAS connection interrupted");

        await vm.AnalyzeRemoteDuplicatesAsync();

        Assert.True(vm.HasRemoteResult);
        Assert.Same(previousGroup, Assert.Single(vm.RemoteDuplicateGroups));
        Assert.Equal(1, vm.RemoteDuplicateGroupCount);
        Assert.Contains("Se conserva el último resultado completo", vm.RemoteStatusMessage);
        Assert.True(vm.HasRemoteError);
    }

    private static LibraryDuplicateAnalysisBusinessLogicModel Analysis() => new(
        Guid.NewGuid(), 8, 6, 6, 0, 6,
        [new ExactDuplicateGroupBusinessLogicModel(512, "BLAKE3", 1, "abc", ["one.jpg", "two.jpg"])], []);

    private sealed class FakeRemoteAgent(
        LibraryDuplicateAnalysisBusinessLogicModel analysis,
        int maximumConcurrency = 2,
        int? effectiveConcurrency = null,
        IReadOnlyList<AgentRootRemoteContractsModel>? allowedRoots = null)
        : IRemoteAgentRpcV1RemoteContractsContract
    {
        private readonly IReadOnlyList<AgentRootRemoteContractsModel> _allowedRoots = allowedRoots ?? [new AgentRootRemoteContractsModel("nas-photos", "Photos")];
        public OperationRequestRemoteContractsModel? SubmittedRequest { get; private set; }
        public Exception? SubmitException { get; set; }

        public Task<AgentCapabilitiesRemoteContractsModel> GetCapabilitiesAsync(
            ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCapabilitiesRemoteContractsModel(
                ProtocolVersionRemoteContractsModel.V1,
                _allowedRoots,
                [],
                new HashSet<OperationKindRemoteContractsModel> { OperationKindRemoteContractsModel.DuplicateScan },
                maximumConcurrency,
                true));

        public Task<AgentHealthSnapshotRemoteContractsModel> GetHealthAsync(ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ThermalSnapshotRemoteContractsModel> GetThermalAsync(IReadOnlyList<string> sensorIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationReceiptRemoteContractsModel> SubmitOperationAsync(OperationRequestRemoteContractsModel request, CancellationToken cancellationToken = default)
        {
            if (SubmitException is not null)
                return Task.FromException<OperationReceiptRemoteContractsModel>(SubmitException);
            SubmittedRequest = request;
            return Task.FromResult(new OperationReceiptRemoteContractsModel(
                "receipt-1", request.IdempotencyKey, OperationStatusRemoteContractsModel.Completed,
                DateTimeOffset.UtcNow, effectiveConcurrency ?? request.RequestedConcurrency, ResultJson: JsonSerializer.Serialize(analysis)));
        }

        public Task<OperationReceiptRemoteContractsModel> GetReceiptAsync(string receiptId, ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
