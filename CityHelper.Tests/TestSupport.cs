using System;
using System.IO;
using Xunit;

namespace CityHelper.Tests;

/// <summary>A scratch folder that is deleted when the test is done.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cityhelper_tests_" + Guid.NewGuid().ToString("N")[..8]);

    public string Path { get; }

    public string Dir(params string[] parts)
    {
        var path = System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    public string Write(string relative, params string[] lines)
    {
        var path = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Tests that read or write the shared CityHelper.ini (IniStore, and the settings and merge code that read it) run one
/// after another: they all use the same file.
/// </summary>
[CollectionDefinition("ini", DisableParallelization = true)]
public sealed class IniCollection { }
