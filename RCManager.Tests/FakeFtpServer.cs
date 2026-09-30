using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocRcManager.Tests;

/// <summary>
/// Un servidor FTP de mentira en 127.0.0.1 (puerto libre), en memoria, con lo justo para que
/// FluentFTP entre, liste (MLSD/MLST), baje, suba, borre, renombre y cambie permisos. No sale del
/// equipo ni toca el disco: es para probar <see cref="Files.FtpFileSystem"/> sin un servidor real.
/// </summary>
public sealed class FakeFtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Ficheros por ruta completa; los directorios son las rutas en <see cref="Dirs"/>.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Dirs { get; } = new(StringComparer.Ordinal) { "/" };
    public Dictionary<string, DateTime> Modified { get; } = new(StringComparer.Ordinal);

    /// <summary>Ordenes recibidas, en el orden en que llegan.</summary>
    public List<string> Commands { get; } = [];

    public string Password { get; set; } = "pw";
    public string Home { get; set; } = "/home";
    public bool Unix { get; set; } = true;
    public bool ChownAllowed { get; set; } = true;
    public bool FailRetr { get; set; }

    /// <summary>Anuncia MLST (listados MLSD); sin el, FluentFTP lista con LIST al estilo de ls.</summary>
    public bool Mlsd { get; set; } = true;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public FakeFtpServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public bool Received(string prefix)
    {
        lock (Commands) return Commands.Any(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = Task.Run(() => SessionAsync(client));
        }
    }

    private static string Parent(string path)
    {
        var i = path.TrimEnd('/').LastIndexOf('/');
        return i <= 0 ? "/" : path[..i];
    }

    private string Full(string cwd, string arg)
    {
        if (arg.Length == 0) return cwd;
        return arg.StartsWith('/') ? arg : (cwd.TrimEnd('/') + "/" + arg);
    }

    private string Fact(string path, bool dir)
    {
        var mod = Modified.TryGetValue(path, out var m) ? $"modify={m:yyyyMMddHHmmss};" : string.Empty;
        var unix = Unix ? (dir ? "UNIX.mode=0755;UNIX.owner=ftp;UNIX.group=ftp;" : "UNIX.mode=0644;UNIX.owner=pepe;UNIX.group=users;") : string.Empty;
        var size = dir ? string.Empty : $"size={Files[path].Length};";
        return $"type={(dir ? "dir" : "file")};{size}{mod}{unix} ";
    }

    private async Task SessionAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        TcpListener? pasv = null;
        var cwd = Home;
        string? renameFrom = null;

        async Task<Stream> DataAsync()
        {
            var c = await pasv!.AcceptTcpClientAsync();
            pasv.Stop();
            pasv = null;
            return c.GetStream();
        }

        try
        {
            await writer.WriteLineAsync("220 Fake FTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (Commands) Commands.Add(line);
                var sp = line.IndexOf(' ');
                var cmd = (sp < 0 ? line : line[..sp]).ToUpperInvariant();
                var arg = sp < 0 ? string.Empty : line[(sp + 1)..];
                switch (cmd)
                {
                    case "FEAT":
                        await writer.WriteLineAsync("211-Features:");
                        if (Mlsd)
                            await writer.WriteLineAsync(" MLST type*;size*;modify*;UNIX.mode*;UNIX.owner*;UNIX.group*;");
                        await writer.WriteLineAsync(" UTF8");
                        await writer.WriteLineAsync(" MFMT");
                        await writer.WriteLineAsync(" SIZE");
                        await writer.WriteLineAsync(" EPSV");
                        await writer.WriteLineAsync("211 End");
                        break;
                    case "USER": await writer.WriteLineAsync("331 Password"); break;
                    case "PASS":
                        await writer.WriteLineAsync(arg == Password ? "230 Logged in" : "530 Login incorrect");
                        break;
                    case "OPTS": case "TYPE": case "NOOP": case "CLNT": case "MODE": case "STRU":
                        await writer.WriteLineAsync("200 OK"); break;
                    case "SYST": await writer.WriteLineAsync(Unix ? "215 UNIX Type: L8" : "215 Windows_NT"); break;
                    case "PWD": await writer.WriteLineAsync($"257 \"{cwd}\""); break;
                    case "CWD":
                        var target = Full(cwd, arg);
                        if (Dirs.Contains(target)) { cwd = target; await writer.WriteLineAsync("250 OK"); }
                        else await writer.WriteLineAsync("550 No such directory");
                        break;
                    case "EPSV":
                        pasv = new TcpListener(IPAddress.Loopback, 0);
                        pasv.Start();
                        await writer.WriteLineAsync($"229 Entering Extended Passive Mode (|||{((IPEndPoint)pasv.LocalEndpoint).Port}|)");
                        break;
                    case "PASV":
                        pasv = new TcpListener(IPAddress.Loopback, 0);
                        pasv.Start();
                        var p = ((IPEndPoint)pasv.LocalEndpoint).Port;
                        await writer.WriteLineAsync($"227 Entering Passive Mode (127,0,0,1,{p / 256},{p % 256})");
                        break;
                    case "MLSD":
                    {
                        var dir = Full(cwd, arg);
                        await writer.WriteLineAsync("150 Listing");
                        using (var data = await DataAsync())
                        {
                            var sb = new StringBuilder();
                            sb.Append($"type=cdir;UNIX.mode=0755; {dir}\r\n");
                            foreach (var d in Dirs.Where(d => d != "/" && Parent(d) == dir))
                                sb.Append(Fact(d, true)).Append(d[(d.LastIndexOf('/') + 1)..]).Append("\r\n");
                            foreach (var f in Files.Keys.Where(f => Parent(f) == dir))
                                sb.Append(Fact(f, false)).Append(f[(f.LastIndexOf('/') + 1)..]).Append("\r\n");
                            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                            await data.WriteAsync(bytes);
                        }
                        await writer.WriteLineAsync("226 Done");
                        break;
                    }
                    case "LIST":
                    {
                        var dir = Full(cwd, arg.StartsWith('-') ? (arg.Split(' ', 2) is { Length: 2 } a ? a[1] : string.Empty) : arg);
                        await writer.WriteLineAsync("150 Listing");
                        using (var data = await DataAsync())
                        {
                            var sb = new StringBuilder();
                            foreach (var d in Dirs.Where(d => d != "/" && Parent(d) == dir))
                                sb.Append($"drwxr-xr-x    2 ftp      ftp          4096 Sep 01 12:00 {d[(d.LastIndexOf('/') + 1)..]}\r\n");
                            foreach (var f in Files.Keys.Where(f => Parent(f) == dir))
                                sb.Append($"-rw-r--r--    1 pepe     users  {Files[f].Length,12} Sep 01 12:00 {f[(f.LastIndexOf('/') + 1)..]}\r\n");
                            await data.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()));
                        }
                        await writer.WriteLineAsync("226 Done");
                        break;
                    }
                    case "MLST":
                    {
                        var path = Full(cwd, arg);
                        if (Files.ContainsKey(path) || Dirs.Contains(path))
                        {
                            await writer.WriteLineAsync("250-Listing");
                            await writer.WriteLineAsync(" " + Fact(path, Dirs.Contains(path)) + path);
                            await writer.WriteLineAsync("250 End");
                        }
                        else await writer.WriteLineAsync("550 Not found");
                        break;
                    }
                    case "SIZE":
                    {
                        var path = Full(cwd, arg);
                        await writer.WriteLineAsync(Files.TryGetValue(path, out var b) ? $"213 {b.Length}" : "550 Not found");
                        break;
                    }
                    case "MDTM":
                        await writer.WriteLineAsync("550 Not supported");
                        break;
                    case "RETR":
                    {
                        var path = Full(cwd, arg);
                        if (FailRetr || !Files.TryGetValue(path, out var b)) { await writer.WriteLineAsync("550 Not found"); break; }
                        await writer.WriteLineAsync("150 Sending");
                        using (var data = await DataAsync())
                            await data.WriteAsync(b);
                        await writer.WriteLineAsync("226 Done");
                        break;
                    }
                    case "STOR":
                    {
                        var path = Full(cwd, arg);
                        await writer.WriteLineAsync("150 Receiving");
                        using (var data = await DataAsync())
                        {
                            var ms = new MemoryStream();
                            await data.CopyToAsync(ms);
                            Files[path] = ms.ToArray();
                        }
                        await writer.WriteLineAsync("226 Done");
                        break;
                    }
                    case "MKD": Dirs.Add(Full(cwd, arg)); await writer.WriteLineAsync($"257 \"{Full(cwd, arg)}\" created"); break;
                    case "DELE":
                        await writer.WriteLineAsync(Files.Remove(Full(cwd, arg)) ? "250 Deleted" : "550 Not found"); break;
                    case "RMD":
                        await writer.WriteLineAsync(Dirs.Remove(Full(cwd, arg)) ? "250 Removed" : "550 Not found"); break;
                    case "RNFR": renameFrom = Full(cwd, arg); await writer.WriteLineAsync("350 Ready"); break;
                    case "RNTO":
                    {
                        var to = Full(cwd, arg);
                        if (renameFrom is not null && Files.Remove(renameFrom, out var b)) { Files[to] = b; await writer.WriteLineAsync("250 Renamed"); }
                        else await writer.WriteLineAsync("550 Not found");
                        break;
                    }
                    case "MFMT":
                    {
                        var parts = arg.Split(' ', 2);
                        Modified[Full(cwd, parts[1])] = DateTime.ParseExact(parts[0], "yyyyMMddHHmmss", null);
                        await writer.WriteLineAsync($"213 Modify={parts[0]}; {parts[1]}");
                        break;
                    }
                    case "SITE":
                        if (arg.StartsWith("CHOWN", StringComparison.OrdinalIgnoreCase) && !ChownAllowed)
                            await writer.WriteLineAsync("500 SITE CHOWN not understood");
                        else
                            await writer.WriteLineAsync("200 SITE OK");
                        break;
                    case "QUIT": await writer.WriteLineAsync("221 Bye"); return;
                    default: await writer.WriteLineAsync("502 Not implemented"); break;
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            pasv?.Stop();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
