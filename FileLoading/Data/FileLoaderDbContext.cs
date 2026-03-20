using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Selcomm.Data.Common;

namespace FileLoading.Data;

/// <summary>
/// Database context for FileLoader operations.
/// </summary>
public class FileLoaderDbContext : OdbcDbContext
{
    public FileLoaderDbContext(
        IConfiguration configuration,
        ILogger<FileLoaderDbContext> logger,
        IHttpContextAccessor httpContextAccessor)
        : base(configuration, logger, httpContextAccessor)
    {
    }
}
