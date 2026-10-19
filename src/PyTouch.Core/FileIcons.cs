namespace PyTouch.Core;

public static class FileIcons
{
    public static string Resolve(string path, bool directory = false, bool expanded = false)
    {
        if (directory) return expanded ? "folder-open" : "folder";
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".py" or ".pyw" or ".pyi" => "python",
            ".csv" or ".tsv" or ".cvs" or ".xls" or ".xlsx" => "table",
            ".c" or ".h" => "c",
            ".cpp" or ".cxx" or ".cc" or ".hpp" or ".hxx" => "cpp",
            ".json" or ".jsonc" => "json",
            ".md" or ".markdown" => "markdown",
            ".js" or ".mjs" or ".cjs" or ".jsx" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".html" or ".htm" => "html",
            ".css" or ".scss" or ".less" => "css",
            ".xml" or ".xaml" or ".svg" => "xml",
            ".yaml" or ".yml" or ".toml" or ".ini" or ".cfg" => "yaml",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" => "image",
            ".pdf" => "pdf",
            ".zip" or ".7z" or ".rar" or ".gz" or ".tar" => "zip",
            ".ps1" or ".psm1" or ".bat" or ".cmd" or ".sh" => "powershell",
            ".ipynb" => "jupyter",
            ".db" or ".sqlite" or ".sqlite3" or ".sql" => "database",
            _ when Path.GetFileName(path).StartsWith(".git", StringComparison.OrdinalIgnoreCase) => "git",
            _ => "document"
        };
    }
}
