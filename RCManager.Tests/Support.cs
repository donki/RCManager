using System.Net;
using System.Net.Http;
using System.Reflection;
using SocRcManager.Files;
using SocRcManager.Localization;

// El almacen, los ajustes, el registro y el idioma son estaticos: las pruebas van de una en una.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SocRcManager.Services
{
    /// <summary>Lo que en la aplicacion genera oauth.props: aqui, identificadores de mentira.</summary>
    public static class OAuthSecrets
    {
        public const string MicrosoftClientId = "ms-client-test";
        public const string GoogleClientId = "google-client-test";
        public const string GoogleClientSecret = "google-secret-test";
    }
}

namespace SocRcManager.Tests
{
    /// <summary>Una carpeta temporal propia de la prueba que se borra al terminar.</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rcm-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name, string? content = null)
        {
            var full = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            if (content is not null)
                System.IO.File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
                    System.IO.File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Path, true);
            }
            catch (Exception)
            {
            }
        }
    }

    public static class Lang
    {
        /// <summary>Pone el idioma de la aplicacion («es» o «en»).</summary>
        public static void Set(string language)
        {
            if (Loc.Language != language)
                Loc.Toggle();
        }
    }

    /// <summary>Un servidor HTTP de mentira: cada peticion pasa por la funcion y se apunta.</summary>
    public sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request, body);
        }

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(text) };
    }

    /// <summary>Un sistema de ficheros remoto en memoria: directorios con sus entradas y un diario de lo que se pide.</summary>
    public sealed class FakeRemote : IRemoteFileSystem
    {
        public Dictionary<string, List<FileEntry>> Dirs { get; } = new(StringComparer.Ordinal);
        public List<string> Log { get; } = [];
        public bool Disposed { get; private set; }

        public string InitialDirectory { get; set; } = "/home/test";
        public bool SupportsPermissions { get; set; } = true;

        public FakeRemote Dir(string path, params FileEntry[] entries)
        {
            Dirs[path] = [.. entries];
            return this;
        }

        public static FileEntry F(string dir, string name, long size = 10) => new(name, RemotePath.Combine(dir, name), false, size, null);
        public static FileEntry D(string dir, string name) => new(name, RemotePath.Combine(dir, name), true, 0, null);

        public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken)
        {
            Log.Add("list " + path);
            return Task.FromResult<IReadOnlyList<FileEntry>>(Dirs.TryGetValue(path, out var l) ? l : []);
        }

        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken) { Log.Add("get " + remotePath); return Task.CompletedTask; }
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken) { Log.Add("put " + remotePath); return Task.CompletedTask; }
        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) { Log.Add("mkdir " + path); return Task.CompletedTask; }
        public Task DeleteFileAsync(string path, CancellationToken cancellationToken) { Log.Add("rm " + path); return Task.CompletedTask; }
        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) { Log.Add("rmdir " + path); return Task.CompletedTask; }
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) { Log.Add($"mv {path} {newPath}"); return Task.CompletedTask; }
        public Task<FileEntry?> StatAsync(string path, CancellationToken cancellationToken) => Task.FromResult<FileEntry?>(null);
        public Task SetModifiedAsync(string path, DateTime modified, CancellationToken cancellationToken) { Log.Add("touch " + path); return Task.CompletedTask; }
        public Task ChangeModeAsync(string path, int mode, CancellationToken cancellationToken) { Log.Add($"chmod {UnixMode.ToOctal(mode)} {path}"); return Task.CompletedTask; }
        public Task ChangeOwnerAsync(string path, string owner, string group, CancellationToken cancellationToken) { Log.Add($"chown {owner}:{group} {path}"); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }

    /// <summary>Acceso a lo privado de una clase enlazada (las tablas de textos).</summary>
    public static class Priv
    {
        public static T Static<T>(Type type, string name) =>
            (T)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    }
}
