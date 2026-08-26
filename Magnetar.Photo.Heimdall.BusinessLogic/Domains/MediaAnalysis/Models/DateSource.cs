namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

/// <summary>Priority-ordered sources for a media file's capture date.</summary>
public enum DateSource
{
    /// <summary>EXIF tag DateTimeOriginal (0x9003) from the Exif Sub-IFD.</summary>
    ExifDateTimeOriginal = 0,

    /// <summary>XMP property (xmp:CreateDate / photoshop:DateCreated / exif:DateTimeOriginal).</summary>
    Xmp = 1,

    /// <summary>QuickTime movie-header atom (mvhd) creation time.</summary>
    QuickTime = 2,

    /// <summary>Filesystem last-write time — lowest confidence fallback.</summary>
    FilesystemMtime = 3,
}