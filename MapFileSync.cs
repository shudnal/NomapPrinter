using BepInEx;
using System;
using System.IO;
using System.Reflection;
using static NomapPrinter.NomapPrinter;

namespace NomapPrinter
{
    // Shared map transport is byte-only and is also needed without a HUD or graphics device.
    internal static class MapFileSync
    {
        private static FileSystemWatcher fileSystemWatcher;
        private static bool worldActive;

        public static void Start()
        {
            worldActive = true;
            SetupSharedMapFileWatcher();
        }

        public static void Stop()
        {
            worldActive = false;
            DisposeWatcher();
        }

        private static void DisposeWatcher()
        {
            FileSystemWatcher previous = fileSystemWatcher;
            fileSystemWatcher = null;
            previous?.Dispose();
        }

        public static void SetupSharedMapFileWatcher()
        {
            DisposeWatcher();

            if (!worldActive || mapStorage.Value != MapStorage.LoadFromSharedFile || sharedFile.Value.IsNullOrWhiteSpace())
                return;

            try
            {
                string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string filename = Path.GetFullPath(Path.IsPathRooted(sharedFile.Value)
                    ? sharedFile.Value
                    : Path.Combine(pluginFolder, sharedFile.Value));
                string directory = Path.GetDirectoryName(filename);

                if (!Directory.Exists(directory))
                {
                    LogWarning($"Shared map directory does not exist: {directory}");
                    return;
                }

                FileSystemWatcher watcher = new FileSystemWatcher(directory, Path.GetFileName(filename));
                fileSystemWatcher = watcher;
                watcher.Changed += MapFileChanged;
                watcher.Created += MapFileChanged;
                watcher.Deleted += MapFileChanged;
                watcher.Renamed += MapFileChanged;
                watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                watcher.EnableRaisingEvents = true;

                LogInfo($"Watcher active: {filename}");
                AssignMapDataFromSharedFile(filename);
            }
            catch (Exception exception)
            {
                DisposeWatcher();
                LogWarning($"Could not watch shared map file ({sharedFile.Value}): {exception.Message}");
            }
        }

        private static void MapFileChanged(object sender, FileSystemEventArgs args)
        {
            // Disposed watchers may already have callbacks queued on the main thread.
            if (!worldActive || !ReferenceEquals(sender, fileSystemWatcher))
                return;

            AssignMapDataFromSharedFile(Path.Combine(fileSystemWatcher.Path, fileSystemWatcher.Filter));
        }

        private static void AssignMapDataFromSharedFile(string filename)
        {
            string fileData = "";
            if (File.Exists(filename))
            {
                try
                {
                    fileData = Convert.ToBase64String(File.ReadAllBytes(filename));
                }
                catch (Exception exception)
                {
                    // Preserve the last complete value while an external writer owns the file.
                    LogWarning($"Error reading shared map file ({filename}): {exception.Message}");
                    return;
                }
            }
            else
                LogInfo($"Can't find file ({filename})!");

            mapDataFromFile.AssignLocalValue(fileData);
        }
    }
}
