using Trax.Cli.Models;

namespace Trax.Cli.Schema;

public interface ISchemaParser
{
    Task<ApiSchema> ParseAsync(string filePath);
}
