using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CityHelper.Tools.PiggViewer.Core;

/// <summary>Raised for problems with a .pigg archive (bad signature, truncated, duplicate entry...).</summary>
public sealed class PiggException : Exception
{
    public PiggException(string message) : base(message) { }
}

/// <summary>One open .pigg file; entries read from it by absolute offset, so reads are safe from any thread.</summary>
public sealed class PiggArchive : IDisposable
{
    private readonly SafeFileHandle _handle;

    public PiggArchive(string path)
    {
        Path = path;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Length = RandomAccess.GetLength(_handle);
    }

    public string Path { get; }
    public long Length { get; }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(_handle, buffer[total..], offset + total);
            if (read <= 0)
                throw new PiggException("Unexpected end of archive");
            total += read;
        }
    }

    public void Dispose() => _handle.Dispose();
}

public abstract class VfsNode
{
    protected VfsNode(string name, DirectoryNode? parent)
    {
        Name = name;
        Parent = parent;
    }

    public string Name { get; }
    public DirectoryNode? Parent { get; }
}

public sealed class DirectoryNode : VfsNode
{
    public DirectoryNode(string name, DirectoryNode? parent) : base(name, parent) { }

    /// <summary>Ordinal ordering, like the std::map the original used.</summary>
    public SortedDictionary<string, VfsNode> Nodes { get; } = new(StringComparer.Ordinal);

    public IEnumerable<DirectoryNode> Directories => Nodes.Values.OfType<DirectoryNode>();
    public IEnumerable<FileNode> Files => Nodes.Values.OfType<FileNode>();

    /// <summary>Path below the mount root, e.g. "texture_library/GUI"; empty for the root.</summary>
    public string RelativePath
    {
        get
        {
            var parts = new List<string>();
            for (DirectoryNode? d = this; d?.Parent != null; d = d.Parent)
                parts.Add(d.Name);
            parts.Reverse();
            return string.Join('/', parts);
        }
    }

    public IEnumerable<FileNode> AllFiles() =>
        Files.Concat(Directories.SelectMany(d => d.AllFiles()));
}

public sealed class FileNode : VfsNode
{
    public FileNode(string name, DirectoryNode parent, PiggArchive archive, uint offset, uint storedSize, uint size, uint modified, bool packed)
        : base(name, parent)
    {
        Archive = archive;
        Offset = offset;
        StoredSize = storedSize;
        Size = size;
        Modified = modified;
        Packed = packed;
    }

    public PiggArchive Archive { get; }
    public uint Offset { get; }

    /// <summary>Bytes occupied in the archive (the compressed size when packed).</summary>
    public uint StoredSize { get; }

    /// <summary>Uncompressed size.</summary>
    public uint Size { get; }

    /// <summary>UTC time_t from the archive.</summary>
    public uint Modified { get; }

    public bool Packed { get; }

    public DateTime ModifiedLocal => DateTimeOffset.FromUnixTimeSeconds(Modified).LocalDateTime;

    /// <summary>The file's bytes, inflated if it is stored compressed.</summary>
    public byte[] Load()
    {
        var stored = new byte[StoredSize];
        Archive.ReadAt(Offset, stored);
        if (!Packed)
            return stored;

        var result = new byte[Size];
        using var zlib = new ZLibStream(new MemoryStream(stored), CompressionMode.Decompress);
        int total = 0;
        while (total < result.Length)
        {
            int read = zlib.Read(result, total, result.Length - total);
            if (read <= 0)
                throw new PiggException($"'{Name}' ended early while decompressing");
            total += read;
        }
        return result;
    }
}

/// <summary>A named byte range of a file's contents, used when one entry exports as several files (textures).</summary>
public sealed record ExportSegment(string Name, int Offset, int Size);

public enum MountType
{
    None,
    Archive,
    Folder,
}

/// <summary>
/// Virtual file system over .pigg archives: mounts one archive, or every archive in a folder, as a single tree.
/// Port of Piglet's Vfs (pig format: header, file headers, string pool, data pool, then the file data).
/// </summary>
public sealed class PiggVfs : IDisposable
{
    private const ushort PigVersion = 2;
    private const uint PigHeaderFlag = 0x00000123;
    private const uint PigFileHeaderFlag = 0x00003456;
    private const uint PigStringPoolFlag = 0x00006789;
    private const uint PigDataPoolFlag = 0x00009abc;

    private const int PigHeaderSize = 16;
    private const int PigFileHeaderSize = 48;
    private const int PoolHeaderSize = 12;

    private List<PiggArchive> _archives = new();

    public DirectoryNode? Root { get; private set; }
    public MountType MountType { get; private set; }
    public int ArchiveCount => _archives.Count;
    public string MountName => Root?.Name ?? "";

    // ------------------------------------------------------------- mounting

    /// <summary>Mounts an archive file, or every readable archive directly inside a folder. Replaces the current mount only on success.</summary>
    public void Mount(string path)
    {
        bool folder = Directory.Exists(path);
        var absolute = Path.GetFullPath(path);
        var root = new DirectoryNode(absolute, null);
        var archives = new List<PiggArchive>();

        try
        {
            if (!folder)
            {
                var archive = new PiggArchive(absolute);
                archives.Add(archive);
                MountArchive(archive, root);              // errors propagate for a single archive
            }
            else
            {
                foreach (var file in Directory.EnumerateFiles(absolute))
                {
                    PiggArchive? archive = null;
                    try
                    {
                        archive = new PiggArchive(file);
                        MountArchive(archive, root);
                        archives.Add(archive);
                    }
                    catch (Exception ex) when (ex is PiggException or IOException or UnauthorizedAccessException)
                    {
                        archive?.Dispose();               // not an archive: skipped, like the original
                    }
                }
            }
        }
        catch
        {
            foreach (var a in archives) a.Dispose();
            throw;
        }

        Unmount();
        _archives = archives;
        Root = root;
        MountType = folder ? MountType.Folder : MountType.Archive;
    }

    public void Unmount()
    {
        foreach (var a in _archives) a.Dispose();
        _archives = new List<PiggArchive>();
        Root = null;
        MountType = MountType.None;
    }

    public void Dispose() => Unmount();

    private static void MountArchive(PiggArchive archive, DirectoryNode root)
    {
        long archiveSize = archive.Length;
        if (archiveSize < PigHeaderSize)
            throw new PiggException("Archive too small");

        var headerBytes = new byte[PigHeaderSize];
        archive.ReadAt(0, headerBytes);
        uint flag = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes);
        ushort requiredReadVersion = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.AsSpan(6));
        ushort fileHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.AsSpan(10));
        uint numFiles = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(12));
        if (flag != PigHeaderFlag || requiredReadVersion > PigVersion)
            throw new PiggException("Invalid Pigg Archive");
        if (fileHeaderSize == 0 || (long)numFiles * fileHeaderSize > archiveSize)
            throw new PiggException("Corrupted archive");

        // File headers (the header size on disk may differ from the one this code knows)
        var files = new FileHeader[numFiles];
        var all = new byte[(int)numFiles * fileHeaderSize];
        archive.ReadAt(PigHeaderSize, all);
        for (int i = 0; i < files.Length; i++)
        {
            var span = new byte[PigFileHeaderSize];
            all.AsSpan(i * fileHeaderSize, Math.Min((int)fileHeaderSize, PigFileHeaderSize)).CopyTo(span);
            files[i] = FileHeader.Parse(span);
        }

        foreach (var file in files)
        {
            if (file.Flag != PigFileHeaderFlag)
                throw new PiggException("Invalid file header signature");
            long entrySize = file.PackSize != 0 ? file.PackSize : file.Size;
            if ((long)file.Offset + entrySize > archiveSize)
                throw new PiggException("Corrupted archive");
            if (file.NameId < 0)
                throw new PiggException("Bad name ID");
        }

        // String pool: one length-prefixed, NUL-terminated name per file
        long position = PigHeaderSize + (long)numFiles * fileHeaderSize;
        var pool = new byte[PoolHeaderSize];
        archive.ReadAt(position, pool);
        position += PoolHeaderSize;
        if (BinaryPrimitives.ReadUInt32LittleEndian(pool) != PigStringPoolFlag)
            throw new PiggException("Invalid string pool signature");

        long numStrings = BinaryPrimitives.ReadUInt32LittleEndian(pool.AsSpan(4));
        long entryCount = Math.Min(numStrings, files.Length);
        var sizeBuffer = new byte[4];
        for (int i = 0; i < entryCount; i++)
        {
            archive.ReadAt(position, sizeBuffer);
            position += 4;
            uint stringSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBuffer);
            if (stringSize == 0 || position + stringSize > archiveSize)
                throw new PiggException("Invalid string pool string size");

            var name = new byte[stringSize];
            archive.ReadAt(position, name);
            position += stringSize;
            AddNode(root, Encoding.UTF8.GetString(name, 0, (int)stringSize - 1), archive, files[i]);
        }

        archive.ReadAt(position, pool);
        if (BinaryPrimitives.ReadUInt32LittleEndian(pool) != PigDataPoolFlag)
            throw new PiggException("Invalid data pool signature");
    }

    private static void AddNode(DirectoryNode parent, string path, PiggArchive archive, FileHeader header)
    {
        var parts = path.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Length == 0)
                continue;
            if (!parent.Nodes.TryGetValue(parts[i], out var existing))
            {
                existing = new DirectoryNode(parts[i], parent);
                parent.Nodes.Add(parts[i], existing);
            }
            parent = existing as DirectoryNode
                ?? throw new PiggException($"'{parts[i]}' is both a file and a folder");
        }

        var name = parts[^1];
        if (name.Length == 0)
            return;

        bool packed = header.PackSize > 0;
        var file = new FileNode(name, parent, archive, header.Offset, packed ? header.PackSize : header.Size,
            header.Size, header.Timestamp, packed);
        if (!parent.Nodes.TryAdd(name, file))
            throw new PiggException($"Duplicate item '{path}' found");
    }

    private readonly record struct FileHeader(
        uint Flag, int NameId, uint Size, uint Timestamp, uint Offset, uint PackSize)
    {
        public static FileHeader Parse(ReadOnlySpan<byte> b) => new(
            BinaryPrimitives.ReadUInt32LittleEndian(b),
            BinaryPrimitives.ReadInt32LittleEndian(b[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[44..]));
    }

    // -------------------------------------------------------------- exporting

    /// <summary>Writes one file into <paramref name="destinationFolder"/>, keeping its modified time.</summary>
    public static void ExportFile(FileNode file, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        var target = Path.Combine(destinationFolder, file.Name);
        File.WriteAllBytes(target, file.Load());
        File.SetLastWriteTimeUtc(target, DateTimeOffset.FromUnixTimeSeconds(file.Modified).UtcDateTime);
    }

    public static void ExportFiles(IEnumerable<FileNode> files, string destinationFolder)
    {
        foreach (var file in files)
            ExportFile(file, destinationFolder);
    }

    /// <summary>Exports a folder with its sub folders; the folder's path below the mount root is recreated under the destination.</summary>
    public static void ExportDirectory(DirectoryNode directory, string destinationFolder)
    {
        var target = directory.RelativePath.Length == 0
            ? destinationFolder
            : Path.Combine(destinationFolder, directory.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        ExportRecursive(directory, target);
    }

    private static void ExportRecursive(DirectoryNode directory, string path)
    {
        Directory.CreateDirectory(path);
        foreach (var node in directory.Nodes.Values)
        {
            if (node is DirectoryNode sub)
                ExportRecursive(sub, Path.Combine(path, sub.Name));
            else
                ExportFile((FileNode)node, path);
        }
    }

    /// <summary>Exports parts of one file's contents under their own names (a texture becomes .hdr, .mip and image files).</summary>
    public static void ExportSegments(byte[] contents, IEnumerable<ExportSegment> segments, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        foreach (var segment in segments)
            File.WriteAllBytes(Path.Combine(destinationFolder, segment.Name), contents.AsSpan(segment.Offset, segment.Size).ToArray());
    }

    // ---------------------------------------------------------------- creating

    /// <summary>Case-insensitive ordering of archive paths (matches the game's pig.c comparator).</summary>
    private sealed class NtfsComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            x ??= ""; y ??= "";
            int n = Math.Min(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[i]));
                if (c != 0)
                    return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }

    /// <summary>
    /// Packs every file below <paramref name="directory"/> into a new .pigg. Entry names are the paths relative to the
    /// folder's parent (so they start with the folder's name). Files are deflated (zlib level 5) unless that saves less
    /// than 10%, in which case they are stored as they are.
    /// </summary>
    public static void CreateArchive(string directory, string archivePath, IProgress<string>? progress = null)
    {
        var directoryPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPath = Path.GetDirectoryName(directoryPath) ?? directoryPath;
        var rootLength = rootPath.TrimEnd(Path.DirectorySeparatorChar).Length + 1;

        var files = new SortedDictionary<string, string>(new NtfsComparer());
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            var name = file[rootLength..].Replace('\\', '/');
            files[name] = file;
        }

        long stringPoolSize = files.Keys.Sum(k => 4L + Encoding.UTF8.GetByteCount(k) + 1);

        using var archive = new FileStream(archivePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true);

        // Header, then room for the file headers which are filled in once the data is placed
        writer.Write(PigHeaderFlag);
        writer.Write(PigVersion);
        writer.Write(PigVersion);
        writer.Write((ushort)PigHeaderSize);
        writer.Write((ushort)PigFileHeaderSize);
        writer.Write((uint)files.Count);
        archive.Seek((long)PigFileHeaderSize * files.Count, SeekOrigin.Current);

        writer.Write(PigStringPoolFlag);
        writer.Write((uint)files.Count);
        writer.Write((uint)stringPoolSize);
        foreach (var name in files.Keys)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            writer.Write((uint)(bytes.Length + 1));
            writer.Write(bytes);
            writer.Write((byte)0);
        }

        writer.Write(PigDataPoolFlag);
        writer.Write(0u);
        writer.Write(0u);

        var headers = new List<byte[]>(files.Count);
        int index = 0;
        foreach (var (name, path) in files)
        {
            progress?.Report($"{index + 1} / {files.Count}: {name}");
            var source = File.ReadAllBytes(path);

            uint offset = (uint)archive.Position;
            var checksum = MD5.HashData(source);
            uint packSize = 0;

            var packed = Deflate(source);
            if (packed != null && packed.Length < (long)(source.Length * 0.9))
            {
                writer.Write(packed);
                packSize = (uint)packed.Length;
            }
            else
            {
                writer.Write(source);
            }

            var header = new byte[PigFileHeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(header, PigFileHeaderFlag);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), index);                 // name id
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)source.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12),
                (uint)new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeSeconds());
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), offset);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), -1);                    // header data id
            checksum.CopyTo(header.AsSpan(28));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), packSize);
            headers.Add(header);
            index++;
        }

        archive.Seek(PigHeaderSize, SeekOrigin.Begin);
        foreach (var header in headers)
            writer.Write(header);
    }

    private static byte[]? Deflate(byte[] source)
    {
        if (source.Length == 0)
            return null;
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, new ZLibCompressionOptions { CompressionLevel = 5 }, leaveOpen: true))
            zlib.Write(source);
        return output.ToArray();
    }
}
