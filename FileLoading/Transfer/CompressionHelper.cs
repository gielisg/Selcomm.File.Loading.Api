using System.IO.Compression;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using FileLoading.Models;
using FileCompressionMethod = FileLoading.Models.CompressionMethod;

namespace FileLoading.Transfer;

/// <summary>
/// Helper class for file compression and decompression.
/// </summary>
public class CompressionHelper
{
    private readonly ILogger<CompressionHelper> _logger;

    public CompressionHelper(ILogger<CompressionHelper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compress a file using the specified method.
    /// </summary>
    /// <param name="sourceFilePath">Path to the file to compress</param>
    /// <param name="method">Compression method</param>
    /// <param name="deleteOriginal">Whether to delete the original file after compression</param>
    /// <returns>Path to the compressed file</returns>
    public async Task<string> CompressFileAsync(
        string sourceFilePath,
        FileCompressionMethod method,
        bool deleteOriginal = false)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source file not found", sourceFilePath);

        if (method == FileCompressionMethod.None)
            return sourceFilePath;

        var compressedPath = method switch
        {
            FileCompressionMethod.GZip => sourceFilePath + ".gz",
            FileCompressionMethod.Zip => Path.ChangeExtension(sourceFilePath, ".zip"),
            _ => throw new NotSupportedException($"Compression method '{method}' is not supported")
        };

        _logger.LogInformation("Compressing {SourceFile} using {Method}", sourceFilePath, method);

        switch (method)
        {
            case FileCompressionMethod.GZip:
                await CompressGZipAsync(sourceFilePath, compressedPath);
                break;

            case FileCompressionMethod.Zip:
                await CompressZipAsync(sourceFilePath, compressedPath);
                break;
        }

        var originalSize = new FileInfo(sourceFilePath).Length;
        var compressedSize = new FileInfo(compressedPath).Length;
        var ratio = originalSize > 0 ? (1 - (double)compressedSize / originalSize) * 100 : 0;

        _logger.LogInformation("Compressed {SourceFile}: {OriginalSize} -> {CompressedSize} ({Ratio:F1}% reduction)",
            sourceFilePath, originalSize, compressedSize, ratio);

        if (deleteOriginal)
        {
            File.Delete(sourceFilePath);
            _logger.LogDebug("Deleted original file {SourceFile}", sourceFilePath);
        }

        return compressedPath;
    }

    /// <summary>
    /// Decompress a file.
    /// </summary>
    /// <param name="compressedFilePath">Path to the compressed file</param>
    /// <param name="destinationFolder">Folder to extract to (null = same as source)</param>
    /// <param name="deleteCompressed">Whether to delete the compressed file after extraction</param>
    /// <returns>Path to the decompressed file</returns>
    public async Task<string> DecompressFileAsync(
        string compressedFilePath,
        string? destinationFolder = null,
        bool deleteCompressed = false)
    {
        if (!File.Exists(compressedFilePath))
            throw new FileNotFoundException("Compressed file not found", compressedFilePath);

        destinationFolder ??= Path.GetDirectoryName(compressedFilePath) ?? ".";

        var extension = Path.GetExtension(compressedFilePath).ToLowerInvariant();
        string decompressedPath;

        _logger.LogInformation("Decompressing {CompressedFile}", compressedFilePath);

        switch (extension)
        {
            case ".gz":
                decompressedPath = await DecompressGZipAsync(compressedFilePath, destinationFolder);
                break;

            case ".zip":
                decompressedPath = await DecompressZipAsync(compressedFilePath, destinationFolder);
                break;

            default:
                throw new NotSupportedException($"Compression format '{extension}' is not supported");
        }

        _logger.LogInformation("Decompressed {CompressedFile} to {DecompressedFile}",
            compressedFilePath, decompressedPath);

        if (deleteCompressed)
        {
            File.Delete(compressedFilePath);
            _logger.LogDebug("Deleted compressed file {CompressedFile}", compressedFilePath);
        }

        return decompressedPath;
    }

    private async Task CompressGZipAsync(string sourceFilePath, string destFilePath)
    {
        await using var sourceStream = File.OpenRead(sourceFilePath);
        await using var destStream = File.Create(destFilePath);
        await using var gzipStream = new GZipOutputStream(destStream);

        gzipStream.SetLevel(6); // Compression level 1-9
        await sourceStream.CopyToAsync(gzipStream);
    }

    private async Task<string> DecompressGZipAsync(string compressedFilePath, string destinationFolder)
    {
        // Remove .gz extension to get original filename
        var fileName = Path.GetFileNameWithoutExtension(compressedFilePath);
        var destFilePath = Path.Combine(destinationFolder, fileName);

        await using var compressedStream = File.OpenRead(compressedFilePath);
        await using var gzipStream = new GZipInputStream(compressedStream);
        await using var destStream = File.Create(destFilePath);

        await gzipStream.CopyToAsync(destStream);
        return destFilePath;
    }

    private async Task CompressZipAsync(string sourceFilePath, string destFilePath)
    {
        await using var destStream = File.Create(destFilePath);
        using var zipStream = new ZipOutputStream(destStream);

        zipStream.SetLevel(6);

        var fileName = Path.GetFileName(sourceFilePath);
        var entry = new ZipEntry(fileName)
        {
            DateTime = File.GetLastWriteTime(sourceFilePath)
        };

        await zipStream.PutNextEntryAsync(entry);

        await using var sourceStream = File.OpenRead(sourceFilePath);
        await sourceStream.CopyToAsync(zipStream);

        zipStream.CloseEntry();
    }

    private async Task<string> DecompressZipAsync(string compressedFilePath, string destinationFolder)
    {
        string? firstFileName = null;

        await using var compressedStream = File.OpenRead(compressedFilePath);
        using var zipStream = new ZipInputStream(compressedStream);

        ZipEntry? entry;
        while ((entry = zipStream.GetNextEntry()) != null)
        {
            if (entry.IsDirectory)
                continue;

            var destFilePath = Path.Combine(destinationFolder, entry.Name);
            firstFileName ??= destFilePath;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(destFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var destStream = File.Create(destFilePath);
            await zipStream.CopyToAsync(destStream);
        }

        return firstFileName ?? throw new InvalidOperationException("Zip file was empty");
    }

    /// <summary>
    /// Detect compression method from file extension.
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>Detected compression method</returns>
    public static FileCompressionMethod DetectCompressionMethod(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".gz" or ".gzip" => FileCompressionMethod.GZip,
            ".zip" => FileCompressionMethod.Zip,
            _ => FileCompressionMethod.None
        };
    }

    /// <summary>
    /// Check if a file is compressed.
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>True if file has a compression extension</returns>
    public static bool IsCompressed(string filePath)
    {
        return DetectCompressionMethod(filePath) != FileCompressionMethod.None;
    }
}
