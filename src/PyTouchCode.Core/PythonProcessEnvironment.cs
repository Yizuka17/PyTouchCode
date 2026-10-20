namespace PyTouchCode.Core;
public static class PythonProcessEnvironment
{
    public static Dictionary<string,string> Create(string python, IDictionary<string,string>? overrides = null)
    {
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string? directory = Path.GetDirectoryName(python);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string prefix = Path.GetFileName(directory).Equals("Scripts", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(directory)! : directory;
            var paths = new[] { prefix, Path.Combine(prefix, "Scripts"), Path.Combine(prefix, "Library", "bin"), Path.Combine(prefix, "Library", "usr", "bin") }.Where(Directory.Exists);
            values["PATH"] = string.Join(Path.PathSeparator, paths) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            if (File.Exists(Path.Combine(prefix, "pyvenv.cfg"))) values["VIRTUAL_ENV"] = prefix;
            if (Directory.Exists(Path.Combine(prefix, "conda-meta"))) { values["CONDA_PREFIX"] = prefix; values["CONDA_DEFAULT_ENV"] = Path.GetFileName(prefix); }
        }
        foreach (var pair in overrides ?? new Dictionary<string,string>()) values[pair.Key] = pair.Value;
        return values;
    }
}
