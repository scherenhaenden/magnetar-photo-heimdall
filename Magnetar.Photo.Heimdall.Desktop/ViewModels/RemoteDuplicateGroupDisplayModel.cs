using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

/// <summary>Presentation-friendly, structured summary of one exact duplicate group.</summary>
public sealed record RemoteDuplicateGroupDisplayModel(
    int GroupNumber,
    long FileLength,
    string Algorithm,
    int AlgorithmVersion,
    string FullHash,
    IReadOnlyList<string> RelativePaths)
{
    public int FileCount => RelativePaths.Count;
    public string GroupLabel => $"Grupo {GroupNumber}";
    public string FileCountLabel => $"{FileCount} archivos";
    public string SizeLabel => $"{FileLength:N0} bytes";
    public string AlgorithmLabel => $"{Algorithm} v{AlgorithmVersion}";

    public static RemoteDuplicateGroupDisplayModel From(int number, ExactDuplicateGroupBusinessLogicModel group) =>
        new(number, group.FileLength, group.Algorithm, group.AlgorithmVersion, group.FullHash, group.RelativePaths);
}
