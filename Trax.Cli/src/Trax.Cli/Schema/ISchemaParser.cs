using Trax.Cli.Models;

namespace Trax.Cli.Schema;

public interface ISchemaParser
{
    ApiSchema Parse(string filePath);
}
