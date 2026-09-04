using Serilog;
using Segra.Backend.App;
using Segra.Backend.Core;
using Segra.Backend.Shared;
using System.Globalization;
using Segra.Backend.Core.Models;
using Segra.Backend.Windows.Storage;

namespace Segra.Backend.Media
{
    public enum HighlightCreationResult
    {
        Created,
        NoSourceContent,
        NoHighlightMoments,
        SourceFileMissing,
        Failed
    }

    /// <summary>
    /// Service for creating highlight videos from bookmarks using fast stream copy.
    /// </summary>
    public static class HighlightService
    {
        private const double KillChainMergeWindowSeconds = 15.0;

        /// <summary>
        /// Creates a highlight video from all highlight-worthy bookmarks (Kill, Goal, etc.).
        /// Uses stream copy for fast extraction without re-encoding.
        /// </summary>
<<<<<<< HEAD
        public static async Task<HighlightCreationResult> CreateHighlightFromBookmarks(string contentId, Action<int, string>? progressCallback = null)
=======
        public static async Task CreateHighlightFromBookmarks(string contentId, Action<int, string>? progressCallback = null)
>>>>>>> upstream/main
        {
            try
            {
                Log.Information($"Starting highlight creation for: {contentId}");

                Content? content = AppState.Instance.Content.FirstOrDefault(x => x.Id == contentId);
                if (content == null)
                {
                    Log.Warning($"No content found matching id: {contentId}");
<<<<<<< HEAD
                    return HighlightCreationResult.NoSourceContent;
=======
                    return;
>>>>>>> upstream/main
                }

                List<Bookmark> highlightBookmarks = content.Bookmarks
                    .Where(b => b.Type.IncludeInHighlight())
                    .OrderBy(b => b.Time)
                    .ToList();

                if (highlightBookmarks.Count == 0)
                {
                    Log.Information($"No highlight bookmarks found for: {content.FileName}");
                    progressCallback?.Invoke(-1, "No highlight moments found in this session");
                    return HighlightCreationResult.NoHighlightMoments;
                }

                Log.Information($"Found {highlightBookmarks.Count} bookmarks to include in highlight");
                progressCallback?.Invoke(5, $"Found {highlightBookmarks.Count} moments");

                double paddingBefore = Settings.Instance.HighlightPaddingBefore;
                double paddingAfter = Settings.Instance.HighlightPaddingAfter;
                var segments = CreateHighlightSegments(highlightBookmarks, paddingBefore, paddingAfter);

                var mergedSegments = MergeOverlappingSegments(segments);
                Log.Information($"Merged {segments.Count} segments into {mergedSegments.Count} clips");

                string videoFolder = Settings.Instance.ContentFolder;
                // Input files are organized by game
                string inputGameFolder = StorageService.SanitizeGameNameForFolder(content.Game ?? "Unknown");
                string inputFolderName = FolderNames.GetVideoFolderName(content.Type);
                string inputFilePath = PathUtils.Combine(videoFolder, inputFolderName, inputGameFolder, $"{content.FileName}.mp4");

                if (!File.Exists(inputFilePath))
                {
                    Log.Error($"Input video file not found: {inputFilePath}");
                    progressCallback?.Invoke(-1, "Source video not found");
                    return HighlightCreationResult.SourceFileMissing;
                }

                // Output highlights are organized by game
                string outputGameFolder = StorageService.SanitizeGameNameForFolder(content.Game ?? "Unknown");
                string outputFolder = PathUtils.Combine(videoFolder, FolderNames.Highlights, outputGameFolder);
                Directory.CreateDirectory(outputFolder);

                string outputFileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
                string outputFilePath = PathUtils.Combine(outputFolder, outputFileName);

                progressCallback?.Invoke(10, "Extracting clips...");

                // Extract and concatenate segments while preserving the requested audio layout.
                bool keepSeparateAudioTracks = Settings.Instance.HighlightKeepSeparateAudioTracks;
                var audioTrackNames = keepSeparateAudioTracks ? content.AudioTrackNames : null;
                bool success = await ExtractAndConcatenateSegments(
                    inputFilePath,
                    outputFilePath,
                    mergedSegments,
                    keepSeparateAudioTracks,
                    audioTrackNames,
                    (progress, message) => progressCallback?.Invoke(10 + (int)(progress * 80), message)
                );

                if (!success || !File.Exists(outputFilePath))
                {
                    Log.Error("Failed to create highlight video");
                    progressCallback?.Invoke(-1, "Failed to create highlight");
                    return HighlightCreationResult.Failed;
                }

                // Ensure the output is fully flushed (matters for network drives) before reading it back.
                await GeneralUtils.EnsureFileReady(outputFilePath);

                progressCallback?.Invoke(92, "Creating metadata...");

                // Create metadata, thumbnail, and waveform.
<<<<<<< HEAD
                // When enabled, highlights explicitly map all streams and preserve the source's audio tracks.
                string? highlightId = await ContentService.CreateMetadataFile(
                    outputFilePath,
                    Content.ContentType.Highlight,
                    content.Game!,
                    null,
                    content.Title,
                    igdbId: content.IgdbId,
                    audioTrackNames: audioTrackNames,
                    audioTrackTypes: keepSeparateAudioTracks ? content.AudioTrackTypes : null,
                    gameExePath: content.GameExePath);
=======
                // Highlights use stream-copy extract+concat, so they preserve the source's audio tracks.
                string? highlightId = await ContentService.CreateMetadataFile(outputFilePath, Content.ContentType.Highlight, content.Game!, null, content.Title, igdbId: content.IgdbId, audioTrackNames: content.AudioTrackNames, audioTrackTypes: content.AudioTrackTypes, gameExePath: content.GameExePath);
>>>>>>> upstream/main

                progressCallback?.Invoke(95, "Creating thumbnail...");
                await ContentService.CreateThumbnail(outputFilePath, Content.ContentType.Highlight, highlightId);

                progressCallback?.Invoke(98, "Creating waveform...");
                await ContentService.CreateWaveformFile(outputFilePath, Content.ContentType.Highlight, highlightId);

                // Load silently then await the state send before "Done" removes the loading card, so the
                // highlight is on screen first (avoids a skeleton-removed-before-content flicker).
                await SettingsService.LoadContentFromFolderIntoState(sendToFrontend: false);
                await MessageService.SendStateToFrontend("Highlight created");

                progressCallback?.Invoke(100, "Done");
                Log.Information($"Highlight created successfully: {outputFilePath}");
                return HighlightCreationResult.Created;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error creating highlight for {contentId}");
                progressCallback?.Invoke(-1, $"Error: {ex.Message}");
                return HighlightCreationResult.Failed;
            }
        }

        private static List<TimeSegment> CreateHighlightSegments(List<Bookmark> bookmarks, double paddingBefore, double paddingAfter)
        {
            var segments = new List<TimeSegment>();
            var sorted = bookmarks.OrderBy(b => b.Time).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var bookmark = sorted[i];
                if (!CanMergeIntoFightChain(bookmark.Type))
                {
                    segments.Add(CreateSegment(bookmark.Time.TotalSeconds, bookmark.Time.TotalSeconds, paddingBefore, paddingAfter));
                    continue;
                }

                double firstMomentTime = bookmark.Time.TotalSeconds;
                double lastMomentTime = firstMomentTime;

                while (i + 1 < sorted.Count &&
                       CanMergeIntoFightChain(sorted[i + 1].Type) &&
                       sorted[i + 1].Time.TotalSeconds - lastMomentTime <= KillChainMergeWindowSeconds)
                {
                    i++;
                    lastMomentTime = sorted[i].Time.TotalSeconds;
                }

                segments.Add(CreateSegment(firstMomentTime, lastMomentTime, paddingBefore, paddingAfter));
            }

            return segments;
        }

        private static bool CanMergeIntoFightChain(BookmarkType type)
        {
            return type == BookmarkType.Kill || type == BookmarkType.Goal;
        }

        private static TimeSegment CreateSegment(double startMomentSeconds, double endMomentSeconds, double paddingBefore, double paddingAfter)
        {
            return new TimeSegment
            {
                StartTime = Math.Max(0, startMomentSeconds - paddingBefore),
                EndTime = endMomentSeconds + paddingAfter
            };
        }

        /// <summary>
        /// Extracts multiple segments from a video and concatenates them using stream copy.
        /// This is a fast operation as it doesn't re-encode the video.
        /// </summary>
        /// <param name="inputFilePath">Path to the source video file</param>
        /// <param name="outputFilePath">Path for the output video file</param>
        /// <param name="segments">List of time segments to extract</param>
        /// <param name="progressCallback">Optional callback for progress updates (0.0 to 1.0)</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> ExtractAndConcatenateSegments(
            string inputFilePath,
            string outputFilePath,
            List<TimeSegment> segments,
            bool keepSeparateAudioTracks = false,
            List<string>? audioTrackNames = null,
            Action<double, string>? progressCallback = null)
        {
            if (!FFmpegService.FFmpegExists())
            {
                Log.Error($"FFmpeg executable not found");
                return false;
            }

            if (segments.Count == 0)
            {
                Log.Warning("No segments provided for extraction");
                return false;
            }

            List<string> tempFiles = new();
            string? concatFilePath = null;

            try
            {
                double totalDuration = segments.Sum(s => s.EndTime - s.StartTime);
                double processedDuration = 0;

                // Extract each segment to a temp file using stream copy
                for (int i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    string tempFile = PathUtils.Combine(Path.GetTempPath(), $"highlight_segment_{Guid.NewGuid()}.mp4");
                    double segmentDuration = segment.EndTime - segment.StartTime;

                    progressCallback?.Invoke(processedDuration / totalDuration, $"Extracting clip {i + 1} of {segments.Count}");

                    var arguments = new List<string>
                    {
                        "-y",
                        "-ss", segment.StartTime.ToString(CultureInfo.InvariantCulture),
                        "-t", segmentDuration.ToString(CultureInfo.InvariantCulture),
<<<<<<< HEAD
                        "-i", inputFilePath
                    };

                    if (keepSeparateAudioTracks)
                    {
                        arguments.AddRange(new[] { "-map", "0:v:0", "-map", "0:a?" });
                        if (audioTrackNames != null)
                        {
                            for (int trackIndex = 0; trackIndex < audioTrackNames.Count; trackIndex++)
                            {
                                arguments.AddRange(new[] { $"-metadata:s:a:{trackIndex}", $"title={audioTrackNames[trackIndex]}" });
                            }
                        }
                    }

                    arguments.AddRange(new[]
                    {
=======
                        "-i", inputFilePath,
                        "-map", "0",
>>>>>>> upstream/main
                        "-c", "copy",
                        "-avoid_negative_ts", "make_zero",
                        tempFile
                    });

                    await FFmpegService.RunSimple(arguments);

                    if (!File.Exists(tempFile))
                    {
                        Log.Error($"Failed to extract segment {i + 1}");
                        continue;
                    }

                    tempFiles.Add(tempFile);
                    processedDuration += segmentDuration;
                }

                if (tempFiles.Count == 0)
                {
                    Log.Error("No segments were successfully extracted");
                    return false;
                }

                progressCallback?.Invoke(0.9, "Combining clips...");

                // If only one segment, just move it to output
                if (tempFiles.Count == 1)
                {
                    File.Move(tempFiles[0], outputFilePath, overwrite: true);
                    tempFiles.Clear();
                    return true;
                }

                concatFilePath = PathUtils.Combine(Path.GetTempPath(), $"highlight_concat_{Guid.NewGuid()}.txt");
                var concatLines = tempFiles.Select(FFmpegService.BuildConcatListLine);
                await File.WriteAllLinesAsync(concatFilePath, concatLines);

                // Concatenate all segments. For separate audio tracks, keep video copied but
                // re-encode audio so every mapped track survives the concat cleanly.
                var concatArguments = new List<string>
                {
                    "-y",
                    "-f", "concat",
                    "-safe", "0",
<<<<<<< HEAD
                    "-i", concatFilePath
                };

                if (keepSeparateAudioTracks)
                {
                    concatArguments.AddRange(new[] { "-map", "0:v:0", "-map", "0:a?" });
                }

                if (keepSeparateAudioTracks)
                {
                    concatArguments.AddRange(new[]
                    {
                        "-c:v", "copy",
                        "-c:a", "aac",
                        "-b:a", Settings.Instance.ClipAudioQuality
                    });

                    if (audioTrackNames != null)
                    {
                        for (int trackIndex = 0; trackIndex < audioTrackNames.Count; trackIndex++)
                        {
                            concatArguments.AddRange(new[] { $"-metadata:s:a:{trackIndex}", $"title={audioTrackNames[trackIndex]}" });
                        }
                    }
                }
                else
                {
                    concatArguments.AddRange(new[] { "-c", "copy" });
                }

                concatArguments.AddRange(new[]
                {
=======
                    "-i", concatFilePath,
                    "-map", "0",
                    "-c", "copy",
>>>>>>> upstream/main
                    "-movflags", "+faststart",
                    outputFilePath
                });
                await FFmpegService.RunSimple(concatArguments);

                progressCallback?.Invoke(1.0, "Done");
                return File.Exists(outputFilePath);
            }
            catch (FFmpegException ffEx)
            {
                Log.Error(ffEx, "Error extracting and concatenating segments");
                _ = MessageService.ShowModal(
                    "Highlight creation failed",
                    FFmpegErrors.DescribeForUser(ffEx.ExitCode),
                    "error");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error extracting and concatenating segments");
                return false;
            }
            finally
            {
                foreach (var tempFile in tempFiles)
                {
                    try { File.Delete(tempFile); }
                    catch { /* ignore cleanup errors */ }
                }

                if (!string.IsNullOrEmpty(concatFilePath))
                {
                    try { File.Delete(concatFilePath); }
                    catch { /* ignore cleanup errors */ }
                }
            }
        }

        /// <summary>
        /// Merges overlapping time segments into continuous segments.
        /// </summary>
        private static List<TimeSegment> MergeOverlappingSegments(List<TimeSegment> segments)
        {
            if (segments.Count == 0) return [];

            var sorted = segments.OrderBy(s => s.StartTime).ToList();
            var merged = new List<TimeSegment>();

            var current = new TimeSegment
            {
                StartTime = sorted[0].StartTime,
                EndTime = sorted[0].EndTime
            };

            for (int i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];

                // Check if segments overlap or are adjacent
                if (current.EndTime >= next.StartTime)
                {
                    // Extend current segment
                    current.EndTime = Math.Max(current.EndTime, next.EndTime);
                }
                else
                {
                    // No overlap, save current and start new
                    merged.Add(current);
                    current = new TimeSegment
                    {
                        StartTime = next.StartTime,
                        EndTime = next.EndTime
                    };
                }
            }

            merged.Add(current);
            return merged;
        }
    }

    /// <summary>
    /// Represents a time segment in a video.
    /// </summary>
    public class TimeSegment
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }
    }
}
