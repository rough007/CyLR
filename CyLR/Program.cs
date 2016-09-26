﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CyLR.read;
using DiscUtils;
using System.Collections.Generic;
using System.Reflection;
using CyLR.archive;

namespace CyLR
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Arguments arguments;
            try
            {
                arguments = new Arguments(args);
            }
            catch (ArgumentException e)
            {
                Console.WriteLine(e.Message);
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unknown error while parsing arguments: {e.Message}");
                return;
            }

            if (arguments.HelpRequested)
            {
                Console.WriteLine(arguments.GetHelp(arguments.HelpTopic));
                return;
            }

            Dictionary<char, List<string>> paths;
            try
            {
                paths = CollectionPaths.GetPaths(arguments);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error occured while collecting files:\n{e}");
                return;
            }


            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                var archiveStream = Stream.Null;
                if (!arguments.DryRun)
                {
                    archiveStream = arguments.SFTPInMemory
                        ? new MemoryStream()
                        : OpenFileStream($@"{arguments.OutputPath}\{Environment.MachineName}.zip");
                }
                using (archiveStream)
                {
                    CreateArchive(archiveStream, paths);

                    if (arguments.SFTPCheck)
                    {
                        SendViaSftp(arguments, archiveStream);
                    }
                }
                if (arguments.SFTPCheck)
                {
                    if (File.Exists($@"{arguments.OutputPath}\{Environment.MachineName}.zip"))
                    {
                        File.Delete($@"{arguments.OutputPath}\{Environment.MachineName}.zip");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error occured while collecting files:\n{e}");
            }

            stopwatch.Stop();
            Console.WriteLine("Extraction complete. {0} elapsed", new TimeSpan(stopwatch.ElapsedTicks).ToString("g"));
        }
        
        /// <summary>
        /// Creates a zip archive containing all files from provided paths.
        /// </summary>
        /// <param name="archiveStream">The Stream the archive will be written to.</param>
        /// <param name="paths">Map of driveLetter->path for all files to collect.</param>
        private static void CreateArchive(Stream archiveStream, Dictionary<char, List<string>> paths)
        {
#if DOT_NET_4_0
            using (var archive = new SharpZipArchive(stream))
#else
            using (var archive = new NativeArchive(archiveStream))
#endif
            {
                foreach (var drive in paths)
                {
                    var driveName = drive.Key;
                    var system = FileSystem.GetFileSystem(drive.Key, FileAccess.Read);

                    var files = drive.Value
                        .SelectMany(path => system.GetFilesFromPath(path))
                        .Select(file => new Tuple<string, DiscFileInfo>($"{driveName}\\{file.FullName}", file));

                    archive.CollectFilesToArchive(files);
                }
            }
        }

        /// <summary>
        /// Sends a stream via SFTP, using configuration from the arguments.
        /// </summary>
        /// <param name="arguments">The arguments to use to connect to the SFTP server.</param>
        /// <param name="stream">The stream of data to send.</param>
        private static void SendViaSftp(Arguments arguments, Stream stream)
        {
            int port;
            var server = arguments.SFTPServer.Split(':');
            try
            {
                port = int.Parse(server[1]);
            }
            catch (Exception)
            {
                port = 22;
            }

            Sftp.Sftp.SendUsingSftp(stream, server[0], port, arguments.UserName, arguments.UserPassword,
                $@"{arguments.OutputPath}/{Environment.MachineName}.zip");
        }

        /// <summary>
        /// Opens a file for reading and writing, creating any missing directories in the path.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <returns>The file Stream.</returns>
        private static Stream OpenFileStream(string path)
        {
            var archiveFile = new FileInfo(path);
            if (archiveFile.Directory != null && !archiveFile.Directory.Exists)
            {
                archiveFile.Directory.Create();
            }
            return File.Open(archiveFile.FullName, FileMode.Create, FileAccess.ReadWrite); //TODO: Replace with non-api call
        }
    }
}
