using System.IO;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Files;

/// <summary>Como va una transferencia: el fichero de ahora, cuanto lleva de cuanto y cuantos van a la vez.</summary>
public sealed record TransferProgress(bool Upload, string Name, long Done, long Total, int Active)
{
    /// <summary>0..100 para la barra.</summary>
    public double Percent => Total > 0 ? Math.Min(100, Done * 100.0 / Total) : 0;

    /// <summary>«Subiendo a.txt · 1 KB de 3 KB  ·  ×2».</summary>
    public string Text =>
        Loc.Format(Upload ? "FilesUploading" : "FilesDownloading", Name, FileRules.SizeText(Done), FileRules.SizeText(Total))
        + (Active > 1 ? $"  ·  ×{Active}" : string.Empty);
}

/// <summary>
/// Las transferencias entre este PC y el servidor (sin interfaz): medir lo que hay que llevar
/// (carpetas enteras), decidir que hacer con lo que ya existe, crear las carpetas y llevar los
/// ficheros, varios a la vez (cada uno con su propia conexion al servidor), con reintentos y
/// conservando las fechas si se pide.
/// </summary>
public sealed class FileTransfer(RemoteSide remote, Func<CancellationToken, Task<IRemoteFileSystem>> factory)
{
    private enum Conflict { Ask, Overwrite, Skip }

    // Conexiones para transferir: la principal (la del panel) y las que se abran ademas, hasta
    // el numero de transferencias a la vez. Se reutilizan.
    private readonly List<IRemoteFileSystem> _pool = [];
    private readonly Queue<IRemoteFileSystem> _idle = new();
    private readonly SemaphoreSlim _poolGate = new(1);

    public RemoteSide Remote { get; } = remote;

    /// <summary>Espera antes de reintentar un fichero que ha fallado.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Las conexiones abiertas para transferir (la principal incluida si se ha usado).</summary>
    public IReadOnlyList<IRemoteFileSystem> Pool => _pool;

    /// <summary>
    /// Lleva <paramref name="entries"/> de <paramref name="from"/> a <paramref name="targetDirectory"/>
    /// en <paramref name="to"/>. <paramref name="askConflict"/> pregunta por un fichero que ya existe
    /// (si la opcion de la conexion es preguntar); <paramref name="report"/> recibe el progreso.
    /// </summary>
    public async Task RunAsync(IFileSide from, IFileSide to, string targetDirectory, IReadOnlyList<FileEntry> entries, Connection options,
        Func<string, ConflictWindow.Answer> askConflict, Action<TransferProgress> report, CancellationToken token)
    {
        var upload = from.IsLocal;
        var parallel = Math.Clamp(options.TransferParallel, 1, 8);
        var retries = Math.Clamp(options.TransferRetries, 0, 5);

        // 1. Medir: que ficheros, a donde, cuantos bytes.
        var plan = new List<(FileEntry Entry, string Target)>();
        long total = 0;
        foreach (var entry in entries)
            total += await FileRules.PlanAsync(from, to, entry, targetDirectory, plan, token);

        // 2. Conflictos: lo que ya existe en el destino, segun la opcion de la conexion.
        var policy = (Conflict)Math.Clamp(options.TransferOnConflict, 0, 2);
        var files = new List<(FileEntry Entry, string Target)>();
        Conflict? forAll = null;
        foreach (var (entry, target) in plan.Where(p => !p.Entry.IsDirectory))
        {
            token.ThrowIfCancellationRequested();
            var exists = upload ? await Remote.Fs.StatAsync(target, token) is not null : File.Exists(target);
            if (!exists)
            {
                files.Add((entry, target));
                continue;
            }
            var decision = forAll ?? policy;
            if (decision == Conflict.Ask)
            {
                var answer = askConflict(entry.Name);
                if (answer.Cancel)
                    throw new OperationCanceledException();
                decision = answer.Overwrite ? Conflict.Overwrite : Conflict.Skip;
                if (answer.ApplyToAll)
                    forAll = decision;
            }
            if (decision == Conflict.Overwrite)
                files.Add((entry, target));
            else
                total -= entry.Size;
        }

        // 3. Carpetas primero, en orden (una dentro de otra), en la conexion principal.
        foreach (var (entry, target) in plan.Where(p => p.Entry.IsDirectory))
        {
            token.ThrowIfCancellationRequested();
            try { await to.CreateDirectoryAsync(target, token); } catch (Exception) { /* ya existe */ }
        }

        // 4. Ficheros, N a la vez, cada uno con su conexion (los clientes SFTP/FTP no admiten
        //    dos operaciones a la vez en la misma conexion).
        long done = 0;
        var active = 0;
        void Report(string name) => report(new TransferProgress(upload, name, Interlocked.Read(ref done), total, Volatile.Read(ref active)));

        using var gate = new SemaphoreSlim(parallel);
        var tasks = files.Select(async item =>
        {
            await gate.WaitAsync(token);
            var fs = await RentAsync(token);
            Interlocked.Increment(ref active);
            try
            {
                Report(item.Entry.Name);
                long mine = 0;
                var progress = new Progress<long>(delta =>
                {
                    Interlocked.Add(ref done, delta);
                    Interlocked.Add(ref mine, delta);
                    Report(item.Entry.Name);
                });
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (upload)
                            await fs.UploadAsync(item.Entry.FullPath, item.Target, progress, token);
                        else
                            await fs.DownloadAsync(item.Entry.FullPath, item.Target, progress, token);
                        break;
                    }
                    catch (Exception) when (attempt < retries && !token.IsCancellationRequested)
                    {
                        // Lo que se contó de este intento se descuenta y se vuelve a empezar.
                        Interlocked.Add(ref done, -Interlocked.Exchange(ref mine, 0));
                        await Task.Delay(RetryDelay, token);
                    }
                }
                if (options.TransferPreserveTimes && item.Entry.Modified is { } when)
                {
                    try
                    {
                        if (upload) await fs.SetModifiedAsync(item.Target, when, token);
                        else File.SetLastWriteTime(item.Target, when);
                    }
                    catch (Exception) { /* no todos los servidores lo admiten */ }
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
                Return(fs);
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task<IRemoteFileSystem> RentAsync(CancellationToken token)
    {
        await _poolGate.WaitAsync(token);
        try
        {
            if (_idle.Count > 0)
                return _idle.Dequeue();
            if (_pool.Count == 0)
            {
                _pool.Add(Remote.Fs);
                return Remote.Fs;
            }
            var fresh = await factory(token);
            _pool.Add(fresh);
            return fresh;
        }
        finally { _poolGate.Release(); }
    }

    private void Return(IRemoteFileSystem fs)
    {
        _poolGate.Wait();
        try { _idle.Enqueue(fs); }
        finally { _poolGate.Release(); }
    }

    /// <summary>Cierra las conexiones de transferir y la principal.</summary>
    public void Shutdown()
    {
        foreach (var fs in _pool)
            try { fs.Dispose(); } catch (Exception) { }
        if (!_pool.Contains(Remote.Fs))
            Remote.Fs.Dispose();
    }
}
