using System.Security.Cryptography;
using System.Text;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace Gplus.Gmail;

/// <summary>
/// Google token store that encrypts each entry with DPAPI (LocalMachine scope), so the token saved
/// by the interactive "authorize" step can be read by the service account on the same machine, but
/// the file is useless if copied elsewhere.
/// </summary>
public sealed class DpapiDataStore(string folder) : IDataStore
{
    private static readonly byte[] Entropy = "Gplus.GmailToken.v1"u8.ToArray();

    private string PathFor<T>(string key) => Path.Combine(folder, $"{typeof(T).FullName}-{key}.bin");

    public Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(folder);
        var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
        var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.LocalMachine);
        var path = PathFor<T>(key);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, sealedBytes);
        File.Move(tmp, path, overwrite: true);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = PathFor<T>(key);
        if (!File.Exists(path)) return Task.FromResult(default(T)!);
        var json = Encoding.UTF8.GetString(
            ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.LocalMachine));
        return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
    }

    public Task DeleteAsync<T>(string key)
    {
        File.Delete(PathFor<T>(key));
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(folder))
            foreach (var f in Directory.GetFiles(folder, "*.bin")) File.Delete(f);
        return Task.CompletedTask;
    }
}
