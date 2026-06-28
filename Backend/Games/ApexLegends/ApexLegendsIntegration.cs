using Segra.Backend.Core.Models;

namespace Segra.Backend.Games.ApexLegends
{
    internal class ApexLegendsIntegration : OcrIntegration
    {
        protected override OcrConfig GetConfig() => new()
        {
            LogPrefix = "Apex",

            // Covers Apex's central/lower notification area plus the full champion banner.
            // CropRegion coordinates are normalized percentages of the captured game frame.
            // If events are missed, tune Y/Height to move or resize the crop before changing keywords.
            // Works best when Apex Legends' UI language is set to English.
            // OCR can produce false positives, so EventCooldown limits repeated bookmarks.
            CropRegion = new CropRegion(
                X: 0.15,
                Y: 0.28,
                Width: 0.70,
                Height: 0.52
            ),

            Threshold = 145,
            PollIntervalMs = 200,
            EventCooldown = TimeSpan.FromSeconds(2.5),
            ExcludeCheckWindow = TimeSpan.FromSeconds(1.0),
            TimeCompensation = TimeSpan.FromSeconds(0.8),

            Keywords =
            [
                new()
                {
                    Text = "KNOCKED DOWN",
                    BookmarkType = BookmarkType.Kill
                },
                new()
                {
                    Text = "ELIMINATED",
                    BookmarkType = BookmarkType.Kill,
                    ExcludeFragments = ["ENEMY SQUAD"]
                },
                new()
                {
                    Text = "ASSIST",
                    BookmarkType = BookmarkType.Assist
                },
                new()
                {
                    Text = "YOU ARE THE CHAMPION",
                    BookmarkType = BookmarkType.Goal
                },
                new()
                {
                    Text = "CHAMPION",
                    BookmarkType = BookmarkType.Goal
                }
            ]
        };
    }
}
