using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FileLoading.Infrastructure;

/// <summary>
/// Document filter that adds descriptions to Swagger tags.
/// Tags are ordered logically by functional area.
/// </summary>
public class TagDescriptionsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Tags = new List<OpenApiTag>
        {
            new OpenApiTag
            {
                Name = "Health",
                Description = "Health check and system status endpoints for monitoring and load balancers"
            },
            new OpenApiTag
            {
                Name = "File Loading",
                Description = "Load and process network files (CDR, CHG, EBL, SVC, ORD). Identify files by nt-file-num (the database record key assigned at load time)"
            },
            new OpenApiTag
            {
                Name = "File Management",
                Description = "Workflow management for transferred files. Most operations use transfer-id (the workflow tracking key). Unload and skip-sequence use nt-file-num because they operate on already-loaded database records"
            },
            new OpenApiTag
            {
                Name = "Transfer Operations",
                Description = "Fetch files from remote sources (SFTP/FTP/FileSystem) into the transfer workflow"
            },
            new OpenApiTag
            {
                Name = "Transfer Sources",
                Description = "CRUD for transfer source configurations. source-id is an auto-generated integer (SERIAL)"
            },
            new OpenApiTag
            {
                Name = "Folder Configuration",
                Description = "Folder workflow configuration per domain/file-type. Folders are always present per domain so only GET and PUT are provided"
            },
            new OpenApiTag
            {
                Name = "Parser Configuration",
                Description = "CRUD for generic file parser configurations and column mappings"
            },
            new OpenApiTag
            {
                Name = "Vendors",
                Description = "CRUD for vendor/network records (networks table)"
            },
            new OpenApiTag
            {
                Name = "File Classes",
                Description = "CRUD for file class records (e.g. CDR, CHG). A file class groups related file types"
            },
            new OpenApiTag
            {
                Name = "File Types",
                Description = "CRUD for file type records (e.g. TEL_GSM, OPTUS). Each file type belongs to a file class and optionally a vendor"
            },
            new OpenApiTag
            {
                Name = "File Types NT",
                Description = "CRUD for network-specific file type configuration (file_type_nt table). Maps a file type to a customer number, filename pattern, and header/trailer skip counts used during loading"
            },
            new OpenApiTag
            {
                Name = "Activity Log",
                Description = "Audit trail of all file operations (downloads, processing, moves, errors)"
            },
            new OpenApiTag
            {
                Name = "Validation",
                Description = "AI-friendly validation summaries and file error analysis"
            },
            new OpenApiTag
            {
                Name = "Exceptions",
                Description = "View files with errors or in the skipped folder"
            }
        };
    }
}
