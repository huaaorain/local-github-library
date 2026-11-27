using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GitHubTopicReports.Services;

internal sealed class GitHubCredentialStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("LocalGitHubLibrary.GitHubToken.v1");
    private readonly string _filePath;

    public GitHubCredentialStore(string libraryRoot)
    {
        var dataDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "github-auth.bin");
    }

    public string? LoadToken()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var tokenBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(tokenBytes).Trim();
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveToken(string token)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token),
            Entropy,
            DataProtectionScope.CurrentUser);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    public void DeleteToken()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
