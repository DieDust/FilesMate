using FilesMate.TestDataGenerator;

if (args.Length == 0 || IsHelp(args[0]))
{
    PrintHelp();
    return args.Length == 0 ? 1 : 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "create" => RunCreate(args.AsSpan(1)),
        "clean" => RunClean(args.AsSpan(1)),
        _ => Fail($"Unknown command '{args[0]}'."),
    };
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DirectoryNotFoundException or IOException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static int RunCreate(ReadOnlySpan<string> args)
{
    var options = ParseCreate(args);
    Console.WriteLine($"Requested root  : {options.Root}");
    Console.WriteLine($"Profile         : {options.Profile}");
    Console.WriteLine($"Files           : {options.FileCount}");
    Console.WriteLine($"Directories     : {options.DirectoryCount}");
    Console.WriteLine($"Depth           : {options.Depth}");
    Console.WriteLine($"Seed            : {options.Seed}");

    var result = new DatasetGenerator().Create(options);
    Console.WriteLine($"Resolved target : {result.Root}");
    Console.WriteLine($"Created files   : {result.FileCount}");
    Console.WriteLine($"Created dirs    : {result.DirectoryCount}");
    Console.WriteLine($"Marker          : {result.MarkerPath}");
    return 0;
}

static int RunClean(ReadOnlySpan<string> args)
{
    var root = GetOption(args, "--root") ?? throw new ArgumentException("Missing --root.");
    _ = HasFlag(args, "--require-marker");
    Console.WriteLine($"Requested root  : {root}");
    Console.WriteLine("Require marker  : true");
    var resolved = PathGuard.ValidateExistingRoot(root);
    Console.WriteLine($"Resolved target : {resolved}");
    new DatasetGenerator().Clean(resolved, requireMarker: true);
    Console.WriteLine("Clean complete.");
    return 0;
}

static GenerationOptions ParseCreate(ReadOnlySpan<string> args)
{
    var root = GetOption(args, "--root") ?? throw new ArgumentException("Missing --root.");
    return new GenerationOptions
    {
        Root = root,
        FileCount = GetInt(args, "--files", 0),
        DirectoryCount = GetInt(args, "--directories", 0),
        Depth = GetInt(args, "--depth", 1),
        Seed = GetInt(args, "--seed", 1),
        Profile = ParseProfile(GetOption(args, "--profile") ?? "mixed"),
    };
}

static DatasetProfile ParseProfile(string value) => value.Trim().ToLowerInvariant() switch
{
    "empty" => DatasetProfile.Empty,
    "mixed" => DatasetProfile.Mixed,
    "images" => DatasetProfile.Images,
    "unicode" => DatasetProfile.Unicode,
    "long-paths" or "longpaths" => DatasetProfile.LongPaths,
    _ => throw new ArgumentException($"Unknown profile '{value}'."),
};

static string? GetOption(ReadOnlySpan<string> args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {name}.");
        }

        return args[i + 1];
    }

    return null;
}

static int GetInt(ReadOnlySpan<string> args, string name, int fallback)
{
    var text = GetOption(args, name);
    if (text is null)
    {
        return fallback;
    }

    if (!int.TryParse(text, out var value))
    {
        throw new ArgumentException($"Invalid integer for {name}: '{text}'.");
    }

    return value;
}

static bool HasFlag(ReadOnlySpan<string> args, string name)
{
    foreach (var arg in args)
    {
        if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static bool IsHelp(string value) =>
    value is "-h" or "--help" or "/?" or "help";

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        FilesMate.TestDataGenerator

        create --root <absolute-path> --files <n> --directories <n> --depth <n> --seed <n> --profile empty|mixed|images|unicode|long-paths
        clean  --root <absolute-path> --require-marker
        """);
}
