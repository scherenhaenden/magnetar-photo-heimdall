using System.Globalization;
using System.Text;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Services;

/// <summary>Builds portable, traversal-safe relative paths without reading or writing files.</summary>
public sealed class OrganizerPathPlannerBusinessLogicService : IOrganizerPathPlannerBusinessLogicService
{
    private static readonly HashSet<string> SupportedTokens = new(StringComparer.Ordinal)
    {
        "name", "extension", "camera", "year", "month", "hash8",
    };

    public OrganizerPathPlanBusinessLogicModel Plan(
        OrganizerProfileBusinessLogicModel profile,
        string sourceFileName,
        string? camera,
        ResolvedCaptureDateBusinessLogicModel? captureDate,
        string? contentHash = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profile.FolderSegments);
        ArgumentNullException.ThrowIfNull(sourceFileName);

        if (profile.CollisionPolicy != OrganizerCollisionPolicyBusinessLogicModel.Fail)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Unsupported collision policy.");
        }

        var safeCamera = SanitizeComponent(string.IsNullOrWhiteSpace(camera)
            ? profile.UnknownCameraFolder
            : camera);
        var folderParts = new List<string>();
        var missingDateFolderAdded = false;

        foreach (var segment in profile.FolderSegments)
        {
            var value = segment switch
            {
                OrganizerFolderSegmentBusinessLogicModel.Camera => safeCamera,
                OrganizerFolderSegmentBusinessLogicModel.Year when captureDate is not null =>
                    captureDate.Value.Year.ToString("D4", CultureInfo.InvariantCulture),
                OrganizerFolderSegmentBusinessLogicModel.Month when captureDate is not null =>
                    captureDate.Value.Month.ToString("D2", CultureInfo.InvariantCulture),
                OrganizerFolderSegmentBusinessLogicModel.Year or OrganizerFolderSegmentBusinessLogicModel.Month =>
                    SanitizeComponent(profile.MissingDateFolder),
                _ => throw new ArgumentOutOfRangeException(nameof(profile), "Unsupported folder segment."),
            };

            if (captureDate is null
                && segment is OrganizerFolderSegmentBusinessLogicModel.Year or OrganizerFolderSegmentBusinessLogicModel.Month)
            {
                if (!missingDateFolderAdded)
                {
                    folderParts.Add(value);
                    missingDateFolderAdded = true;
                }

                continue;
            }

            folderParts.Add(value);
        }

        var (name, extension) = SplitFileName(sourceFileName);
        var fileName = ExpandTemplate(
            profile.FileNameTemplate,
            name,
            extension,
            safeCamera,
            captureDate?.Value,
            contentHash);

        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new ArgumentException("The filename template must produce a filename.", nameof(profile));
        }

        folderParts.Add(SanitizeComponent(fileName));
        return new OrganizerPathPlanBusinessLogicModel(
            string.Join('/', folderParts),
            profile.CollisionPolicy);
    }

    private static string ExpandTemplate(
        string template,
        string name,
        string extension,
        string camera,
        DateTimeOffset? captureDate,
        string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new ArgumentException("A filename template is required.", nameof(template));
        }

        var year = captureDate?.Year.ToString("D4", CultureInfo.InvariantCulture) ?? "unknown-date";
        var month = captureDate?.Month.ToString("D2", CultureInfo.InvariantCulture) ?? "unknown-date";
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["extension"] = extension,
            ["camera"] = camera,
            ["year"] = year,
            ["month"] = month,
            ["hash8"] = template.Contains("{hash8}", StringComparison.Ordinal)
                ? GetHashPrefix(contentHash)
                : string.Empty,
        };

        var output = new StringBuilder(template.Length + name.Length + extension.Length);
        for (var index = 0; index < template.Length;)
        {
            if (template[index] != '{')
            {
                if (template[index] == '}')
                {
                    throw new ArgumentException("The filename template contains an unmatched closing brace.", nameof(template));
                }

                output.Append(template[index++]);
                continue;
            }

            var closing = template.IndexOf('}', index + 1);
            if (closing < 0)
            {
                throw new ArgumentException("The filename template contains an unmatched opening brace.", nameof(template));
            }

            var token = template[(index + 1)..closing];
            if (!SupportedTokens.Contains(token))
            {
                throw new ArgumentException($"Unsupported filename template token '{{{token}}}'.", nameof(template));
            }

            output.Append(values[token]);
            index = closing + 1;
        }

        return output.ToString();
    }

    private static string GetHashPrefix(string? contentHash)
    {
        if (contentHash is null || contentHash.Length < 8
            || contentHash.Take(8).Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A hexadecimal content hash with at least 8 characters is required by {hash8}.", nameof(contentHash));
        }

        return contentHash[..8].ToLowerInvariant();
    }

    private static (string Name, string Extension) SplitFileName(string sourceFileName)
    {
        var portableName = sourceFileName.Replace('\\', '/');
        var leaf = portableName[(portableName.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..")
        {
            throw new ArgumentException("A source filename is required.", nameof(sourceFileName));
        }

        var dot = leaf.LastIndexOf('.');
        if (dot <= 0)
        {
            return (leaf, string.Empty);
        }

        return (leaf[..dot], leaf[dot..]);
    }

    private static string SanitizeComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var result = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormC))
        {
            if (character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
                || char.IsControl(character))
            {
                result.Append('_');
            }
            else
            {
                result.Append(character);
            }
        }

        var component = result.ToString().Trim().TrimEnd('.');
        if (component.Length == 0 || component is "." or "..")
        {
            return "unknown";
        }

        var stem = component.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9'))
        {
            component = $"_{component}";
        }

        return component.Length <= 180 ? component : component[..180];
    }
}
