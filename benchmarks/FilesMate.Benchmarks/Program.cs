using BenchmarkDotNet.Running;

using FilesMate.Benchmarks.Directories;

BenchmarkSwitcher.FromAssembly(typeof(DirectoryEnumerationBenchmarks).Assembly).Run(args);
