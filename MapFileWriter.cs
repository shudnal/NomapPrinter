using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static NomapPrinter.NomapPrinter;

namespace NomapPrinter
{
    // Local saves encode the captured texture and write PNG bytes on one worker queue.
    // Results are reported on the main thread, outside the worker exception boundary.
    internal static class MapFileWriter
    {
        private static readonly object sync = new object();
        private static readonly Queue<(bool failed, string message)> completedWrites = new Queue<(bool, string)>();
        private static Task pendingWrites = Task.CompletedTask;

        public static void Enqueue(string filename, Texture2D texture)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("A map destination is required.", nameof(filename));
            // Called on the main thread; do not inspect Unity object properties later.
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            lock (sync)
            {
                // Always use the pool, never the caller's synchronization context.
                // Serialize the entire encode/write job, not just the disk operation.
                // A failed save must not prevent subsequent requests from running.
                pendingWrites = pendingWrites.ContinueWith(
                    _ => EncodeAndWriteFile(filename, texture),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        private static void EncodeAndWriteFile(string filename, Texture2D texture)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            byte[] png;

            try
            {
                // Deliberately use the same background Texture2D encoding approach as
                // the table export. This is not a snapshot or a thread-safety guarantee.
                png = ImageConversion.EncodeToPNG(texture);
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("PNG encoding returned no map data.");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                string timings = string.Format(CultureInfo.InvariantCulture,
                    "stage=encode, bytes=0, encodeMs={0:F2}, writeMs=0.00", stopwatch.Elapsed.TotalMilliseconds);
                lock (sync)
                    completedWrites.Enqueue((true, $"Saving map to local file error ({filename}; {timings}):\n{exception}"));

                // This is an auxiliary save. Keep the previous PNG and do not retry
                // encoding on the main thread or interrupt character saving.
                return;
            }

            stopwatch.Stop();
            WriteFile(filename, png, stopwatch.Elapsed.TotalMilliseconds);
        }

        private static void WriteFile(string filename, byte[] png, double encodeMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string temporaryFile = null;
            Exception failure = null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
                temporaryFile = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporaryFile, png);

                // Keep the last complete map intact if writing the replacement fails.
                // Both files are in the same directory; never delete the destination first.
                if (File.Exists(filename))
                    File.Replace(temporaryFile, filename, null);
                else
                    File.Move(temporaryFile, filename);

                temporaryFile = null;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (temporaryFile != null)
                {
                    try
                    {
                        File.Delete(temporaryFile);
                    }
                    catch (Exception cleanupException)
                    {
                        failure = new AggregateException("Map write and temporary file cleanup failed.", failure, cleanupException);
                    }
                }
            }

            stopwatch.Stop();
            string timings = string.Format(CultureInfo.InvariantCulture,
                "bytes={0}, encodeMs={1:F2}, writeMs={2:F2}", png.Length, encodeMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
            string message = failure == null
                ? $"Saved nomap data to {filename}: {timings}"
                : $"Saving map to local file error ({filename}; stage=write, {timings}):\n{failure}";

            lock (sync)
                completedWrites.Enqueue((failure != null, message));
        }

        public static void ReportCompletedWrites()
        {
            while (true)
            {
                (bool failed, string message) result;
                lock (sync)
                {
                    if (completedWrites.Count == 0)
                        return;

                    result = completedWrites.Dequeue();
                }

                if (result.failed)
                    LogWarning(result.message);
                else
                    LogInfo(result.message);
            }
        }

        public static void Flush()
        {
            Task lastWrite;
            lock (sync)
                lastWrite = pendingWrites;

            // Only used on shutdown/reload, never from the autosave path. This now
            // waits for both encoding and disk I/O. No main-thread callback is queued
            // by our worker; Unity still controls the native encoder behavior.
            try
            {
                lastWrite.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                LogWarning($"Waiting for pending map writes failed: {exception}");
            }

            ReportCompletedWrites();
        }
    }
}
