using System.Security.Cryptography;

namespace FileLoading.Services;

/// <summary>
/// Streaming SHA-256 file hash computation for duplicate file detection.
/// Uses IncrementalHash with a fixed buffer — constant memory regardless of file size.
/// </summary>
public static class FileHashService
{
    private const int BufferSize = 81920; // 80KB chunks

    /// <summary>
    /// Compute SHA-256 hash of a file on disk.
    /// </summary>
    public static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return await ComputeHashAsync(stream, ct);
    }

    /// <summary>
    /// Compute SHA-256 hash from an open stream.
    /// </summary>
    public static async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            sha.AppendData(buffer, 0, bytesRead);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Copy a stream to a destination while computing the hash simultaneously.
    /// Used by the upload path to avoid reading the file twice.
    /// </summary>
    public static async Task<string> CopyAndHashAsync(Stream source, Stream destination, CancellationToken ct = default)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            sha.AppendData(buffer, 0, bytesRead);
            await destination.WriteAsync(buffer, 0, bytesRead, ct);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
