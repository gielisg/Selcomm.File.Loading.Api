using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Transfer;

/// <summary>
/// Factory for creating transfer clients based on protocol type.
/// </summary>
public class TransferClientFactory : ITransferClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TransferClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Create a transfer client for the given source configuration.
    /// </summary>
    /// <param name="config">Transfer source configuration</param>
    /// <returns>Transfer client instance</returns>
    public ITransferClient CreateClient(TransferSourceConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return config.Protocol switch
        {
            TransferProtocol.Sftp => new SftpTransferClient(
                config, _loggerFactory.CreateLogger<SftpTransferClient>()),

            TransferProtocol.Ftp => new FtpTransferClient(
                config, _loggerFactory.CreateLogger<FtpTransferClient>()),

            TransferProtocol.FileSystem => new FileSystemTransferClient(
                config, _loggerFactory.CreateLogger<FileSystemTransferClient>()),

            _ => throw new NotSupportedException($"Transfer protocol '{config.Protocol}' is not supported")
        };
    }
}
