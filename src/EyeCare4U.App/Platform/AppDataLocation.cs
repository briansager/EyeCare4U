using System.IO;

namespace EyeCare4U.App.Platform;

/// <summary>Normal launches always use LocalApplicationData. Isolated review is explicit and temporary.</summary>
public static class AppDataLocation
{
    public static string Resolve(string[] arguments)
    {
        if(arguments.Length==0)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EyeCare4U");
        if(arguments.Length!=2 || arguments[0]!="--isolated-review" || !Path.IsPathFullyQualified(arguments[1]))
            throw new ArgumentException("Use no arguments for normal launch, or the documented isolated-review launcher.");
        var root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"EyeCare4U-Review"));
        var directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments[1]));
        if(!string.Equals(Path.GetDirectoryName(directory),root,StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(directory),"N",out _) ||
            !Directory.Exists(directory) ||
            (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0 ||
            (File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0 ||
            !File.Exists(Path.Combine(directory,".eyecare4u-review")))
            throw new ArgumentException("Isolated review requires a marked temporary directory created by Start-IsolatedReview.ps1.");
        return directory;
    }
}
