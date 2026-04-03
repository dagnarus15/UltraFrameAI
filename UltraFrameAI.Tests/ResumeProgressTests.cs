using System.Reflection;
using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class ResumeProgressTests
{
    [Fact]
    public void PipelineService_EstimateResumeLoadingEta_UsesLongerCountdownAndExtension()
    {
        var tempRoot = TestSupport.CreateTempDirectory();
        try
        {
            var tempFile = Path.Combine(tempRoot, "partial.mkv");
            File.WriteAllBytes(tempFile, new byte[1024 * 1024]);

            var method = typeof(PipelineService).GetMethod("EstimateResumeLoadingEta", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var etaAt42 = (string)(method!.Invoke(null, new object[] { tempFile, TimeSpan.FromSeconds(42) }) ?? string.Empty);
            var etaPastZero = (string)(method.Invoke(null, new object[] { tempFile, TimeSpan.FromSeconds(61) }) ?? string.Empty);

            Assert.Equal("~00:00:18", etaAt42);
            Assert.Equal("~00:00:14", etaPastZero);
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void MainViewModel_ApplyResumePreflightStatus_ShowsLoadingFileAndExplicitFps()
    {
        var originalLanguage = LocalizedStrings.CurrentLanguage;
        LocalizedStrings.SetLanguage(UiLanguage.English);

        var tempRoot = TestSupport.CreateTempDirectory();
        try
        {
            var tempFile = Path.Combine(tempRoot, "partial.mkv");
            File.WriteAllBytes(tempFile, new byte[1024 * 1024]);

            var viewModel = new MainViewModel(persistUserState: false);
            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "partial.mkv",
                SourcePath = tempFile,
                OutputPath = tempFile
            };

            var method = typeof(MainViewModel).GetMethod("ApplyResumePreflightStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(
                viewModel,
                new object?[]
                {
                    item,
                    LocalizedStrings.Get("ResumePreflightRecovering"),
                    LocalizedStrings.Get("ResumePreflightLoadingFile"),
                    TimeSpan.FromSeconds(42),
                    null,
                    "--"
                });

            Assert.Equal(LocalizedStrings.Get("ResumePreflightLoadingFile"), viewModel.CurrentItemDetail);
            Assert.Equal(LocalizedStrings.Get("ResumePreflightLoadingFile"), item.Detail);
            Assert.True(viewModel.IsRenderPreviewLoading);
            Assert.Equal(LocalizedStrings.Get("ResumePreflightLoadingFile"), viewModel.RenderPreviewLoadingText);
            Assert.Equal("00:00:42", viewModel.ElapsedText);
            Assert.Equal("00:00:42", item.ElapsedText);
            Assert.Equal("--", viewModel.ProcessingFpsText);
            Assert.Equal("0%", item.ProgressText);
            Assert.StartsWith("~", viewModel.EtaText);
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
            LocalizedStrings.SetLanguage(originalLanguage);
        }
    }
}
