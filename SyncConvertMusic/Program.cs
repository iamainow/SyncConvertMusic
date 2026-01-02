using System.Diagnostics;

internal class Program
{
    internal static void SyncMusic(Parameters parameters)
    {
        const string destinationExtension = ".mp3";
        string argumentsTemplate(string source, string destination)
        {
            return $"""
                -y -i "{source}" -codec:a libmp3lame -q:a {parameters.Quality} "{destination}"
                """;
        }

        if (!Directory.Exists(parameters.Destination))
        {
            Directory.CreateDirectory(parameters.Destination);
        }

        bool filterOutHiddenReadonlySystem(DirectoryInfo directoryInfo)
        {
            return !directoryInfo.Attributes.HasFlag(FileAttributes.Hidden) &&
                !directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly) &&
                !directoryInfo.Attributes.HasFlag(FileAttributes.System);
        }

        var sourceDirectoriesRelativePaths = Directory.EnumerateDirectories(parameters.Source, "*", SearchOption.AllDirectories)
            .Where(path => filterOutHiddenReadonlySystem(new DirectoryInfo(path)))
            .Select(path => Path.GetRelativePath(parameters.Source, path))
            .ToHashSet();

        var destinationDirectoriesRelativePaths = Directory.EnumerateDirectories(parameters.Destination, "*", SearchOption.AllDirectories)
            .Where(path => filterOutHiddenReadonlySystem(new DirectoryInfo(path)))
            .Select(path => Path.GetRelativePath(parameters.Destination, path))
            .ToHashSet();

        var directoriesToCreate = sourceDirectoriesRelativePaths
            .Where(x => !destinationDirectoriesRelativePaths.Contains(x))
            .Select(x => Path.Combine(parameters.Destination, x))
            .ToArray();

        var directoriesToDelete = destinationDirectoriesRelativePaths
            .Where(x => !sourceDirectoriesRelativePaths.Contains(x))
            .Select(x => Path.Combine(parameters.Destination, x))
            .ToArray();

        foreach (string directoryToCreate in directoriesToCreate)
        {
            Directory.CreateDirectory(directoryToCreate);
        }

        var sourceFilesRelativePathsWithoutExtensions = Enumerable.Concat(
            Directory.EnumerateFiles(parameters.Source, $"*{parameters.SourceExtension}", SearchOption.AllDirectories)
                .Where(path => filterOutHiddenReadonlySystem(new DirectoryInfo(path)))
                .Select(path => Path.GetRelativePath(parameters.Source, path)[..^parameters.SourceExtension.Length]),
            Directory.EnumerateFiles(parameters.Source, $"*{destinationExtension}", SearchOption.AllDirectories)
                .Where(path => filterOutHiddenReadonlySystem(new DirectoryInfo(path)))
                .Select(path => Path.GetRelativePath(parameters.Source, path)[..^destinationExtension.Length])
            ).ToHashSet();

        var destinationFilesRelativePathsWithoutExtensions = Directory.EnumerateFiles(parameters.Destination, $"*{destinationExtension}", SearchOption.AllDirectories)
            .Where(path => filterOutHiddenReadonlySystem(new DirectoryInfo(path)))
            .Select(path => Path.GetRelativePath(parameters.Destination, path)[..^destinationExtension.Length])
            .ToHashSet();

        string[] filesToDelete = destinationFilesRelativePathsWithoutExtensions
            .Where(x => !sourceFilesRelativePathsWithoutExtensions.Contains(x))
            .Select(x => Path.Combine(parameters.Destination, x + destinationExtension))
            .ToArray();

        foreach (string fileToDelete in filesToDelete)
        {
            File.Delete(fileToDelete);
        }

        foreach (string directoryToDelete in directoriesToDelete.OrderByDescending(x => x.Length))
        {
            Directory.Delete(directoryToDelete);
        }

        var filesToConvert = sourceFilesRelativePathsWithoutExtensions
            .Where(x => !destinationFilesRelativePathsWithoutExtensions.Contains(x))
            .Select(x => new
            {
                Source = Path.Combine(parameters.Source, x + parameters.SourceExtension),
                Destination = Path.Combine(parameters.Destination, x + destinationExtension),
            })
            .Where(x => File.Exists(x.Source))
            .ToArray();

        var filesToCopy = sourceFilesRelativePathsWithoutExtensions
            .Where(x => !destinationFilesRelativePathsWithoutExtensions.Contains(x))
            .Select(x => new
            {
                Source = Path.Combine(parameters.Source, x + destinationExtension),
                Destination = Path.Combine(parameters.Destination, x + destinationExtension),
            })
            .Where(x => File.Exists(x.Source))
            .ToArray();

        foreach (var fileToCopy in filesToCopy)
        {
            File.Copy(fileToCopy.Source, fileToCopy.Destination);
        }

        Parallel.ForEach(filesToConvert, fileToConvert =>
        {
            using Process process = Process.Start(parameters.ConverterExe, argumentsTemplate(fileToConvert.Source, fileToConvert.Destination));
            process.WaitForExit();
        });
    }

    internal static void Main(string[] args)
    {
        Parameters? parameters = new ParametersBuilder().Parse(args);
        if (parameters is null) return;
        SyncMusic(parameters);
    }

    internal class InternalParameters
    {
        public int? Quality { get; set; }
        public string? Source { get; set; }
        public string? Destination { get; set; }
        public string? SourceExtension { get; set; }
        public string? ConverterExe { get; set; }
        public Parameters ToParameters()
        {
            ArgumentNullException.ThrowIfNull(Quality);
            ArgumentNullException.ThrowIfNull(Source);
            ArgumentNullException.ThrowIfNull(Destination);
            ArgumentNullException.ThrowIfNull(SourceExtension);
            ArgumentNullException.ThrowIfNull(ConverterExe);

            return new Parameters
            {
                Quality = Quality.Value,
                Source = Source,
                Destination = Destination,
                SourceExtension = SourceExtension,
                ConverterExe = ConverterExe,
            };
        }
    }

    public class Parameters
    {
        public required int Quality { get; set; }
        public required string Source { get; set; }
        public required string Destination { get; set; }
        public required string SourceExtension { get; set; }
        public required string ConverterExe { get; set; }
    }

    public class ParametersBuilder
    {
        public Parameters? Parse(ReadOnlySpan<string> args)
        {
            var enumerator = args.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                Console.Write("""
                    -quality x : where x = [0, 10] - quality of converted audio files
                    -source-directory <path> : source directory
                    -dest-directory <path> : destination directory
                    -source-ext <ext> : source extension e.g. .flac, .wav
                    -ffmpeg <path> : path to ffmpeg executable
                    """);

                return null;
            }

            InternalParameters result = new();
            do
            {
                switch (enumerator.Current)
                {
                    case "-quality":
                        if (!enumerator.MoveNext())
                        {
                            throw new ArgumentException("missing -quality value, should use -quality [0, 10]");
                        }

                        if (!int.TryParse(enumerator.Current, out int quality))
                        {
                            throw new ArgumentException("-quality should be a number [0, 10]");
                        }

                        result.Quality = quality;
                        break;

                    case "-source-directory":
                        if (!enumerator.MoveNext())
                        {
                            throw new ArgumentException("missing -source-directory value, should use -source-directory <path>");
                        }

                        if (string.IsNullOrEmpty(enumerator.Current))
                        {
                            throw new ArgumentException("missing -source-directory value, should use -source-directory <path>");
                        }

                        result.Source = enumerator.Current;
                        break;

                    case "-dest-directory":
                        if (!enumerator.MoveNext())
                        {
                            throw new ArgumentException("missing -dest-directory value, should use -dest-directory <path>");
                        }

                        if (string.IsNullOrEmpty(enumerator.Current))
                        {
                            throw new ArgumentException("missing -dest-directory value, should use -dest-directory <path>");
                        }

                        result.Destination = enumerator.Current;
                        break;

                    case "-source-ext":
                        if (!enumerator.MoveNext())
                        {
                            throw new ArgumentException("missing -source-ext value, should use -source-ext <ext>");
                        }

                        if (string.IsNullOrEmpty(enumerator.Current))
                        {
                            throw new ArgumentException("missing -source-ext value, should use -source-ext <ext>");
                        }

                        if (!enumerator.Current.StartsWith("."))
                        {
                            throw new ArgumentException("-source-ext value should start with '.'");
                        }

                        result.SourceExtension = enumerator.Current;
                        break;

                    case "-ffmpeg":
                        if (!enumerator.MoveNext())
                        {
                            throw new ArgumentException("missing -ffmpeg value, should use -ffmpeg <path>");
                        }

                        if (string.IsNullOrEmpty(enumerator.Current))
                        {
                            throw new ArgumentException("missing -ffmpeg value, should use -ffmpeg <path>");
                        }

                        result.ConverterExe = enumerator.Current;
                        break;

                    default:
                        throw new ArgumentException($"unknown argument '{enumerator.Current}'");
                }
            } while (enumerator.MoveNext());

            return result.ToParameters();
        }
    }
}