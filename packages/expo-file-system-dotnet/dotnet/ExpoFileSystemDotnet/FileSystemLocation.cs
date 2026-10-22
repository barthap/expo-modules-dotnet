using System.Text;

namespace ExpoFileSystemDotnet;

public enum PathFlavor
{
  Posix,
  Windows,
}

internal static class PathFlavors
{
  internal static PathFlavor Current =>
      OperatingSystem.IsWindows() ? PathFlavor.Windows : PathFlavor.Posix;
}

/// <summary>
/// A canonical, immutable local file system location. Parsing is pure string work
/// and never touches <c>System.IO.Path</c> or <c>System.Uri</c>, so behavior is the
/// same on every operating system for a given <see cref="PathFlavor"/>.
/// </summary>
internal sealed class FileSystemLocation
{
  private static readonly UTF8Encoding StrictUtf8 = new(false, true);
  private const string UnreservedUriChars = "-._~!$&'()*+,;=:@";

  private readonly PathFlavor _flavor;
  private readonly string? _host;
  private readonly string? _drive;
  private readonly string[] _segments;

  private FileSystemLocation(
      PathFlavor flavor,
      string? host,
      string? drive,
      string[] segments,
      bool isDirectory
  )
  {
    _flavor = flavor;
    _host = host;
    _drive = drive;
    _segments = segments;
    IsDirectory = isDirectory;
  }

  public bool IsDirectory { get; }

  public bool HasDirectoryPath => IsDirectory || _segments.Length == 0 || (IsUnc && _segments.Length == 1);

  public bool IsRoot => _segments.Length == Floor;

  public string Name => _segments.Length == 0 ? "" : _segments[^1];

  public string Uri =>
      "file://"
      + (_host ?? "")
      + "/"
      + (_drive is null ? "" : _drive + "/")
      + string.Join('/', _segments.Select(EncodeSegment))
      + (IsDirectory && _segments.Length > 0 ? "/" : "");

  public string NativePath
  {
    get
    {
      if (_host is not null)
      {
        return @"\\" + _host + @"\" + string.Join('\\', _segments);
      }
      if (_drive is not null)
      {
        return _drive + @"\" + string.Join('\\', _segments);
      }
      return "/" + string.Join('/', _segments);
    }
  }

  public FileSystemLocation Parent =>
      new(_flavor, _host, _drive, _segments.Length > Floor ? _segments[..^1] : _segments, true);

  private bool IsUnc => _host is not null;

  private int Floor => IsUnc ? 1 : 0;

  public static FileSystemLocation Parse(string input, bool isDirectory, PathFlavor flavor)
  {
    if (string.IsNullOrEmpty(input) || input.Contains('\0'))
    {
      throw FileSystemErrors.InvalidLocation(input ?? "");
    }

    if (!IsEncodable(input))
    {
      throw FileSystemErrors.InvalidLocation(input);
    }

    if (flavor == PathFlavor.Windows)
    {
      if (
          input.Length >= 4
          && IsSeparator(input[0])
          && IsSeparator(input[1])
          && input[2] is '.' or '?'
          && IsSeparator(input[3])
      )
      {
        throw FileSystemErrors.InvalidLocation(input);
      }
      if (
          input.Length >= 3
          && IsAsciiLetter(input[0])
          && input[1] == ':'
          && (input[2] == '\\' || input[2] == '/')
      )
      {
        var segments = input[3..].Split('\\', '/');
        return Build(flavor, null, DriveOf(input[0]), segments, isDirectory, input);
      }
      if (
          input.StartsWith(@"\\", StringComparison.Ordinal)
          || input.StartsWith("//", StringComparison.Ordinal)
      )
      {
        var parts = input[2..].Split('\\', '/').Where(part => part.Length > 0).ToArray();
        if (parts.Length < 2 || !IsValidHost(parts[0]))
        {
          throw FileSystemErrors.InvalidLocation(input);
        }
        return Build(flavor, parts[0], null, parts[1..], isDirectory, input);
      }
    }

    var schemeEnd = SchemeEnd(input);
    if (schemeEnd < 0)
    {
      if (flavor == PathFlavor.Posix && input[0] == '/')
      {
        return Build(flavor, null, null, input.Split('/'), isDirectory, input);
      }
      throw FileSystemErrors.InvalidLocation(input);
    }

    if (!input.AsSpan(0, schemeEnd).Equals("file", StringComparison.OrdinalIgnoreCase))
    {
      throw FileSystemErrors.InvalidLocation(input);
    }
    var rest = input[(schemeEnd + 1)..];
    if (
        !rest.StartsWith("//", StringComparison.Ordinal)
        || rest.Contains('?')
        || rest.Contains('#')
    )
    {
      throw FileSystemErrors.InvalidLocation(input);
    }
    rest = rest[2..];
    var slash = rest.IndexOf('/');
    var authority = slash < 0 ? rest : rest[..slash];
    var path = slash < 0 ? "" : rest[(slash + 1)..];
    var decoded = path.Split('/').Select(segment => DecodeSegment(segment, flavor, input)).ToList();

    if (flavor == PathFlavor.Posix)
    {
      if (authority.Length > 0)
      {
        throw FileSystemErrors.InvalidLocation(input);
      }
      return Build(flavor, null, null, decoded, isDirectory, input);
    }

    if (authority.Length > 0)
    {
      var host = DecodeSegment(authority, flavor, input);
      if (!IsValidHost(host))
      {
        throw FileSystemErrors.InvalidLocation(input);
      }
      return Build(flavor, host, null, decoded, isDirectory, input);
    }
    if (
        decoded.Count == 0
        || decoded[0].Length != 2
        || !IsAsciiLetter(decoded[0][0])
        || decoded[0][1] != ':'
    )
    {
      throw FileSystemErrors.InvalidLocation(input);
    }
    return Build(flavor, null, DriveOf(decoded[0][0]), decoded.Skip(1), isDirectory, input);
  }

  public FileSystemLocation Child(string name, bool isDirectory)
  {
    ValidateName(name, _flavor);
    return new FileSystemLocation(
        _flavor,
        _host,
        _drive,
        [.. _segments, name],
        isDirectory
    );
  }

  public FileSystemLocation WithName(string name) => Parent.Child(name, IsDirectory);

  public bool IsWithin(FileSystemLocation root)
  {
    if (
        _flavor != root._flavor
        || !string.Equals(_host, root._host, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(_drive, root._drive, StringComparison.OrdinalIgnoreCase)
        || root._segments.Length > _segments.Length
    )
    {
      return false;
    }
    for (var i = 0; i < root._segments.Length; i++)
    {
      if (!string.Equals(_segments[i], root._segments[i], StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }
    }
    return true;
  }

  public static void ValidateName(string name, PathFlavor flavor)
  {
    if (
        string.IsNullOrEmpty(name)
        || !IsEncodable(name)
        || name is "." or ".."
        || name.Contains('/')
        || name.Contains('\0')
        || (flavor == PathFlavor.Windows && (name.Contains('\\') || IsInvalidWindowsSegment(name)))
    )
    {
      throw FileSystemErrors.InvalidName(name ?? "");
    }
  }

  private static FileSystemLocation Build(
      PathFlavor flavor,
      string? host,
      string? drive,
      IEnumerable<string> rawSegments,
      bool isDirectory,
      string input
  )
  {
    var floor = host is null ? 0 : 1;
    var segments = new List<string>();
    foreach (var segment in rawSegments)
    {
      if (
          flavor == PathFlavor.Windows
          && segment.Length > 0
          && segment is not "." and not ".."
          && IsInvalidWindowsSegment(segment)
      )
      {
        throw FileSystemErrors.InvalidLocation(input);
      }
      if (segments.Count < floor)
      {
        // The UNC share is kept as-is and can never be a dot segment.
        if (segment.Length == 0 || segment is "." or "..")
        {
          throw FileSystemErrors.InvalidLocation(input);
        }
        segments.Add(segment);
      }
      else if (segment.Length == 0 || segment == ".")
      {
        continue;
      }
      else if (segment == "..")
      {
        if (segments.Count > floor)
        {
          segments.RemoveAt(segments.Count - 1);
        }
      }
      else
      {
        segments.Add(segment);
      }
    }
    return new FileSystemLocation(flavor, host, drive, [.. segments], isDirectory);
  }

  // Win32 strips trailing dots and spaces, so such a segment aliases another item. The
  // reserved characters include ':', which opens NTFS stream syntax (`X::$INDEX_ALLOCATION`)
  // that names directory `X` and would alias it past IsWithin. The drive designator is not a
  // segment, so it never reaches this check.
  private static bool IsInvalidWindowsSegment(string segment) =>
      segment[^1] is '.' or ' ' || segment.Any(c => c < ' ' || c is '<' or '>' or ':' or '"' or '|' or '?' or '*');

  private static int SchemeEnd(string input)
  {
    if (!IsAsciiLetter(input[0]))
    {
      return -1;
    }
    for (var i = 1; i < input.Length; i++)
    {
      var c = input[i];
      if (c == ':')
      {
        return i;
      }
      if (!(IsAsciiLetter(c) || char.IsAsciiDigit(c) || c is '+' or '.' or '-'))
      {
        return -1;
      }
    }
    return -1;
  }

  private static string DecodeSegment(string segment, PathFlavor flavor, string input)
  {
    var bytes = new List<byte>(segment.Length);
    var literal = new StringBuilder();
    void FlushLiteral()
    {
      if (literal.Length > 0)
      {
        bytes.AddRange(Encoding.UTF8.GetBytes(literal.ToString()));
        literal.Clear();
      }
    }

    for (var i = 0; i < segment.Length; i++)
    {
      if (segment[i] != '%')
      {
        literal.Append(segment[i]);
        continue;
      }
      if (i + 2 >= segment.Length)
      {
        throw FileSystemErrors.InvalidLocation(input);
      }
      if (!char.IsAsciiHexDigit(segment[i + 1]) || !char.IsAsciiHexDigit(segment[i + 2]))
      {
        throw FileSystemErrors.InvalidLocation(input);
      }
      FlushLiteral();
      bytes.Add(Convert.ToByte(segment.Substring(i + 1, 2), 16));
      i += 2;
    }
    FlushLiteral();

    string result;
    try
    {
      result = StrictUtf8.GetString([.. bytes]);
    }
    catch (DecoderFallbackException)
    {
      throw FileSystemErrors.InvalidLocation(input);
    }
    if (result.Contains('/') || result.Contains('\0') || (flavor == PathFlavor.Windows && result.Contains('\\')))
    {
      throw FileSystemErrors.InvalidLocation(input);
    }
    return result;
  }

  private static string EncodeSegment(string segment)
  {
    var builder = new StringBuilder(segment.Length);
    foreach (var b in StrictUtf8.GetBytes(segment))
    {
      var c = (char)b;
      if (b < 0x80 && (IsAsciiLetter(c) || char.IsAsciiDigit(c) || UnreservedUriChars.Contains(c)))
      {
        builder.Append(c);
      }
      else
      {
        builder.Append('%').Append(b.ToString("X2"));
      }
    }
    return builder.ToString();
  }

  private static bool IsSeparator(char c) => c is '\\' or '/';

  private static bool IsEncodable(string value)
  {
    try
    {
      StrictUtf8.GetByteCount(value);
      return true;
    }
    catch (EncoderFallbackException)
    {
      return false;
    }
  }

  private static bool IsValidHost(string host) =>
      host.Length > 0
      && host.Any(c => c != '.')
      && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

  private static bool IsAsciiLetter(char c) => char.IsAsciiLetter(c);

  private static string DriveOf(char letter) => char.ToUpperInvariant(letter) + ":";
}
