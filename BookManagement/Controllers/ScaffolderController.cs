using System.Text;
using BookManagement.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

public class ScaffolderController : Controller
{
    private readonly ApplicationDbContext _context;
    public ScaffolderController(ApplicationDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.Tables = await GetTables();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName) || !(await GetTables()).Contains(tableName))
            return BadRequest("Table tidak ditemukan.");

        var columns = new List<(string Name, string Type, bool Nullable)>();
        var connection = _context.Database.GetDbConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName
                ORDER BY ORDINAL_POSITION";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES"));
        }

        var className = ToPascalCase(tableName);
        var code = new StringBuilder();
        code.AppendLine("using System;");
        code.AppendLine();
        code.AppendLine("namespace BookManagement.Models");
        code.AppendLine("{");
        code.AppendLine($"    public class {className}");
        code.AppendLine("    {");
        foreach (var column in columns)
        {
            var type = CSharpType(column.Type);
            if (column.Nullable && type != "string") type += "?";
            if (column.Nullable && type == "string") type += "?";
            code.AppendLine($"        public {type} {ToPascalCase(column.Name)} {{ get; set; }}");
        }
        code.AppendLine("    }");
        code.AppendLine("}");
        return File(Encoding.UTF8.GetBytes(code.ToString()), "text/plain", className + ".cs");
    }

    private async Task<List<string>> GetTables()
    {
        var tables = new List<string>();
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables;
    }

    private static string CSharpType(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "int" => "int", "bigint" => "long", "smallint" => "short", "tinyint" => "byte",
        "bit" => "bool", "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
        "float" => "double", "real" => "float", "date" or "datetime" or "datetime2" or "smalldatetime" => "DateTime",
        "time" => "TimeSpan", "uniqueidentifier" => "Guid", "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => "byte[]",
        _ => "string"
    };

    private static string ToPascalCase(string value) => string.Concat(value.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
}
