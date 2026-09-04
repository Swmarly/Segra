using Serilog;
using Segra.Backend.App;
using Segra.Backend.Core.Models;

namespace Segra.Backend.Media
{
    internal class AiService
    {
<<<<<<< HEAD
        public static async Task<HighlightCreationResult> CreateHighlight(string contentId)
=======
        public static async Task CreateHighlight(string contentId)
>>>>>>> upstream/main
        {
            string highlightId = Guid.NewGuid().ToString();
            Content? content = null;

            try
            {
                Log.Information($"Starting highlight creation for: {contentId}");

                content = AppState.Instance.Content.FirstOrDefault(x => x.Id == contentId);
                if (content == null)
                {
                    Log.Warning($"No content found matching id: {contentId}");
<<<<<<< HEAD
                    return HighlightCreationResult.NoSourceContent;
=======
                    return;
>>>>>>> upstream/main
                }

                int momentCount = content.Bookmarks.Count(b => b.Type.IncludeInHighlight());
                if (momentCount == 0)
                {
                    Log.Information($"No highlight bookmarks found for: {content.FileName}");
                    await SendProgress(highlightId, -1, "error", "No highlight moments found in this session", content);
                    return HighlightCreationResult.NoHighlightMoments;
                }

                await SendProgress(highlightId, 0, "processing", $"Found {momentCount} moments", content);

<<<<<<< HEAD
                return await HighlightService.CreateHighlightFromBookmarks(contentId, async (progress, message) =>
=======
                await HighlightService.CreateHighlightFromBookmarks(contentId, async (progress, message) =>
>>>>>>> upstream/main
                {
                    string status = progress < 0 ? "error" : progress >= 100 ? "done" : "processing";
                    await SendProgress(highlightId, progress, status, message, content);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error creating highlight for {contentId}");
                if (content != null)
                {
                    await SendProgress(highlightId, -1, "error", $"Error: {ex.Message}", content);
                }
                return HighlightCreationResult.Failed;
            }
        }

        private static async Task SendProgress(string id, int progress, string status, string message, Content content)
        {
            var progressMessage = new HighlightProgressMessage
            {
                Id = id,
                Progress = progress,
                Status = status,
                Message = message,
                Content = content
            };

            await MessageService.SendFrontendMessage("AiProgress", progressMessage);
        }
    }

    public class HighlightProgressMessage
    {
        public required string Id { get; set; }
        public required int Progress { get; set; }
        public required string Status { get; set; }
        public required string Message { get; set; }
        public required Content Content { get; set; }
    }
}
